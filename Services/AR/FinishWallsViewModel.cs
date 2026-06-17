using Autodesk.Revit.DB;
using System.Collections.ObjectModel;

namespace PlugINPRO.Services.AR
{
    public class FinishWallsViewModel
    {
        public ObservableCollection<WallFinishRule> Rules
        {
            get;
            set;
        }
        = new ObservableCollection<WallFinishRule>();

        public ObservableCollection<WallType> WallTypes
        {
            get;
            set;
        }
        = new ObservableCollection<WallType>();

        public bool IsAllRooms { get; set; } = true;

        public bool IsSelectedRooms { get; set; }
    }

}
