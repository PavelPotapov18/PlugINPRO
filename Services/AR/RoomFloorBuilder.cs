using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;

namespace PlugINPRO.Services.AR
{
    public class RoomFloorBuilder
    {
        public Floor Create(
            Document doc,
            Room room,
            FloorType floorType,
            DoorOffsetMode doorMode,
            double customOffsetMm = 0)
        {
            // Получаем контур помещения
            CurveLoop loop = GetRoomBoundaryLoop(doc, room, doorMode, customOffsetMm);
            if (loop == null)
                return null;

            Floor floor = Floor.Create(
                doc,
                new List<CurveLoop> { loop },
                floorType.Id,
                room.LevelId);

            FillParameters(floor, room);
            return floor;
        }

        private CurveLoop GetRoomBoundaryLoop(
            Document doc,
            Room room,
            DoorOffsetMode doorMode,
            double customOffsetMm)
        {
            SpatialElementBoundaryOptions options =
                new SpatialElementBoundaryOptions();

            IList<IList<BoundarySegment>> loops =
                room.GetBoundarySegments(options);

            if (loops == null || loops.Count == 0)
                return null;

            if (doorMode == DoorOffsetMode.None)
            {
                CurveLoop simplLoop = new CurveLoop();
                foreach (BoundarySegment seg in loops[0])
                    simplLoop.Append(seg.GetCurve());
                return simplLoop;
            }



            // Получаем стадию
            Phase phase = doc.GetElement(room.get_Parameter(BuiltInParameter.ROOM_PHASE).AsElementId()) as Phase;

            // Двери, у которых FromRoom или ToRoom == наше помещение
            List<FamilyInstance> roomDoors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(d => phase != null && (
                    d.get_FromRoom(phase)?.Id == room.Id ||
                    d.get_ToRoom(phase)?.Id == room.Id))
                .ToList();

            List<Curve> resultCurves = new List<Curve>();

            
            foreach (BoundarySegment segment in loops[0])
            {
                Curve segCurve = segment.GetCurve();
                Element boundaryElem = doc.GetElement(segment.ElementId);
                
                    if (!(boundaryElem is Wall wall))
                {
                    resultCurves.Add(segCurve);
                    continue;
                }
                        
                // Ищем двери в этой стене
                List<FamilyInstance> doorsInWall = roomDoors
                    .Where(d => d.Host?.Id == wall.Id)
                    .ToList();

                if (doorsInWall.Count == 0)
                {
                    resultCurves.Add(segCurve);
                    continue;
                }

                // Считаем величину выступа
                double offset = 0;
                switch (doorMode)
                {
                    case DoorOffsetMode.ToWallCenter:
                        offset = wall.Width / 2.0;
                        break;
                    case DoorOffsetMode.ToWallFull:
                        offset = wall.Width;
                        break;
                    case DoorOffsetMode.Custom:
                        offset = UnitUtils.ConvertToInternalUnits(
                            customOffsetMm, UnitTypeId.Millimeters);
                        break;
                }

                // Направление выступа — наружу из помещения
                XYZ wallNormal = wall.Orientation;
                BoundingBoxXYZ bb = room.get_BoundingBox(null);
                XYZ roomCenter = (bb.Min + bb.Max) / 2.0;
                XYZ segMid = segCurve.Evaluate(0.5, true);
                XYZ toRoom = (roomCenter - segMid).Normalize();
                if (wallNormal.DotProduct(toRoom) > 0)
                    wallNormal = wallNormal.Negate();

                // Направление вдоль сегмента
                XYZ segStart = segCurve.GetEndPoint(0);
                XYZ segEnd = segCurve.GetEndPoint(1);
                XYZ segDir = (segEnd - segStart).Normalize();

                // Нормаль должна смотреть ИЗ помещения
                if (wallNormal.DotProduct(toRoom) > 0)
                    wallNormal = wallNormal.Negate();

                // Сортируем двери вдоль сегмента
                doorsInWall = doorsInWall
                    .OrderBy(d => segDir.DotProduct(
                        (d.Location as LocationPoint).Point - segStart))
                    .ToList();

                XYZ cursor = segStart;
                
                foreach (FamilyInstance door in doorsInWall)
                {
                    XYZ doorCenter = (door.Location as LocationPoint).Point;

                    // Ширина проёма
                    double doorWidth = door.get_Parameter(BuiltInParameter.FAMILY_WIDTH_PARAM)?.AsDouble() ?? 0;
                    if (doorWidth == 0)
                        doorWidth = door.Symbol.get_Parameter(BuiltInParameter.FAMILY_WIDTH_PARAM)?.AsDouble() ?? 0;


                    // Проекция центра двери на сегмент
                    double tCenter = segDir.DotProduct(doorCenter - segStart);
                    double tStart = tCenter - doorWidth / 2.0;
                    double tEnd = tCenter + doorWidth / 2.0;

                    double doorWidthMm = UnitUtils.ConvertFromInternalUnits(doorWidth, UnitTypeId.Millimeters);
                    double offsetMm = UnitUtils.ConvertFromInternalUnits(offset, UnitTypeId.Millimeters);

                                       

                    XYZ doorPtStart = segStart + segDir * tStart;
                    XYZ doorPtEnd = segStart + segDir * tEnd;

                    // Отрезок вдоль стены до начала проёма
                    if ((doorPtStart - cursor).GetLength() > 1e-6)
                        resultCurves.Add(Line.CreateBound(cursor, doorPtStart));

                    // Выступ — три отрезка вокруг проёма
                    XYZ p1 = doorPtStart;
                    XYZ p2 = doorPtStart + wallNormal * offset;
                    XYZ p3 = doorPtEnd + wallNormal * offset;
                    XYZ p4 = doorPtEnd;

                    resultCurves.Add(Line.CreateBound(p1, p2));
                    resultCurves.Add(Line.CreateBound(p2, p3));
                    resultCurves.Add(Line.CreateBound(p3, p4));

                    cursor = doorPtEnd;
                }

                // Остаток сегмента после последней двери
                if ((segEnd - cursor).GetLength() > 1e-6)
                    resultCurves.Add(Line.CreateBound(cursor, segEnd));
            }
            // Собираем CurveLoop из отрезков
            CurveLoop curveLoop = new CurveLoop();
            foreach (Curve c in resultCurves)
                curveLoop.Append(c);

            return curveLoop;
        }

        private void FillParameters(
            Floor floor,
            Room room)
        {
            Parameter roomParam = floor.LookupParameter("Помещение Имя");

            if (roomParam != null && !roomParam.IsReadOnly)
                roomParam.Set($"{room.get_Parameter(BuiltInParameter.ROOM_NUMBER).AsString()} {room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString()}");
        }
    }
}