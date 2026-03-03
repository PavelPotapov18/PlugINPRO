using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlugINPRO
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

            PdfDwgExportWindow window = new PdfDwgExportWindow(doc);

            var helper = new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = uiApp.MainWindowHandle
            };



            if (window.ShowDialog() != true)
                return Result.Cancelled;

            ExportSettings settings = window.ResultSettings;
            string packageName = settings.PackageName;
            string projectStatus = settings.ProjectStatus;
            bool isAlternativeSheetName = settings.UseAlternativeName;
            
            string separator1 = settings.Separator1;
            string separator2 = settings.Separator2;
            string separator3 = settings.Separator3;

            string alternativePrefix = settings.AlternativePrefix;
            string directoryToSave = settings.DirectoryPath;
            string OPRSuffix = settings.AddOprSuffix ? ".ОПР" : "";
            string predefinedSetup = settings.DwgPredefinedSetup;

            var sheetsToExport = new FilteredElementCollector(doc)
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
            string projCode = projNum + separator1 + buildingNum;
            string combinedPDFName = projCode + separator2 + packageName;
            if (!string.IsNullOrEmpty(OPRSuffix))
                combinedPDFName += OPRSuffix;
            List<string> combinedPDFNameList = new List<string>() { combinedPDFName };

            //Работа с выбранными листами
            List<string> sheetsNames = new List<string>();
            List<string> alternativeSheetsNames = new List<string>();

            List<ElementId> uniqueRevIds = new List<ElementId>();
            foreach (ViewSheet sheet in sheetsToExport)
            {
                //Формируем корректное имя для листов
                string afryCode = sheet.LookupParameter("AFRY_Нули").AsString();
                string currentRevision = sheet.get_Parameter(BuiltInParameter.SHEET_CURRENT_REVISION).AsString();
                string rev = "";
                if(!string.IsNullOrEmpty(currentRevision))
                    rev = "_R" + currentRevision;
                string currentIzm = sheet.LookupParameter("INP_Изм_тек.")?.AsString();
                string izm = "";
                if (!string.IsNullOrEmpty(currentIzm))
                    izm = "_Izm" + currentIzm;

                string correctSheetName = combinedPDFName + separator3 + afryCode + rev + izm;

                string alternativeSheetName = alternativePrefix + " Лист " + sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER).AsString() + izm;

                sheetsNames.Add(correctSheetName);
                
                alternativeSheetsNames.Add(alternativeSheetName);

                //Получаем уникальные ID ревизий по листам
                foreach (ElementId revId in sheet.GetAllRevisionIds())
                {
                    if (!uniqueRevIds.Contains(revId))
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

            // ФОРМИРУЕМ ДИРЕКТОРИИ ДЛЯ СОХРАНЕНИЯ
            // 1. Текущая дата
            string directoryDate = DateTime.Now.ToString("yyyy.MM.dd");

            // 2. Имя папки (date + rev)
            string folderName = directoryDate + dirRevSuffix;

            // 3. Формирование путей
            string mainFolderPath = Path.Combine(directoryToSave, folderName);
            string separateDocsPath = Path.Combine(mainFolderPath, "Отдельные документы");
            string redFormatPath = Path.Combine(mainFolderPath, "Ред_формат");

            // 4. Создание папок
            Directory.CreateDirectory(mainFolderPath);
            Directory.CreateDirectory(separateDocsPath);
            Directory.CreateDirectory(redFormatPath);


            //Экспорт объединенного PDF файла комплекта
            List<bool> combinedExport = ExportSheetsToPdf(doc, sheetsToExport, combinedPDFNameList, mainFolderPath, true);

            //Экспорт отдельных PDF файлов листов
            List<bool> separateExport;
            if(isAlternativeSheetName == false)
                separateExport = ExportSheetsToPdf(doc, sheetsToExport, sheetsNames, separateDocsPath, false);
            else
                separateExport = ExportSheetsToPdf(doc, sheetsToExport, alternativeSheetsNames, separateDocsPath, false);


            //Экспорт отдельных DWG файлов листов
            bool runIt = true;
            if (!runIt)
            {
                TaskDialog.Show("Export DWG", "RunIt = False");
                return Result.Cancelled;
            }

            int count = Math.Min(sheetsToExport.Count, sheetsNames.Count);

            

            for (int i = 0; i < count; i++)
            {
                ExportSheetsToDwg(
                    doc,
                    sheetsToExport[i],
                    sheetsNames[i],
                    redFormatPath,
                    predefinedSetup
                    );
            }
            return Result.Succeeded;
        }
        
        public List<bool> ExportSheetsToPdf(
        Document doc,
        List<ViewSheet> sheets,
        List<string> names,
        string folder,
        bool isCombined)
        {
            List<bool> results = new List<bool>();
            //Настройки экспорта в PDF
            PDFExportOptions PDFOpts = new PDFExportOptions()
            {
                ExportQuality = PDFExportQualityType.DPI600,
                PaperFormat = ExportPaperFormat.Default,
                ZoomType = ZoomType.Zoom,
                ZoomPercentage = 100,
                AlwaysUseRaster = false,
                HideCropBoundaries = true,
                HideReferencePlane = true,
                HideScopeBoxes = true,
                HideUnreferencedViewTags = true,
                MaskCoincidentLines = true,
                ViewLinksInBlue = false,
                ColorDepth = ColorDepthType.Color,
                StopOnError = false,
            };
            if (isCombined) //Экспорт объединенного PDF файла комплекта
            {
                PDFOpts.Combine = true;
                PDFOpts.FileName = names[0];

                IList<ElementId> sheetsIds = new List<ElementId>();
                foreach (ViewSheet sheet in sheets)
                {
                    sheetsIds.Add(sheet.Id);
                }
                bool exported = doc.Export(folder, sheetsIds, PDFOpts);
                results.Add(exported);
            }
            else //Экспорт отдельных PDF файлов листов
            {
                PDFOpts.Combine = false;
                int count = Math.Min(sheets.Count, names.Count);

                for (int i = 0; i < count; i++)
                {
                    string tempName = Guid.NewGuid().ToString();
                    PDFOpts.FileName = tempName;

                    IList<ElementId> sheetIds = new List<ElementId>
                    {
                        sheets[i].Id
                    };

                    bool exported = doc.Export(folder, sheetIds, PDFOpts);
                    if(exported)
                    {
                        string createdFile = Directory
                            .GetFiles(folder, "*.pdf")
                            .OrderByDescending(f => File.GetCreationTime(f))
                            .FirstOrDefault();
                        if (!string.IsNullOrEmpty(createdFile))
                        {
                            string finalPath = Path.Combine(folder, names[i] + ".pdf");
                            if(File.Exists(finalPath))
                                File.Delete(finalPath);
                            File.Move(createdFile, finalPath);
                        }
                    }
                    results.Add(exported);
                }
            }
            return results;
        }

        public bool ExportSheetsToDwg(
            Document doc,
            ViewSheet sheet,
            string name,
            string folderPath,
            string predefinedSetup)
        {
            DWGExportOptions DwgOptions;
            if (!string.IsNullOrEmpty(predefinedSetup))
            {
                DwgOptions = DWGExportOptions.GetPredefinedOptions(doc, predefinedSetup);
            }
            else
            {
                DwgOptions = new DWGExportOptions();
            }
            DwgOptions.SharedCoords = true;
            DwgOptions.MergedViews = true;

            ICollection<ElementId> sheetId = new List<ElementId> { sheet.Id };

            return doc.Export(folderPath, name, sheetId, DwgOptions);
        }


    }
}
