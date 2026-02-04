using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.ApplicationServices;
using System.IO;


namespace PlugINPRO
{
    [Transaction(TransactionMode.Manual)]
    public class IFCtoRVTCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;

            Application app = uiApp.Application;


            IFCRVTWindow window = new IFCRVTWindow();
            bool? dialogResult = window.ShowDialog();

            if (dialogResult != true)
                return Result.Cancelled;

            //пути к директориям
            string inputFolder = window.InputDirectory;
            string outputFolder = window.OutputDirectory;

            List<string> keywords = new List<string> { "kr", "kzh", "km" }; //Фильтр по ключевым словам в  названии IFC

            List<string> ifcFiles = new List<string>(); //Список IFC файлов, с которыми будет проводиться работа

            if (Directory.Exists(inputFolder))
            {
                foreach (string file in Directory.GetFiles(inputFolder, "*.ifc"))
                {
                    string fileNameLower = Path.GetFileName(file).ToLower();
                    foreach (string keyword in keywords)
                    {
                        if (fileNameLower.Contains(keyword))
                        {
                            ifcFiles.Add(file);
                            break; //добавляем файл в список 1 раз
                        }
                    }
                }
            }

            foreach (string file in ifcFiles)
            {
                try
                {
                    string ifcFile = Path.GetFileName(file);

                    //Открываем IFC в Revit без опций
                    Document doc = app.OpenIFCDocument(ifcFile);

                    //Получаем материал "ADSK_Бетон_Железобетон" и его штриховку
                    string materialName = "ADSK_Бетон_Железобетон";
                    ElementId patternId = ElementId.InvalidElementId;

                    FilteredElementCollector coll1 = new FilteredElementCollector(doc);
                    var materials = coll1.OfClass(typeof(Material)).ToElements();

                    foreach (Material mat in materials)
                    {
                        if (mat.Name == materialName)
                        {
                            patternId = mat.CutForegroundPatternId;
                            break;
                        }
                    }

                    if (patternId != ElementId.InvalidElementId)
                    {
                        using (Transaction t = new Transaction(doc, "Назначение штриховки материалам"))
                        {
                            t.Start();
                            //Обрабатываем материалы в модели
                            foreach (Material mat in materials)
                            {
                                if (mat.Name.Contains("CONCRETE") || mat.Name.Contains("Бетон"))
                                {
                                    mat.CutForegroundPatternId = patternId; //Устанавливаем штриховку разреза
                                }
                            }
                            t.Commit();
                        }
                    }
                    
                    //Удаляем анкерные болты из модели
                    List<ElementId> familiesToDelete = new List<ElementId>();
                    List<string> famKeyWords = new List<string>{ "Гайка", "Шайба", "Анкерный блок", "STIRRUP", "Верхняя сетка", "Нижняя сетка", "П-стержень", "Рабочий стержень", "ХОМУТ", "Каркас пространственный" };

                    FilteredElementCollector coll2 = new FilteredElementCollector(doc);
                    var families = coll2.OfClass(typeof(Family));
                    foreach (Family family in families)
                    {
                        foreach(string kw in famKeyWords)
                        {
                            if (family.Name.Contains(kw))
                                familiesToDelete.Add(family.Id);
                        }
                    }

                    //Генерируем путь для сохранения RVT файла
                    string rvtFileName = Path.GetFileNameWithoutExtension(ifcFile) + ".rvt";
                    string rvtPath = Path.Combine(outputFolder, rvtFileName);

                    //Сохраняем как RVT файл
                    SaveAsOptions saveAsOptions = new SaveAsOptions
                    {
                        OverwriteExistingFile = true
                    };
                    doc.SaveAs(rvtPath, saveAsOptions);

                    //Закрываем документ
                    doc.Close();

                    TaskDialog.Show("Revit", $"Сохранен: {rvtPath}");
                }

                catch (Exception ex)
                {
                    TaskDialog.Show("Ошибка", ex.Message);
                    return Result.Failed;
                }
            }
            TaskDialog.Show("Revit", $"Конвертация завершена! Файлы в директории {outputFolder} успешно обновлены");
            return Result.Succeeded;
        }
    }
}
