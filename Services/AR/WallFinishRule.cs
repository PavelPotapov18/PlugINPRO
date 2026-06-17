using Autodesk.Revit.DB;
using System.Collections.ObjectModel;

namespace PlugINPRO.Services.AR
{
    public class WallFinishRule
    {
        public WallType SourceWallType { get; set; }

        public ObservableCollection<FinishLayer> Layers { get; set; }
            = new ObservableCollection<FinishLayer>();
    }
}
