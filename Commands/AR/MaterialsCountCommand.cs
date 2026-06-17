using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlugINPRO.Commands.AR
{
    [Transaction(TransactionMode.Manual)]
    public class MaterialsCountCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            Document doc = uiApp.ActiveUIDocument.Document;

            // Получаем настройки единиц проекта
            Units units = doc.GetUnits();

            FormatOptions formatOptionsArea =
                units.GetFormatOptions(SpecTypeId.Area);
            FormatOptions formatOptionsVolume =
                units.GetFormatOptions(SpecTypeId.Volume);
            FormatOptions formatOptionsLength =
                units.GetFormatOptions(SpecTypeId.Length);

            ForgeTypeId areaUnitTypeId = formatOptionsArea.GetUnitTypeId();
            ForgeTypeId volumeUnitTypeId = formatOptionsVolume.GetUnitTypeId();
            ForgeTypeId lengthUnitTypeId = formatOptionsLength.GetUnitTypeId();

            //Делаем выборку элементов для подсчета
            List<Element> elems =
                new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(n =>
                    n.Category != null &&
                    (
                    n.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Walls ||
                    n.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Floors ||
                    n.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Roofs ||
                    n.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Ceilings ||
                    n.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors ||
                    n.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Windows ||
                    n.Category.Id.IntegerValue == (int)BuiltInCategory.OST_GenericModel ||
                    n.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFraming
                    )
                )
                .ToList();

            using (Transaction t = new Transaction(doc, "Копирование значений в \"ADSK_Количество\""))
            {
                t.Start();
                foreach (Element elem in elems)
                {
                    Parameter countParameter = elem.LookupParameter("ADSK_Количество");
                    Parameter areaParameter = elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    Parameter volumeParameter = elem.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                    Parameter lengthParameter = elem.LookupParameter("ADSK_Размер_Длина");
                    if (lengthParameter == null)
                    {
                        lengthParameter = elem.get_Parameter(BuiltInParameter.FAMILY_LINE_LENGTH_PARAM);
                    }

                    ElementType elemType = (ElementType)doc.GetElement(elem.GetTypeId());

                    Parameter vomCategoryParam = elemType.LookupParameter("INP_ВОМ_Категория");


                    if (vomCategoryParam != null && !string.IsNullOrEmpty(vomCategoryParam.AsString()))
                    {
                        Parameter vomUnitsParam = elemType.LookupParameter("INP_ВОМ_Единица измерения");
                        switch (vomUnitsParam.AsString())
                        {
                            case "кв.м":
                                SetCountParameterValue(countParameter, areaParameter, areaUnitTypeId);
                                break;
                            case "куб.м":
                                SetCountParameterValue(countParameter, volumeParameter, volumeUnitTypeId);
                                break;
                            case "пог.м":
                                string value = lengthParameter.AsValueString();

                                if (!string.IsNullOrEmpty(value))
                                {
                                    value = value.Replace(" мм", "");
                                    value = value.Replace(",", ".");

                                    if (double.TryParse(
                                        value,
                                        System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out double parsedLength))
                                    {
                                        countParameter.Set(parsedLength / 1000.0);
                                    }
                                }
                                break;
                            case "шт":
                                if (countParameter != null && !countParameter.IsReadOnly)
                                    countParameter.Set(1);
                                break;
                        }
                    }
                }
                t.Commit();
            }    
            
            TaskDialog.Show("Revit", "Параметры элементов успешно заполнены!");
            return Result.Succeeded;
        }

        public void SetCountParameterValue(Parameter countParameter, Parameter parameter, ForgeTypeId unitsTypeId)
        {
            double value = parameter.AsDouble();
            double valueUserUnits = UnitUtils.ConvertFromInternalUnits(value, unitsTypeId);
            if (countParameter != null && !countParameter.IsReadOnly)
                countParameter.Set(valueUserUnits);
        }
    }
}
