using Autodesk.Revit.DB;
using System.ComponentModel;

namespace PlugINPRO.Services.AR
{
    public class FinishLayer : INotifyPropertyChanged
    {
        private WallType _finishWallType;
        public WallType FinishWallType
        {
            get => _finishWallType;
            set
            {
                _finishWallType = value;
                OnPropertyChanged(nameof(FinishWallType));
            }
        }

        // Внутреннее хранение в футах (единицы Revit)
        public double BaseOffset { get; set; } = 0;
        public double Height { get; set; } = 0;

        // Свойства для привязки в UI (мм)
        public double BaseOffsetMm
        {
            get => UnitUtils.ConvertFromInternalUnits(
                BaseOffset, UnitTypeId.Millimeters);
            set => BaseOffset = UnitUtils.ConvertToInternalUnits(
                value, UnitTypeId.Millimeters);
        }

        public double HeightMm
        {
            get => UnitUtils.ConvertFromInternalUnits(
                Height, UnitTypeId.Millimeters);
            set => Height = UnitUtils.ConvertToInternalUnits(
                value, UnitTypeId.Millimeters);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}