using Autodesk.Revit.DB;
using System.ComponentModel;

namespace PlugINPRO.Services.AR
{
    public class FinishLayer : INotifyPropertyChanged
    {
        private WallType _finishWallType;

        // Внутренние единицы Revit (футы)
        private double _baseOffset;

        private double _height;
        public WallType FinishWallType
        {
            get => _finishWallType;
            set
            {
                if (_finishWallType == value)
                    return;
                _finishWallType = value;
                OnPropertyChanged(nameof(FinishWallType));
            }
        }

        

        // =========================================================
        // ВНУТРЕННИЕ ЗНАЧЕНИЯ REVIT
        // =========================================================

        public double BaseOffset
        {
            get => _baseOffset;

            set
            {
                if (_baseOffset == value)
                    return;

                _baseOffset = value;

                OnPropertyChanged(
                    nameof(BaseOffset));

                // Обновляем отображение в мм
                OnPropertyChanged(
                    nameof(BaseOffsetMm));
            }
        }


        public double Height
        {
            get => _height;

            set
            {
                if (_height == value)
                    return;

                _height = value;

                OnPropertyChanged(
                    nameof(Height));

                // Обновляем отображение в мм
                OnPropertyChanged(
                    nameof(HeightMm));
            }
        }


        // =========================================================
        // ЗНАЧЕНИЯ ДЛЯ UI — ММ
        // =========================================================

        public double BaseOffsetMm
        {
            get
            {
                return UnitUtils.ConvertFromInternalUnits(
                    BaseOffset,
                    UnitTypeId.Millimeters);
            }

            set
            {
                double internalValue =
                    UnitUtils.ConvertToInternalUnits(
                        value,
                        UnitTypeId.Millimeters);

                if (System.Math.Abs(
                        BaseOffset - internalValue) < 1e-9)
                {
                    return;
                }

                BaseOffset = internalValue;

                // BaseOffset уже вызовет:
                // OnPropertyChanged(BaseOffset)
                // OnPropertyChanged(BaseOffsetMm)
            }
        }


        public double HeightMm
        {
            get
            {
                return UnitUtils.ConvertFromInternalUnits(
                    Height,
                    UnitTypeId.Millimeters);
            }

            set
            {
                double internalValue =
                    UnitUtils.ConvertToInternalUnits(
                        value,
                        UnitTypeId.Millimeters);

                if (System.Math.Abs(
                        Height - internalValue) < 1e-9)
                {
                    return;
                }

                Height = internalValue;

                // Height уже вызовет:
                // OnPropertyChanged(Height)
                // OnPropertyChanged(HeightMm)
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}