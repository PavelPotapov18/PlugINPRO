using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using PlugINPRO.UI.Windows.BIM;

namespace PlugINPRO.Commands.BIM
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
                    if(fileNameLower.EndsWith(".ifc"))
                    {
                        foreach(string keyword in keywords)
                    {
                            if (fileNameLower.Contains(keyword))
                            {
                                ifcFiles.Add(Path.Combine(inputFolder, Path.GetFileName(file)));
                                break; //добавляем файл в список 1 раз
                            }
                        }
                    }
                    
                }
                TaskDialog.Show("Debug", $"Найдено IFC файлов: {ifcFiles.Count}");
            }

            foreach (string file in ifcFiles)
            {
                try
                {
                    string ifcFile = Path.GetFileName(file);

                    //Открываем IFC в Revit без опций
                    Document doc = app.OpenIFCDocument(file);

                    //Получаем материал "ADSK_Бетон_Железобетон" и его штриховку
                    string materialName = "ADSK_Бетон_Железобетон";
                    ElementId patternId = ElementId.InvalidElementId;

                    var materials = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .ToList();

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

                    var families = new FilteredElementCollector(doc)
                        .OfClass(typeof(Family))
                        .Cast<Family>()
                        .ToList();
                    foreach (Family family in families)
                    {
                        foreach(string kw in famKeyWords)
                        {
                            if (family.Name.Contains(kw))
                                familiesToDelete.Add(family.Id);
                        }
                    }

                    if(familiesToDelete.Count > 0)
                    {
                        using (Transaction t = new Transaction(doc, "Удаление семейств"))
                        {
                            t.Start();
                            foreach(ElementId famId in familiesToDelete)
                            {
                                doc.Delete(famId);
                            }
                            t.Commit();
                        }
                    }

                    //Удаляем материалы, содержащие STEEL\
                    List<ElementId> materialsToDelete = new List<ElementId>();
                    foreach(Material mat  in materials)
                        if(mat.Name.ToUpper().Contains("STEEL"))
                            materialsToDelete.Add(mat.Id);

                    if (materialsToDelete.Count > 0)
                    {
                        using (Transaction t = new Transaction(doc, "Удаление материалов STEEL"))
                        {
                            t.Start();
                            foreach(ElementId materialId in materialsToDelete)
                            {
                                doc.Delete(materialId);
                            }
                            t.Commit();
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
                    doc.Close(false);

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
