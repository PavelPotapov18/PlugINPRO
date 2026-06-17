using Autodesk.Revit.DB;
using System.Collections.ObjectModel;

namespace PlugINPRO.Services.AR
{
    public class FloorSettingsViewModel
    {
        public ObservableCollection<FloorType> FloorTypes { get; set; }
            = new ObservableCollection<FloorType>();

        public FloorType SelectedFloorType { get; set; }
    }
}