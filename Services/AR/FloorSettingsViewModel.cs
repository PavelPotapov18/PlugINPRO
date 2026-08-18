using Autodesk.Revit.DB;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PlugINPRO.Services.AR
{
    public class FloorSettingsViewModel : INotifyPropertyChanged
    {
        // =========================================================
        // ПОЛЫ
        // =========================================================

        public ObservableCollection<FloorType> FloorTypes { get; }
            = new ObservableCollection<FloorType>();

        private FloorType _selectedFloorType;

        public FloorType SelectedFloorType
        {
            get => _selectedFloorType;
            set
            {
                if (_selectedFloorType == value)
                    return;

                _selectedFloorType = value;
                OnPropertyChanged(nameof(SelectedFloorType));
            }
        }


        // =========================================================
        // РЕЖИМ ОБРАБОТКИ ПОМЕЩЕНИЙ
        // =========================================================

        private bool _isSelectedRooms = true;
        private bool _isFilteredRooms = false;

        public bool IsSelectedRooms
        {
            get => _isSelectedRooms;

            set
            {
                if (_isSelectedRooms == value)
                    return;

                _isSelectedRooms = value;

                // Если выбрали помещения вручную,
                // фильтрация должна быть выключена
                if (value)
                {
                    _isFilteredRooms = false;

                    OnPropertyChanged(
                        nameof(IsFilteredRooms));

                    OnPropertyChanged(
                        nameof(IsFilterPanelVisible));
                }

                OnPropertyChanged(
                    nameof(IsSelectedRooms));
            }
        }

        public bool IsFilteredRooms
        {
            get => _isFilteredRooms;

            set
            {
                if (_isFilteredRooms == value)
                    return;

                _isFilteredRooms = value;

                // Если включили фильтр,
                // ручной выбор помещений выключаем
                if (value)
                {
                    _isSelectedRooms = false;

                    OnPropertyChanged(
                        nameof(IsSelectedRooms));
                }

                OnPropertyChanged(
                    nameof(IsFilteredRooms));

                OnPropertyChanged(
                    nameof(IsFilterPanelVisible));
            }
        }

        public bool IsFilterPanelVisible
        {
            get => IsFilteredRooms;
        }


        // =========================================================
        // ФИЛЬТР
        // =========================================================

        public ObservableCollection<string> RoomParameterNames { get; }
            = new ObservableCollection<string>();

        public ObservableCollection<RoomFilterRule> FilterRules { get; }
            = new ObservableCollection<RoomFilterRule>();


        private RoomFilterMatchMode _filterMatchMode =
            RoomFilterMatchMode.All;

        public RoomFilterMatchMode FilterMatchMode
        {
            get => _filterMatchMode;

            set
            {
                if (_filterMatchMode == value)
                    return;

                _filterMatchMode = value;

                OnPropertyChanged(
                    nameof(FilterMatchMode));
            }
        }


        // =========================================================
        // ДВЕРИ
        // =========================================================

        private DoorOffsetMode _doorOffsetMode =
            DoorOffsetMode.None;

        public DoorOffsetMode DoorOffsetMode
        {
            get => _doorOffsetMode;

            set
            {
                if (_doorOffsetMode == value)
                    return;

                _doorOffsetMode = value;

                OnPropertyChanged(
                    nameof(DoorOffsetMode));

                OnPropertyChanged(
                    nameof(IsCustomOffset));
            }
        }

        public bool IsCustomOffset =>
            DoorOffsetMode == DoorOffsetMode.Custom;


        // =========================================================
        // СМЕЩЕНИЕ
        // =========================================================

        private double _customOffsetMm;

        public double CustomOffsetMm
        {
            get => _customOffsetMm;

            set
            {
                if (_customOffsetMm == value)
                    return;

                _customOffsetMm = value;

                OnPropertyChanged(
                    nameof(CustomOffsetMm));
            }
        }


        // =========================================================
        // PROPERTY CHANGED
        // =========================================================

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(
            string propertyName)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }
    }
}