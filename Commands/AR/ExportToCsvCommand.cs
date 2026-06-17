using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using PlugINPRO.Services.AR;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlugINPRO.Commands.AR
{
    [Transaction(TransactionMode.Manual)]
    public class ExportToCsvCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;


            // Диалог сохранения файла
            SaveFileDialog dlg = new SaveFileDialog
            {
                Title = "Сохранить CSV",
                Filter = "CSV файлы (*.csv)|*.csv",
                FileName = "export.csv"
            };

            if (dlg.ShowDialog() != true)
                return Result.Cancelled;

            // Заголовки
            var headers = new[]
            {
                "№ пом.",
                "Отм.",
                "Наименование материала",
                "Площадь",
                "Ориентация по сторонам света"
            };

            List<Room> rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .OrderBy(r =>
                {
                    string num = r.get_Parameter(BuiltInParameter.ROOM_NUMBER).AsString() ?? "";
                    return num.Split('.')
                              .Select(p => int.TryParse(p, out int n) ? n : 0)
                              .ToArray();
                }, new IntArrayComparer())
                .ToList();

            List<Wall> walls = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .Where(w => w.WallType.Name.Contains("Отделка_ЭЭ"))
                .ToList();

            List<Floor> floors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .Cast<Floor>()
                .ToList();

            List<Element> doors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .ToList();

            List<Element> windows = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Windows)
                .WhereElementIsNotElementType()
                .ToList();

            List<List<string>> rows = new List<List<string>>();

            foreach(Room room in rooms)
            {
                string roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString();
                string roomNumber = room.get_Parameter(BuiltInParameter.ROOM_NUMBER).AsString();
                string roomWall = $"{roomNumber} {roomName}";

                List<string> roomRow = new List<string> { $"=\"{roomNumber}\"", roomName, "", "", ""};
                rows.Add(roomRow);

                double roomLoc = UnitUtils.ConvertFromInternalUnits(((LocationPoint)(room.Location)).Point.Z, UnitTypeId.Meters);
                string sign = roomLoc > 0 ? "+" : roomLoc < 0 ? "-" : "";
                string roomLocStr = sign + Math.Abs(roomLoc).ToString("F3").Replace(",", ".");
                

                double roomArea = UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareMeters);
                string roomAreaStr = roomArea.ToString("F2").Replace(',', '.');


                string roofInfo = room.LookupParameter("ADSK_Примечание").AsString();
                if(!string.IsNullOrEmpty(roofInfo))
                {
                    List<string> roofRow = new List<string> { "", roomLocStr, roofInfo, roomAreaStr, "-" };
                    rows.Add(roofRow);
                }
                
                
                foreach (Floor floor in floors)
                {
                    string floorName = floor.FloorType.Name;

                    double floorArea = UnitUtils.ConvertFromInternalUnits(floor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED).AsDouble(), UnitTypeId.SquareMeters);
                    string floorAreaStr = floorArea.ToString("F2").Replace(',', '.');

                    string floorRoomParam = floor.LookupParameter("Помещение Имя").AsString();
                    if (floorRoomParam != roomWall)
                        continue;
                    
                    List<string> floorRow = new List<string> { "", roomLocStr, floorName, floorAreaStr, "-" };
                    rows.Add(floorRow);
                }

                ISet<string> usedOrientations = new HashSet<string>();

                foreach (Wall wall in walls)
                {
                    string wallRoomParam = wall.LookupParameter("Помещение Имя").AsString();
                    if (wallRoomParam != roomWall)
                        continue;

                    string wallOrientation = wall.LookupParameter("INP_Ориентация по сторонам света").AsString();

                    double wallArea = UnitUtils.ConvertFromInternalUnits(wall.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED).AsDouble(), UnitTypeId.SquareMeters);
                    string wallAreaStr = wallArea.ToString("F2").Replace(',', '.');

                    string wallFunction = "";
                    if (wall.WallType.Name.Contains("Цоколь"))
                        wallFunction = "Цоколь";
                    else if (wall.WallType.Name.Contains("Сэндвич"))
                        wallFunction = "Сэндвич-панель";
                    else
                        continue;

                    List<string> wallRow = new List<string>{ "", roomLocStr, wallFunction, wallAreaStr, wallOrientation };
                    rows.Add(wallRow);

                    // Двери и окна — только один раз на каждую ориентацию
                    if (!wall.WallType.Name.Contains("Цоколь") &&
                        !usedOrientations.Contains(wallOrientation))
                    {
                        usedOrientations.Add(wallOrientation);

                        List<string> doorRow = GetDoorsWindowsRow(
                            doc, doors, wallOrientation, roomWall, roomLocStr, "Дверь");
                        if (doorRow != null)
                            rows.Add(doorRow);

                        List<string> windowsRow = GetDoorsWindowsRow(
                            doc, windows, wallOrientation, roomWall, roomLocStr, "Окна/Остекление");
                        if (windowsRow != null)
                            rows.Add(windowsRow);
                    }
                }
            }

            // Экспорт
            new CsvExporter().Export(dlg.FileName, headers, rows);
            TaskDialog.Show("Готово",
                $"Данные успешно сохранены в файл {dlg.FileName}");

            return Result.Succeeded;
        }

        public List<string> GetDoorsWindowsRow(Document doc,List<Element> elems, string wallOrientation, string roomWall, string roomLocStr, string name)
        {
            double area = 0;

            foreach (Element elem in elems)
            {
                FamilyInstance faminst = elem as FamilyInstance;

                // Проверка параметров
                Parameter orientParam = faminst.LookupParameter("INP_Ориентация по сторонам света");
                Parameter roomParam = faminst.LookupParameter("Помещение Имя");
                

                if (orientParam == null || roomParam == null)
                    continue;

                Parameter areaParam = faminst.LookupParameter("ADSK_Площадь проемов");

                if(areaParam == null)
                {
                    ElementType type = doc.GetElement(faminst.GetTypeId()) as ElementType;
                    areaParam = type.LookupParameter("ADSK_Площадь проемов");
                }
                    

                string elemOrientation = orientParam.AsString();
                string roomElem = roomParam.AsString();

                if (roomWall != roomElem || wallOrientation != elemOrientation)
                    continue;

                area += UnitUtils.ConvertFromInternalUnits(areaParam.AsDouble(), UnitTypeId.SquareMeters);
            }
            if (area == 0)
                return null;

            string areaStr = area.ToString("F2").Replace(',', '.');
            

            return new List<string> { "", roomLocStr, name, areaStr, wallOrientation };
        }

        public class IntArrayComparer : IComparer<int[]>
        {
            public int Compare(int[] x, int[] y)
            {
                int len = Math.Max(x.Length, y.Length);
                for (int i = 0; i < len; i++)
                {
                    int a = i < x.Length ? x[i] : 0;
                    int b = i < y.Length ? y[i] : 0;
                    int cmp = a.CompareTo(b);
                    if (cmp != 0)
                        return cmp;
                }
                return 0;
            }
        }

    }
}
