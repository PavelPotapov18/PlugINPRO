using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;

namespace PlugINPRO.Services.AR
{
    public class FinishWallBuilder
    {
        public Wall Create(
            Document doc,
            FinishSegmentInfo info)
        {
            Curve curve =
                OffsetCurveIntoRoom(
                    info.BoundaryCurve,
                    info.Room,
                    info.HostWall,
                    info.FinishType);

            if (info.Height < 1)
            {
                info.Height =
                    UnitUtils.ConvertToInternalUnits(
                        3000,
                        UnitTypeId.Millimeters);
            }

            Wall wall =
                Wall.Create(
                    doc,
                    curve,
                    info.FinishType.Id,
                    info.Room.LevelId,
                    info.Height,
                    info.BaseOffset,
                    true,
                    false);

            JoinGeometryUtils.JoinGeometry(doc, wall, info.HostWall);

            FillParameters(wall, info);

            return wall;
        }

        private void FillParameters(
            Wall wall,
            FinishSegmentInfo info)
        {
            Parameter roomParam =
                wall.LookupParameter("Помещение Имя");

            if (roomParam != null &&
                !roomParam.IsReadOnly)
            {
                roomParam.Set(
                    $"{info.Room.get_Parameter(BuiltInParameter.ROOM_NUMBER).AsString()} {info.Room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString()}");
            }

            Parameter roomBounding =
                wall.get_Parameter(
                    BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);

            if (roomBounding != null)
            {
                roomBounding.Set(0);
            }

            Parameter hostOrientation = info.HostWall.LookupParameter("INP_Ориентация по сторонам света");
            Parameter wallOrientation = wall.LookupParameter("INP_Ориентация по сторонам света");

            if (wallOrientation == null) return;

            wallOrientation.Set(
                hostOrientation != null &&
                !string.IsNullOrEmpty(hostOrientation.AsString())
                    ? hostOrientation.AsString()
                    : "-");

        }
        private Curve OffsetCurveIntoRoom(
            Curve boundaryCurve,
            Room room,
            Wall hostWall,
            WallType finishType)
        {

            XYZ wallNormal = hostWall.Orientation;

            // Центр помещения(BoundingBox даёт приблизительный центр)
            BoundingBoxXYZ bb = room.get_BoundingBox(null);
            XYZ roomCenter = (bb.Min + bb.Max) / 2.0;

            XYZ curvePoint = boundaryCurve.Evaluate(0.5, true);

            XYZ toRoom = (roomCenter - curvePoint).Normalize();

            if (wallNormal.DotProduct(toRoom) < 0)
                wallNormal = wallNormal.Negate();

            double offset = finishType.Width / 2.0;

            return boundaryCurve.CreateTransformed(
                Transform.CreateTranslation(wallNormal * offset));
        }
    }
}
