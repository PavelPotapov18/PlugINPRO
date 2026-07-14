using Autodesk.Revit.DB;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PlugINPRO.Services.AR
{
    public class FinishWallsViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<WallFinishRule> Rules { get; set; }
            = new ObservableCollection<WallFinishRule>();

        public ObservableCollection<WallType> WallTypes { get; set; }
            = new ObservableCollection<WallType>();

        // Список параметров помещений для выбора в фильтре
        public ObservableCollection<string> RoomParameterNames { get; set; }
            = new ObservableCollection<string>();

        // Правила фильтрации помещений
        public ObservableCollection<RoomFilterRule> FilterRules { get; set; }
            = new ObservableCollection<RoomFilterRule>();

        private bool _isAllRooms = true;
        private bool _isSelectedRooms;
        private bool _isFilteredRooms;

        public bool IsAllRooms
        {
            get => _isAllRooms;
            set { _isAllRooms = value; OnPropertyChanged(nameof(IsAllRooms)); }
        }

        public bool IsSelectedRooms
        {
            get => _isSelectedRooms;
            set { _isSelectedRooms = value; OnPropertyChanged(nameof(IsSelectedRooms)); }
        }

        public bool IsFilteredRooms
        {
            get => _isFilteredRooms;
            set
            {
                _isFilteredRooms = value;
                OnPropertyChanged(nameof(IsFilteredRooms));
                OnPropertyChanged(nameof(IsFilterPanelVisible));
            }
        }

        public bool IsFilterPanelVisible => _isFilteredRooms;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}