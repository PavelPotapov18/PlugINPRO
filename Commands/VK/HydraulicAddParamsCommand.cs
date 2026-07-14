using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace PlugINPRO.Commands.VK
{
    [Transaction(TransactionMode.Manual)]
    public class HydraulicAddParamsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document doc = uiapp.ActiveUIDocument.Document;

            try
            {
                // 1. Спрашиваем путь к ФОП файлу
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Title = "Выберите файл общих параметров (Shared Parameters File)";
                openFileDialog.Filter = "TXT files (*.txt)|*.txt|All files (*.*)|*.*";
                openFileDialog.CheckFileExists = true;

                if (openFileDialog.ShowDialog() != DialogResult.OK)
                {
                    TaskDialog.Show("Отмена", "Операция отменена пользователем");
                    return Result.Cancelled;
                }

                string sharedParamsFilePath = openFileDialog.FileName;

                // 2. Добавляем параметры
                AddHydraulicParameters(doc, sharedParamsFilePath);

                TaskDialog.Show("Успешно", "Параметры успешно добавлены в проект!");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Ошибка", $"Не удалось добавить параметры:\n{ex.Message}");
                return Result.Failed;
            }
        }

        private void AddHydraulicParameters(Document doc, string sharedParamsFilePath)
        {
            // Устанавливаем файл общих параметров
            doc.Application.SharedParametersFilename = sharedParamsFilePath;

            // Открываем файл
            DefinitionFile definitionFile = doc.Application.OpenSharedParameterFile();

            if (definitionFile == null)
            {
                throw new Exception("Не удалось открыть файл общих параметров");
            }

            // Сопоставление групп и категорий
            var groupToCategory = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
            {
                { "Pipes", BuiltInCategory.OST_PipeCurves },
                { "Pipe Fittings", BuiltInCategory.OST_PipeFitting },
                { "Plumbing Fixtures", BuiltInCategory.OST_PlumbingFixtures },
                { "Piping Systems", BuiltInCategory.OST_PipingSystem },
                { "Project", BuiltInCategory.OST_ProjectInformation }
            };

            using (Transaction tx = new Transaction(doc, "Добавление общих параметров"))
            {
                tx.Start();

                foreach (DefinitionGroup group in definitionFile.Groups)
                {
                    string groupName = group.Name;

                    if (groupToCategory.TryGetValue(groupName, out BuiltInCategory category))
                    {
                        Category targetCategory = doc.Settings.Categories.get_Item(category);

                        if (targetCategory == null)
                        {
                            TaskDialog.Show("Внимание", $"Категория {category} не найдена");
                            continue;
                        }

                        if (!targetCategory.AllowsBoundParameters)
                        {
                            TaskDialog.Show("Внимание", $"Категория {targetCategory.Name} не поддерживает привязку параметров");
                            continue;
                        }

                        AddParametersFromGroup(doc, group, targetCategory);
                    }
                    else
                    {
                        TaskDialog.Show("Внимание", $"Группа '{groupName}' не сопоставлена. Параметры не добавлены.");
                    }
                }

                tx.Commit();
            }
        }

        private void AddParametersFromGroup(Document doc, DefinitionGroup group, Category targetCategory)
        {
            // Получаем существующие параметры в категории
            HashSet<string> existingParams = GetExistingParameterNames(doc, targetCategory);

            foreach (ExternalDefinition definition in group.Definitions)
            {
                string paramName = definition.Name;

                if (existingParams.Contains(paramName)) 
                {
                    TaskDialog.Show("Пропущено", $"Параметр '{paramName}' уже существует в категории {targetCategory.Name}");
                    continue;
                }

                // Создаём Instance Binding
                CategorySet categorySet = new CategorySet();
                categorySet.Insert(targetCategory);

                InstanceBinding binding = doc.Application.Create.NewInstanceBinding(categorySet);

                // Добавляем параметр (BuiltInParameterGroup.PG_TEXT = группа "Текст")
                bool success = doc.ParameterBindings.Insert(definition, binding, BuiltInParameterGroup.PG_TEXT);

                if (success)
                {
                    TaskDialog.Show("Добавлено", $"Параметр '{paramName}' добавлен в категорию {targetCategory.Name}");
                }
                else
                {
                    TaskDialog.Show("Ошибка", $"Не удалось добавить параметр '{paramName}'");
                }
            }
        }

        private HashSet<string> GetExistingParameterNames(Document doc, Category targetCategory)
        {
            HashSet<string> existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Получаем BindingMap документа
            BindingMap bindingMap = doc.ParameterBindings;

            // Используем итератор для обхода всех привязок
            DefinitionBindingMapIterator iterator = bindingMap.ForwardIterator();
            iterator.Reset();

            while (iterator.MoveNext())
            {
                Definition definition = iterator.Key;
                ElementBinding binding = iterator.Current as ElementBinding;

                if (binding != null && binding.Categories != null)
                {
                    // Проверяем, содержит ли набор категорий нашу целевую категорию
                    if (binding.Categories.Contains(targetCategory))
                    {
                        existingNames.Add(definition.Name);
                    }
                }
            }

            return existingNames;
        }
    }
}