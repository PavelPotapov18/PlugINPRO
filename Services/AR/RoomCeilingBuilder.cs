using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlugINPRO.Services.AR
{
    public class RoomCeilingBuilder
    {
        public Ceiling Create(
            Document doc,
            Room room,
            CeilingType ceilingType,
            double offset)
        {
            // Получаем контуры помещения (внешний контур + вырезаемые внутренние контуры (если такие имеются))
            List<CurveLoop> loops = GetRoomBoundaryLoop(doc, room);
            if (loops == null)
                return null;

            Ceiling ceiling = Ceiling.Create(
                doc,
                loops,
                ceilingType.Id,
                room.LevelId);

            FillParameters(ceiling, room, offset);
            return ceiling;
        }

        private List<CurveLoop> GetRoomBoundaryLoop(
            Document doc,
            Room room)
        {
            List<CurveLoop> finalCurveLoops = new List<CurveLoop>();
            SpatialElementBoundaryOptions options =
                new SpatialElementBoundaryOptions();

            IList<IList<BoundarySegment>> loops =
                room.GetBoundarySegments(options);

            if (loops == null || loops.Count == 0)
                return null;
            TaskDialog.Show("Loops", loops.Count.ToString());

            
            CurveLoop simplLoop = new CurveLoop();
            foreach (BoundarySegment seg in loops[0])
                simplLoop.Append(seg.GetCurve());
            finalCurveLoops.Add(simplLoop);
            

            if (loops.Count > 1)
            {
                
                for (int i = 1; i < loops.Count; i++)
                {
                    CurveLoop extraLoop = new CurveLoop();
                    foreach (BoundarySegment seg in loops[i])
                        extraLoop.Append(seg.GetCurve());
                    finalCurveLoops.Add(extraLoop);                   
                }
            }
           return finalCurveLoops;
        }

        private void FillParameters(
            Ceiling ceiling,
            Room room,
            double offset)
        {
            Parameter roomParam = ceiling.LookupParameter("Помещение Имя");
            Parameter ceilingOffset = ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM);

            if (roomParam != null && !roomParam.IsReadOnly)
                roomParam.Set($"{room.get_Parameter(BuiltInParameter.ROOM_NUMBER).AsString()} {room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString()}");

            ceilingOffset.Set(UnitUtils.ConvertToInternalUnits(offset, UnitTypeId.Millimeters));
        }
    }
}