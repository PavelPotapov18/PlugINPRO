using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System.Collections.Generic;

namespace PlugINPRO.Services.AR
{
    public class RoomFloorBuilder
    {
        public Floor Create(
            Document doc,
            Room room,
            FloorType floorType)
        {
            // Получаем контур помещения
            CurveLoop loop = GetRoomBoundaryLoop(doc, room);
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
            Room room)
        {
            SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions();

            IList<IList<BoundarySegment>> loops = room.GetBoundarySegments(options);

            if (loops == null || loops.Count == 0)
                return null;

            // Берём внешний контур (первый loop)
            CurveLoop curveLoop = new CurveLoop();
            foreach (BoundarySegment segment in loops[0])
                curveLoop.Append(segment.GetCurve());

            return curveLoop;
        }

        private void FillParameters(
            Floor floor,
            Room room)
        {
            Parameter roomParam =
                floor.LookupParameter("Помещение Имя");

            if (roomParam != null && !roomParam.IsReadOnly)
                roomParam.Set($"{room.get_Parameter(BuiltInParameter.ROOM_NUMBER).AsString()} {room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString()}");
        }
    }
}