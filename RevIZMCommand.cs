using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace PlugINPRO
{
    [Transaction(TransactionMode.Manual)]
    public class RevIZMCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            Application app = uiApp.Application;
            Document doc = uiApp.ActiveUIDocument.Document;

            // Получаем все листы проекта
            FilteredElementCollector coll1 = new FilteredElementCollector(doc);
            var sheets = coll1.OfCategory(BuiltInCategory.OST_Sheets).ToElements();

            // Получаем ID ревизий по листам
            List<IList<ElementId>> revsIds = new List<IList<ElementId>>();
            foreach (var elem in sheets)
            {
                ViewSheet sheet = elem as ViewSheet;
                IList<ElementId> revisionsOnSheetIds = sheet.GetAllRevisionIds();
                revsIds.Add(revisionsOnSheetIds);
            }

            // Получаем ревизии по листам
            List<List<Revision>> revs = new List<List<Revision>>();
            for(int i = 0;  i < revsIds.Count; i++)
            {
                List<Revision> revisions = new List<Revision>();
                for (int j = 0; j < revsIds[i].Count; j++)
                {
                    Revision rev = (Revision)doc.GetElement(revsIds[i][j]);
                    revisions.Add(rev);
                }
                revs.Add(revisions);
                
            }

            
            // Получаем все пометочные облака
            FilteredElementCollector coll2 = new FilteredElementCollector(doc);
            var clouds = coll2.OfCategory(BuiltInCategory.OST_RevisionClouds).WhereElementIsNotElementType().ToElements();

            // Получаем ревизии по облакам, считаем количество участков по листам
            Dictionary<ElementId,List<ElementId>> cloudDict = new Dictionary<ElementId, List<ElementId>>();
            foreach (Element cloud in clouds)
            {
                RevisionCloud cloud1 = cloud as RevisionCloud;
                if (cloud1 == null)
                    continue;

                ElementId revOfCloudId = cloud1.RevisionId;
                if (revOfCloudId == ElementId.InvalidElementId)
                    continue; // облако не привязано к ревизии

                var sheetsOfCloudIds = cloud1.GetSheetIds().ToList();
                if (sheetsOfCloudIds == null)
                    continue;
                
                if (cloudDict.ContainsKey(revOfCloudId))
                    cloudDict[revOfCloudId].Add(sheetsOfCloudIds[0]);
                else
                    cloudDict.Add(revOfCloudId, sheetsOfCloudIds);

            }




            // Записываем данные ревизий и ИЗМов
            List<List<List<string>>> RevsData = new List<List<List<string>>>();
            List<List<List<string>>> IZMData = new List<List<List<string>>>();
            
            for (int i = 0; i < revs.Count; i++)
            {
                List<List<string>> b = new List<List<string>>();//Список для данных по ревизиям
                List<List<string>> b1 = new List<List<string>>();//Список для данных по ИЗМам
                for (int j = 0; j < revsIds[i].Count; j++)
                {
                    List<string> c = new List<string>(); //Список для данных по ревизии
                    List<string> c1 = new List<string>(); //Список для данных по ИЗМу
                    string descriptionRev;
                    string date;

                    if (revs[i][j].Description.Contains('!'))
                    {
                        string[] description = revs[i][j].Description.Split('!');
                        descriptionRev = description[1];
                        date = revs[i][j].RevisionDate;
                        string[] descriptionIZMData = description[0].Split('_');
                        string cloudCount = "-";

                        if (cloudDict.ContainsKey(revs[i][j].Id) && cloudDict[revs[i][j].Id].Contains(sheets[i].Id))
                        {
                            cloudCount = cloudDict[revs[i][j].Id].Count<ElementId>(s => s == sheets[i].Id).ToString();
                        }

                        //Формируем список с данными ИЗМа, добавляем в общий
                        c1.Add(descriptionIZMData[0]);
                        c1.Add(cloudCount);
                        c1.Add(descriptionIZMData[1]);
                        c1.Add(descriptionIZMData[2]);
                        c1.Add(date.ToString());
                        b1.Add(c1);
                    }
                    else
                    {
                        descriptionRev = revs[i][j].Description;
                        date = revs[i][j].RevisionDate;

                        //Формируем список с данными ИЗМа, добавляем в общий
                        c1.Add("");
                        c1.Add("");
                        c1.Add("");
                        c1.Add("");
                        c1.Add("");
                        b1.Add(c1);
                    }

                    //Формируем номер ревизии
                    string revNum = "";
                    if (j >= 10)
                        revNum = $"{j}";
                    else
                        revNum = $"0{j}";


                    //Формируем список с данными ревизии, добавляем в общий
                    c.Add(revNum);
                    c.Add(date);
                    c.Add(descriptionRev);
                    c.Add(revs[i][j].IssuedTo);
                    c.Add(revs[i][j].IssuedBy);
                    b.Add(c);  
                }
                //Проверка длины списка с данными ИЗМов (берем последние 4 элемента)
                if (b1.Count > 4)
                {
                    b1.RemoveRange(0, b1.Count - 4);
                    IZMData.Add(b1);
                }
                else
                    IZMData.Add(b1);

                //Проверка длины списка с данными ревизий (берем последние 3 элемента)
                if (b.Count > 3)
                {
                    b.RemoveRange(0, b.Count - 3);
                    RevsData.Add(b);
                }
                else
                    RevsData.Add(b);

            }

            // Заполняем параметры по листам
            SetRevisionParameters(doc, sheets, RevsData);
            SetIZMParameters(doc, sheets, IZMData);
            TaskDialog.Show("Revit", "Параметры ревизий и ИЗМов успешно заполнены!");
            return Result.Succeeded;
        }

        
        public bool SetRevisionParameters(Document doc, IList<Element> sheets, List<List<List<string>>> data)
        {
            using (Transaction t = new Transaction(doc, "Заполнение параметров ревизий"))
            {
                if (t.Start() == TransactionStatus.Started)
                {
                    for (int i = 0; i < sheets.Count; i++)
                    {
                        Parameter num3 = sheets[i].LookupParameter("INP_Рев.№_3");
                        Parameter date3 = sheets[i].LookupParameter("INP_Дата_3");
                        Parameter description3 = sheets[i].LookupParameter("INP_Основание_3");
                        Parameter issuedTo3 = sheets[i].LookupParameter("INP_Разработал_3");
                        Parameter issuedBy3 = sheets[i].LookupParameter("INP_Проверил_3");

                        Parameter num2 = sheets[i].LookupParameter("INP_Рев.№_2");
                        Parameter date2 = sheets[i].LookupParameter("INP_Дата_2");
                        Parameter description2 = sheets[i].LookupParameter("INP_Основание_2");
                        Parameter issuedTo2 = sheets[i].LookupParameter("INP_Разработал_2");
                        Parameter issuedBy2 = sheets[i].LookupParameter("INP_Проверил_2");

                        Parameter num1 = sheets[i].LookupParameter("INP_Рев.№_1");
                        Parameter date1 = sheets[i].LookupParameter("INP_Дата_1");
                        Parameter description1 = sheets[i].LookupParameter("INP_Основание_1");
                        Parameter issuedTo1 = sheets[i].LookupParameter("INP_Разработал_1");
                        Parameter issuedBy1 = sheets[i].LookupParameter("INP_Проверил_1");

                        
                        if (data[i].Count >= 3)
                        {
                            num3.Set(data[i][2][0]);
                            date3.Set(data[i][2][1]);
                            description3.Set(data[i][2][2]);
                            issuedTo3.Set(data[i][2][3]);
                            issuedBy3.Set(data[i][2][4]);
                        }
                        if (data[i].Count >= 2)
                        {
                            num2.Set(data[i][1][0]);
                            date2.Set(data[i][1][1]);
                            description2.Set(data[i][1][2]);
                            issuedTo2.Set(data[i][1][3]);
                            issuedBy2.Set(data[i][1][4]);
                        }
                        if (data[i].Count >= 1)
                        {
                            num1.Set(data[i][0][0]);
                            date1.Set(data[i][0][1]);
                            description1.Set(data[i][0][2]);
                            issuedTo1.Set(data[i][0][3]);
                            issuedBy1.Set(data[i][0][4]);
                        }
                        
                    }
                   
                }
                return (TransactionStatus.Committed == t.Commit());

            }
        }

        public bool SetIZMParameters(Document doc, IList<Element> sheets, List<List<List<string>>> data)
        {
            using (Transaction t = new Transaction(doc, "Заполнение параметров ИЗМов"))
            {
                if (t.Start() == TransactionStatus.Started)
                {
                    for (int i = 0; i < sheets.Count; i++)
                    {
                        Parameter izm4 = sheets[i].LookupParameter("INP_Изм_4");
                        Parameter count4 = sheets[i].LookupParameter("INP_Кол.уч_4");
                        Parameter sheet4 = sheets[i].LookupParameter("INP_Лист_4");
                        Parameter numDoc4 = sheets[i].LookupParameter("INP_№док._4");
                        Parameter date4 = sheets[i].LookupParameter("INP_Дата_изм_4");

                        Parameter izm3 = sheets[i].LookupParameter("INP_Изм_3");
                        Parameter count3 = sheets[i].LookupParameter("INP_Кол.уч_3");
                        Parameter sheet3 = sheets[i].LookupParameter("INP_Лист_3");
                        Parameter numDoc3 = sheets[i].LookupParameter("INP_№док._3");
                        Parameter date3 = sheets[i].LookupParameter("INP_Дата_изм_3");

                        Parameter izm2 = sheets[i].LookupParameter("INP_Изм_2");
                        Parameter count2 = sheets[i].LookupParameter("INP_Кол.уч_2");
                        Parameter sheet2 = sheets[i].LookupParameter("INP_Лист_2");
                        Parameter numDoc2 = sheets[i].LookupParameter("INP_№док._2");
                        Parameter date2 = sheets[i].LookupParameter("INP_Дата_изм_2");

                        Parameter izm1 = sheets[i].LookupParameter("INP_Изм_1");
                        Parameter count1 = sheets[i].LookupParameter("INP_Кол.уч_1");
                        Parameter sheet1 = sheets[i].LookupParameter("INP_Лист_1");
                        Parameter numDoc1 = sheets[i].LookupParameter("INP_№док._1");
                        Parameter date1 = sheets[i].LookupParameter("INP_Дата_изм_1");

                        Parameter currentIZM = sheets[i].LookupParameter("INP_Изм_тек.");

                        if (data[i].Count == 4 && !string.IsNullOrWhiteSpace(data[i][3][0]))
                        {
                            SetParam(izm4, data[i][3][0]);
                            SetParam(count4, data[i][3][1]);
                            SetParam(sheet4, data[i][3][2]);
                            SetParam(numDoc4, data[i][3][3]);
                            SetParam(date4, data[i][3][4]);
                            currentIZM.Set(data[i][3][0]);
                        }

                        if (data[i].Count >= 3 && !string.IsNullOrWhiteSpace(data[i][2][0]))
                        {
                            SetParam(izm3, data[i][2][0]);
                            SetParam(count3, data[i][2][1]);
                            SetParam(sheet3, data[i][2][2]);
                            SetParam(numDoc3, data[i][2][3]);
                            SetParam(date3, data[i][2][4]);
                            currentIZM.Set(data[i][2][0]);
                        }
                        if (data[i].Count >= 2 && !string.IsNullOrWhiteSpace(data[i][1][0]))
                        {
                            SetParam(izm2, data[i][1][0]);
                            SetParam(count2, data[i][1][1]);
                            SetParam(sheet2, data[i][1][2]);
                            SetParam(numDoc2, data[i][1][3]);
                            SetParam(date2, data[i][1][4]);
                            currentIZM.Set(data[i][1][0]);
                        }
                        if (data[i].Count >= 1 && !string.IsNullOrWhiteSpace(data[i][0][0]))
                        {
                            SetParam(izm1, data[i][0][0]);
                            SetParam(count1, data[i][0][1]);
                            SetParam(sheet1, data[i][0][2]);
                            SetParam(numDoc1, data[i][0][3]);
                            SetParam(date1, data[i][0][4]);
                            currentIZM.Set(data[i][0][0]);
                        }
                    }
                }
                return (TransactionStatus.Committed == t.Commit());
            }
        }

        private void SetParam(Parameter p, string value)
        {
            if (p != null && !p.IsReadOnly)
                p.Set(value ?? "");
        }
    }
}
