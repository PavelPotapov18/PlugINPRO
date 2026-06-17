using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace PlugINPRO.Services.AR
{
    public class FinishSegmentInfo
    {
        public Room Room { get; set; }

        public Wall HostWall { get; set; }

        public Curve BoundaryCurve { get; set; }

        public WallType FinishType { get; set; }

        public double Height { get; set; }

        public double BaseOffset { get; set; }
    }
}