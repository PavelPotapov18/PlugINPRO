using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using PlugINPRO.UI.Windows.Common;


namespace PlugINPRO.Commands.Common
{
    [Transaction(TransactionMode.Manual)]
    internal class WorksetsCheckCommand : IExternalCommand
    {
        public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            //Имя выбранного рабочего набора
            WorksetsCheckWindow window = new WorksetsCheckWindow(doc);

            var helper = new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = uiApp.MainWindowHandle
            };

            if (window.ShowDialog() != true)
                return Result.Cancelled;

            Workset selectedWorkset = window.SelectedWorkset;

            if (selectedWorkset == null)
            {
                TaskDialog.Show("Ошибка", "Рабочий набор не найден.");
                return Result.Failed;
            }
            string selectedWorksetName = selectedWorkset.Name;


            //Ключевые слова для поиска связанных файлов
            List<string> linkARKeyWords = new List<string> { "ar" };
            List<string> linkATXKeyWords = new List<string> { "atx" };
            List<string> linkKRKeyWords = new List<string> { "kr", "kzh", "km" };
            List<string> linkVKKeyWords = new List<string> { "vk", "pv" };
            List<string> linkOVKeyWords = new List<string> { "ov", "ios4" };
            List<string> linkESKeyWords = new List<string> { "es", "ios1" };

            //Создаем список с ID некорректных элементов
            List<ElementId> incorrectElementsIds = new List<ElementId>();            
            
            List<RevitLinkInstance> rvtLinkInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .WhereElementIsNotElementType()
                .Cast<RevitLinkInstance>()
                .ToList();

            switch (selectedWorksetName)
            {
                case "Shared Levels and Grids":
                case "Общие уровни и сетки":
                    List<Level> levels = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .WhereElementIsNotElementType()
                        .Cast<Level>()
                        .ToList();
                    List<Grid> grids = new FilteredElementCollector(doc)
                        .OfClass(typeof(Grid))
                        .WhereElementIsNotElementType()
                        .Cast<Grid>()
                        .ToList();

                    foreach(Grid grid in grids)
                    {
                        if(grid.WorksetId != selectedWorkset.Id)
                            incorrectElementsIds.Add(grid.Id);
                    }
                    foreach (Level level in levels)
                    {
                        if (level.WorksetId != selectedWorkset.Id)
                            incorrectElementsIds.Add(level.Id);
                    }
                    break;

                case "#Связь_АР":
                    RevitLinksCheckByKeyword(doc, rvtLinkInstances, linkARKeyWords, selectedWorkset, ref incorrectElementsIds);
                    break;

                case "#Связь_АTX":
                    RevitLinksCheckByKeyword(doc, rvtLinkInstances, linkATXKeyWords, selectedWorkset, ref incorrectElementsIds);
                    break;
                case "#Связь_ВК":
                    RevitLinksCheckByKeyword(doc, rvtLinkInstances, linkVKKeyWords, selectedWorkset, ref incorrectElementsIds);
                    break;
                case "#Связь_КР":
                    RevitLinksCheckByKeyword(doc, rvtLinkInstances, linkKRKeyWords, selectedWorkset, ref incorrectElementsIds);
                    break;
                case "#Связь_ОВ":
                    RevitLinksCheckByKeyword(doc, rvtLinkInstances, linkOVKeyWords, selectedWorkset, ref incorrectElementsIds);
                    break;
                case "#Связь_ЭОМ":
                    RevitLinksCheckByKeyword(doc, rvtLinkInstances, linkESKeyWords, selectedWorkset, ref incorrectElementsIds);
                    break;
                case "Внешние ссылки":
                    RevitLinksCheck(doc, rvtLinkInstances, selectedWorkset, ref incorrectElementsIds);
                    break;

            }

            if (incorrectElementsIds.Count == 0)
                TaskDialog.Show("Готово", $"Набор \"{selectedWorksetName}\" содержит все необходимые элементы.");
            else
            {
                TaskDialogResult result = TaskDialog.Show(
                    "Обнаружены ошибки",
                    $"Найдено элементов: {incorrectElementsIds.Count}\n\n" +
                    "Выделить и изолировать их на текущем виде?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
                    );
                if (result == TaskDialogResult.Yes)
                {
                    // Выделяем
                    uiDoc.Selection.SetElementIds(incorrectElementsIds);

                    View activeView = doc.ActiveView;

                    if (activeView.CanUseTemporaryVisibilityModes())
                    {
                        using (Transaction t =
                            new Transaction(doc, "Изоляция элементов"))
                        {
                            t.Start();
                            activeView.IsolateElementsTemporary(incorrectElementsIds);
                            t.Commit();
                        }
                    }
                    else
                    {
                        TaskDialog.Show("Ошибка",
                            "На данном виде нельзя применить изоляцию.");
                    }
                }
            }
            
            return Result.Succeeded;
        }

        public void RevitLinksCheckByKeyword(
            Document doc,
            List<RevitLinkInstance> rvtLinkInstances,
            List<string> keywords,
            Workset selectedWorkset,
            ref List<ElementId> incorrectElementsIds)
        {
            List<RevitLinkInstance> selectedLinkInstances = new List<RevitLinkInstance>();
            foreach (RevitLinkInstance revitLinkInstance in rvtLinkInstances)
            {
                string instanceNameLower = revitLinkInstance.Name.ToLower();
                string typeNameLower = doc.GetElement(revitLinkInstance.GetTypeId()).Name.ToLower();
                foreach( string kw in keywords)
                {
                    if (instanceNameLower.Contains(kw) && typeNameLower.Contains(kw))
                        selectedLinkInstances.Add(revitLinkInstance);
                }
            }
            foreach (RevitLinkInstance revitLinkInstance in selectedLinkInstances)
            {
                if (revitLinkInstance.WorksetId != selectedWorkset.Id)
                    incorrectElementsIds.Add(revitLinkInstance.Id);
                if (doc.GetElement(revitLinkInstance.GetTypeId()).WorksetId != selectedWorkset.Id)
                    incorrectElementsIds.Add(revitLinkInstance.GetTypeId());
            }
        }
        public void RevitLinksCheck(
            Document doc,
            List<RevitLinkInstance> rvtLinkInstances,
            Workset selectedWorkset,
            ref List<ElementId> incorrectElementsIds)
        {
            foreach (RevitLinkInstance revitLinkInstance in rvtLinkInstances)
            {
                if (revitLinkInstance.WorksetId != selectedWorkset.Id)
                    incorrectElementsIds.Add(revitLinkInstance.Id);
                if (doc.GetElement(revitLinkInstance.GetTypeId()).WorksetId != selectedWorkset.Id)
                    incorrectElementsIds.Add(revitLinkInstance.GetTypeId());
            }
        }
    }
}
