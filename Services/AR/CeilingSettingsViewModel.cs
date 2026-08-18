using Autodesk.Revit.DB;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PlugINPRO.Services.AR
{
    public class CeilingSettingsViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<CeilingType> CeilingTypes { get; set; }
            = new ObservableCollection<CeilingType>();

        private CeilingType _selectedCeilingType;

        public CeilingType SelectedCeilingType
        {
            get => _selectedCeilingType;
            set
            {
                if (_selectedCeilingType == value)
                    return;

                _selectedCeilingType = value;
                OnPropertyChanged(nameof(SelectedCeilingType));
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


        // Значение смещения в мм
        public double CustomOffsetMm { get; set; } = 0;

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