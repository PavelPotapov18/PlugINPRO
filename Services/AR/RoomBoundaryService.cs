using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System.Collections.Generic;
using System.Linq;

namespace PlugINPRO.Services.AR
{
    public class RoomBoundaryService
    {
        public List<FinishSegmentInfo> GetSegments(
            Document doc,
            Room room,
            List<WallFinishRule> rules)
        {
            List<FinishSegmentInfo> result =
                new List<FinishSegmentInfo>();

            SpatialElementBoundaryOptions options =
                new SpatialElementBoundaryOptions();

            IList<IList<BoundarySegment>> loops =
                room.GetBoundarySegments(options);

            if (loops == null)
                return result;

            foreach (IList<BoundarySegment> loop in loops)
            {
                foreach (BoundarySegment segment in loop)
                {
                    Element host =
                        doc.GetElement(segment.ElementId);

                    if (!(host is Wall wall))
                        continue;

                    WallFinishRule rule =
                        rules.FirstOrDefault(x =>
                            x.SourceWallType != null &&
                            x.SourceWallType.Id == wall.WallType.Id);

                    if (rule == null || rule.Layers.Count == 0)
                        continue;

                    // Для каждого слоя — отдельный FinishSegmentInfo
                    foreach (FinishLayer layer in rule.Layers)
                    {
                        if (layer.FinishWallType == null)
                            continue;

                        result.Add(new FinishSegmentInfo
                        {
                            Room = room,
                            HostWall = wall,
                            BoundaryCurve = segment.GetCurve(),
                            FinishType = layer.FinishWallType,
                            Height = layer.Height > 0
                                ? layer.Height
                                : room.UnboundedHeight,
                            BaseOffset = layer.BaseOffset
                        });
                    }
                }
            }

            return result;
        }
    }
}