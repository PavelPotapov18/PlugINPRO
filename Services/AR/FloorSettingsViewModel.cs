using Autodesk.Revit.DB;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PlugINPRO.Services.AR
{
    public class FloorSettingsViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<FloorType> FloorTypes { get; set; }
            = new ObservableCollection<FloorType>();

        public FloorType SelectedFloorType { get; set; }

        private DoorOffsetMode _doorOffsetMode = DoorOffsetMode.None;
        public DoorOffsetMode DoorOffsetMode
        {
            get => _doorOffsetMode;
            set
            {
                _doorOffsetMode = value;
                OnPropertyChanged(nameof(DoorOffsetMode));
                OnPropertyChanged(nameof(IsCustomOffset));
            }
        }

        // Видимость поля ввода — только для Custom
        public bool IsCustomOffset => _doorOffsetMode == DoorOffsetMode.Custom;

        // Значение смещения в мм (для Custom)
        public double CustomOffsetMm { get; set; } = 0;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}