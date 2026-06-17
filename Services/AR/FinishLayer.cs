using Autodesk.Revit.DB;

namespace PlugINPRO.Services.AR
{
    public class FinishLayer
    {
        public WallType FinishWallType { get; set; }

        // Отступ от низа стены-основы, в единицах Revit (футы внутри)
        public double BaseOffset { get; set; } = 0;

        // Высота слоя отделки, в единицах Revit
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
    }
}