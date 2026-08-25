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
            UIDocument uidoc =
                commandData.Application.ActiveUIDocument;

            Document doc =
                uidoc.Document;


            // ============================================================
            // ДИАЛОГ СОХРАНЕНИЯ
            // ============================================================

            SaveFileDialog dlg =
                new SaveFileDialog
                {
                    Title = "Сохранить CSV",
                    Filter = "CSV файлы (*.csv)|*.csv",
                    FileName = "export.csv"
                };

            if (dlg.ShowDialog() != true)
                return Result.Cancelled;


            // ============================================================
            // ЗАГОЛОВКИ CSV
            // ============================================================

            var headers = new[]
            {
                "№ пом.",
                "Отм.",
                "Наименование материала",
                "Площадь",
                "Ориентация по сторонам света"
            };


            // ============================================================
            // ПОМЕЩЕНИЯ
            // ============================================================

            List<Room> rooms =
                new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0)
                    .OrderBy(
                        r =>
                        {
                            string num =
                                r.get_Parameter(
                                    BuiltInParameter.ROOM_NUMBER)
                                    ?.AsString() ?? "";

                            return num
                                .Split('.')
                                .Select(
                                    p => int.TryParse(
                                        p,
                                        out int n)
                                        ? n
                                        : 0)
                                .ToArray();
                        },
                        new IntArrayComparer())
                    .ToList();


            // ============================================================
            // СТЕНЫ ОТДЕЛКИ
            // ============================================================

            List<Wall> walls =
                new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .WhereElementIsNotElementType()
                    .Cast<Wall>()
                    .Where(
                        w =>
                        w.WallType != null &&
                        w.WallType.Name.Contains("Отделка_ЭЭ"))
                    .ToList();


            // ============================================================
            // ПОЛЫ
            // ============================================================

            List<Floor> floors =
                new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Floors)
                    .WhereElementIsNotElementType()
                    .Cast<Floor>()
                    .ToList();


            // ============================================================
            // ДВЕРИ
            // ============================================================

            List<Element> doors =
                new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .WhereElementIsNotElementType()
                    .ToList();


            // ============================================================
            // ОКНА
            // ============================================================

            List<Element> windows =
                new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Windows)
                    .WhereElementIsNotElementType()
                    .ToList();


            // ============================================================
            // СТРОКИ CSV
            // ============================================================

            List<List<string>> rows =
                new List<List<string>>();


            // ============================================================
            // ОБРАБОКА ПОМЕЩЕНИЙ
            // ============================================================

            foreach (Room room in rooms)
            {
                string roomName =
                    room.get_Parameter(
                        BuiltInParameter.ROOM_NAME)
                        ?.AsString() ?? "";

                string roomNumber =
                    room.get_Parameter(
                        BuiltInParameter.ROOM_NUMBER)
                        ?.AsString() ?? "";

                string roomWall =
                    $"{roomNumber} {roomName}";


                // --------------------------------------------------------
                // Строка помещения
                // --------------------------------------------------------

                List<string> roomRow =
                    new List<string>
                    {
                        $"=\"{roomNumber}\"",
                        roomName,
                        "",
                        "",
                        ""
                    };

                rows.Add(roomRow);


                // --------------------------------------------------------
                // Отметка помещения
                // --------------------------------------------------------

                double roomLoc =
                    UnitUtils.ConvertFromInternalUnits(
                        ((LocationPoint)room.Location).Point.Z,
                        UnitTypeId.Meters);

                string sign =
                    roomLoc > 0
                        ? "+"
                        : roomLoc < 0
                            ? "-"
                            : "";

                string roomLocStr =
                    sign +
                    Math.Abs(roomLoc)
                        .ToString("F3")
                        .Replace(",", ".");


                // --------------------------------------------------------
                // Площадь помещения
                // --------------------------------------------------------

                double roomArea =
                    UnitUtils.ConvertFromInternalUnits(
                        room.Area,
                        UnitTypeId.SquareMeters);

                string roomAreaStr =
                    roomArea
                        .ToString("F2")
                        .Replace(',', '.');


                // --------------------------------------------------------
                // Примечание помещения
                // --------------------------------------------------------

                string roofInfo =
                    room.LookupParameter(
                        "ADSK_Примечание")
                        ?.AsString();


                if (!string.IsNullOrEmpty(roofInfo))
                {
                    List<string> roofRow =
                        new List<string>
                        {
                            "",
                            roomLocStr,
                            roofInfo,
                            roomAreaStr,
                            "-"
                        };

                    rows.Add(roofRow);
                }


                // ========================================================
                // ПОЛЫ
                // ========================================================

                foreach (Floor floor in floors)
                {
                    string floorName =
                        floor.FloorType?.Name ?? "";


                    Parameter areaParam =
                        floor.get_Parameter(
                            BuiltInParameter.HOST_AREA_COMPUTED);

                    if (areaParam == null)
                        continue;


                    double floorArea =
                        UnitUtils.ConvertFromInternalUnits(
                            areaParam.AsDouble(),
                            UnitTypeId.SquareMeters);


                    string floorAreaStr =
                        floorArea
                            .ToString("F2")
                            .Replace(',', '.');


                    string floorRoomParam =
                        floor.LookupParameter(
                            "Помещение Имя")
                            ?.AsString();


                    if (floorRoomParam != roomWall)
                        continue;


                    List<string> floorRow =
                        new List<string>
                        {
                            "",
                            roomLocStr,
                            floorName,
                            floorAreaStr,
                            "-"
                        };

                    rows.Add(floorRow);
                }


                // ========================================================
                // СТЕНЫ
                // ========================================================

                ISet<string> usedOrientations =
                    new HashSet<string>();


                // --------------------------------------------------------
                // Получаем стены только текущего помещения
                // --------------------------------------------------------

                List<Wall> roomWalls =
                    new List<Wall>();


                foreach (Wall wall in walls)
                {
                    string wallRoomParam =
                        wall.LookupParameter(
                            "Помещение Имя")
                            ?.AsString();


                    if (string.IsNullOrWhiteSpace(
                            wallRoomParam))
                    {
                        continue;
                    }


                    if (wallRoomParam != roomWall)
                        continue;


                    roomWalls.Add(wall);
                }


                // ========================================================
                // ГРУППИРОВКА
                //
                // Ключ:
                //
                // 1. Реальный типоразмер стены
                // 2. Ориентация
                //
                // Например:
                //
                // ADSK_Наружная_Цоколь_260 + C
                // ADSK_Наружная_Цоколь_260 + C
                //
                // объединятся.
                //
                // Но:
                //
                // ADSK_Наружная_Цоколь_260 + C
                // ADSK_Наружная_Цоколь_300 + C
                //
                // останутся разными группами.
                // ========================================================

                var wallGroups =
                    roomWalls
                        .Select(
                            wall =>
                            {
                                string orientation =
                                    wall.LookupParameter(
                                        "INP_Ориентация по сторонам света")
                                        ?.AsString();


                                orientation =
                                    NormalizeOrientation(
                                        orientation);


                                string wallFunction =
                                    GetWallFunction(
                                        wall);


                                return new
                                {
                                    Wall = wall,

                                    WallTypeName =
                                        wall.WallType?.Name ?? "",

                                    WallFunction =
                                        wallFunction,

                                    Orientation =
                                        orientation
                                };
                            })
                        .Where(
                            x =>
                            !string.IsNullOrEmpty(
                                x.WallFunction))
                        .GroupBy(
                            x =>
                            new
                            {
                                x.WallTypeName,
                                x.Orientation
                            })
                        .ToList();


                // ========================================================
                // ОБРАБАТЫВАЕМ ГРУППЫ СТЕН
                // ========================================================

                foreach (var group in wallGroups)
                {
                    double totalArea = 0;


                    // ----------------------------------------------------
                    // Складываем площади всех стен группы
                    // ----------------------------------------------------

                    foreach (var item in group)
                    {
                        Parameter wallAreaParam =
                            item.Wall.get_Parameter(
                                BuiltInParameter.HOST_AREA_COMPUTED);


                        if (wallAreaParam == null)
                            continue;


                        double wallArea =
                            UnitUtils.ConvertFromInternalUnits(
                                wallAreaParam.AsDouble(),
                                UnitTypeId.SquareMeters);


                        totalArea += wallArea;
                    }


                    if (totalArea <= 0)
                        continue;


                    string areaStr =
                        totalArea
                            .ToString("F2")
                            .Replace(',', '.');


                    // ----------------------------------------------------
                    // В CSV выводим не имя типоразмера,
                    // а название материала
                    // ----------------------------------------------------

                    string wallFunction =
                        group.First().WallFunction;


                    string orientation =
                        group.Key.Orientation;


                    List<string> wallRow =
                        new List<string>
                        {
                            "",
                            roomLocStr,
                            wallFunction,
                            areaStr,
                            orientation
                        };


                    rows.Add(wallRow);


                    // ====================================================
                    // ДВЕРИ И ОКНА
                    //
                    // Обрабатываем только один раз для каждой ориентации.
                    // ====================================================

                    if (wallFunction != "Цоколь" &&
                        !usedOrientations.Contains(
                            orientation))
                    {
                        usedOrientations.Add(
                            orientation);


                        // ------------------------------------------------
                        // Двери
                        // ------------------------------------------------

                        List<string> doorRow =
                            GetDoorsWindowsRow(
                                doc,
                                doors,
                                orientation,
                                roomWall,
                                roomLocStr,
                                "Дверь");


                        if (doorRow != null)
                            rows.Add(doorRow);


                        // ------------------------------------------------
                        // Окна
                        // ------------------------------------------------

                        List<string> windowsRow =
                            GetDoorsWindowsRow(
                                doc,
                                windows,
                                orientation,
                                roomWall,
                                roomLocStr,
                                "Окна/Остекление");


                        if (windowsRow != null)
                            rows.Add(windowsRow);
                    }
                }
            }


            // ============================================================
            // ЭКСПОРТ
            // ============================================================

            new CsvExporter().Export(
                dlg.FileName,
                headers,
                rows);


            TaskDialog.Show(
                "Готово",
                $"Данные успешно сохранены в файл:\n{dlg.FileName}");


            return Result.Succeeded;
        }


        // ================================================================
        // ОПРЕДЕЛЕНИЕ МАТЕРИАЛА СТЕНЫ
        // ================================================================

        private string GetWallFunction(
            Wall wall)
        {
            if (wall == null)
                return null;


            WallType wallType =
                wall.WallType;


            if (wallType == null)
                return null;


            string typeName =
                wallType.Name ?? "";


            if (typeName.Contains("Цоколь"))
                return "Цоколь";


            if (typeName.Contains("Сэндвич"))
                return "Сэндвич-панель";


            return null;
        }


        // ================================================================
        // НОРМАЛИЗАЦИЯ ОРИЕНТАЦИИ
        // ================================================================

        private string NormalizeOrientation(
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";


            return value
                .Trim()
                .ToUpperInvariant();
        }


        // ================================================================
        // ДВЕРИ / ОКНА
        // ================================================================

        public List<string> GetDoorsWindowsRow(
            Document doc,
            List<Element> elems,
            string wallOrientation,
            string roomWall,
            string roomLocStr,
            string name)
        {
            double area = 0;


            foreach (Element elem in elems)
            {
                FamilyInstance faminst =
                    elem as FamilyInstance;


                if (faminst == null)
                    continue;


                // --------------------------------------------------------
                // Параметр ориентации
                // --------------------------------------------------------

                Parameter orientParam =
                    faminst.LookupParameter(
                        "INP_Ориентация по сторонам света");


                // --------------------------------------------------------
                // Параметр помещения
                // --------------------------------------------------------

                Parameter roomParam =
                    faminst.LookupParameter(
                        "Помещение Имя");


                if (orientParam == null ||
                    roomParam == null)
                {
                    continue;
                }


                // --------------------------------------------------------
                // Площадь проема
                // --------------------------------------------------------

                Parameter areaParam =
                    faminst.LookupParameter(
                        "ADSK_Площадь проемов");


                // Если параметра нет у экземпляра,
                // ищем у типоразмера.
                if (areaParam == null)
                {
                    ElementType type =
                        doc.GetElement(
                            faminst.GetTypeId())
                        as ElementType;


                    if (type != null)
                    {
                        areaParam =
                            type.LookupParameter(
                                "ADSK_Площадь проемов");
                    }
                }


                if (areaParam == null)
                    continue;


                // --------------------------------------------------------
                // Значения
                // --------------------------------------------------------

                string elemOrientation =
                    NormalizeOrientation(
                        orientParam.AsString());


                string roomElem =
                    roomParam.AsString();


                if (roomWall != roomElem ||
                    wallOrientation != elemOrientation)
                {
                    continue;
                }


                if (areaParam.StorageType !=
                    StorageType.Double)
                {
                    continue;
                }


                area +=
                    UnitUtils.ConvertFromInternalUnits(
                        areaParam.AsDouble(),
                        UnitTypeId.SquareMeters);
            }


            if (area <= 0)
                return null;


            string areaStr =
                area
                    .ToString("F2")
                    .Replace(',', '.');


            return new List<string>
            {
                "",
                roomLocStr,
                name,
                areaStr,
                wallOrientation
            };
        }


        // ================================================================
        // СРАВНЕНИЕ НОМЕРОВ ПОМЕЩЕНИЙ
        // ================================================================

        public class IntArrayComparer :
            IComparer<int[]>
        {
            public int Compare(
                int[] x,
                int[] y)
            {
                int len =
                    Math.Max(
                        x.Length,
                        y.Length);


                for (int i = 0; i < len; i++)
                {
                    int a =
                        i < x.Length
                            ? x[i]
                            : 0;


                    int b =
                        i < y.Length
                            ? y[i]
                            : 0;


                    int cmp =
                        a.CompareTo(b);


                    if (cmp != 0)
                        return cmp;
                }


                return 0;
            }
        }
    }
}