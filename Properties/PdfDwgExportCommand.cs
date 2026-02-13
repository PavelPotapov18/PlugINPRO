using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlugINPRO.Properties
{
    [Transaction(TransactionMode.Manual)]
    internal class PdfDwgExportCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            string packageName = "AR_3001";
            string projectStatus = "Р";
            bool isAlternativeSheetName = false;
            string alternativePrefix = "Proj1";
            string directoryToSave = @"C:\OneDrive\OneDrive - Inproec\Desktop\testPlug";

            var sheetsToExport = new  FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => 
                s.LookupParameter("ADSK_Штамп Раздел проекта").AsString() == packageName &&
                s.LookupParameter("Project status_text").AsString() == projectStatus)
                .ToList();

            //Получаем информацию о проекте, берем необходимые для имени листов параметры, формируем шифр проекта и комплекта
            ProjectInfo projectInfo = doc.ProjectInformation;
            string projNum = projectInfo.Number;
            string buildingNum = projectInfo.LookupParameter("ADSK_Номер здания").AsString();
            string projCode = projNum + "-" + buildingNum;
            string joinedPDFName = projCode + "-" + packageName;

            //Работа с выбранными листами
            List<string>sheetsNames = new List<string>();
            List<ElementId> uniqueRevIds = new List<ElementId>();
            foreach (ViewSheet sheet in sheetsToExport)
            {
                //Формируем корректное имя для листов
                string afryCode = sheet.LookupParameter("AFRY_Нули").AsString();
                string currentRevision = sheet.get_Parameter(BuiltInParameter.SHEET_CURRENT_REVISION).AsString();
                string currentIzm = sheet.LookupParameter("INP_Изм_тек.").AsString();
                string izm = "";
                if(currentIzm != "")
                    izm = "_Izm" + currentIzm;
                string correctSheetName = joinedPDFName + "_" + afryCode + "_R" + currentRevision + izm;
                //Формируем альтернативное имя для листов
                string alternativeSheetName = alternativePrefix + " Лист " + sheet.SheetNumber + izm;
                if (isAlternativeSheetName)
                    sheetsNames.Add(alternativeSheetName);
                else
                    sheetsNames.Add(correctSheetName);

                //Получаем уникальные ID ревизий по листам
                foreach(ElementId revId in sheet.GetAllRevisionIds())
                {
                    if(!uniqueRevIds.Contains(revId))
                        uniqueRevIds.Add(revId);
                }
            }

            //Формируем директории для сохранения файлов
            List<Revision> uniqueRevs = new List<Revision>();
            HashSet<string> uniqueDates = new HashSet<string>();
            foreach (ElementId revId in uniqueRevIds)
            {
                uniqueRevs.Add(doc.GetElement(revId) as Revision);
            }
            foreach (Revision rev in uniqueRevs)
            {
                string date = rev.RevisionDate;
                if (!string.IsNullOrEmpty(date))
                {
                    uniqueDates.Add(date);
                }
            }
            int index = uniqueDates.Count - 1;
            if (index < 0) index = 0;

            string dirRevSuffix = "_R" + index.ToString("D2");

            // 1. Текущая дата
            string directoryDate = DateTime.Now.ToString("yyyy.MM.dd");

            // 2. Имя папки (date + rev)
            string folderName = directoryDate + dirRevSuffix;

            // 3. Формирование путей (правильно через Path.Combine!)
            string mainFolderPath = Path.Combine(directoryToSave, folderName);
            string separateDocsPath = Path.Combine(mainFolderPath, "Отдельные документы");
            string redFormatPath = Path.Combine(mainFolderPath, "Ред_формат");

            // 4. Создание папок
            Directory.CreateDirectory(mainFolderPath);
            Directory.CreateDirectory(separateDocsPath);
            Directory.CreateDirectory(redFormatPath);
                
            return Result.Succeeded;
        }
    }
}
