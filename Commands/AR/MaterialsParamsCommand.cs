using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;


namespace PlugINPRO.Commands.AR
{
    [Transaction(TransactionMode.Manual)]
    public class MaterialsParamsCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            Document doc = uiApp.ActiveUIDocument.Document;

            //Делаем выборку элементов модели
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

            ISet<WallType> wallTypesSet = new HashSet<WallType>();
            ISet<FloorType> floorTypesSet = new HashSet<FloorType>();
            ISet<RoofType> roofTypesSet = new HashSet<RoofType>();
            ISet<ElementType> ceilingTypesSet = new HashSet<ElementType>();
            ISet<ElementType> doorTypesSet = new HashSet<ElementType>();
            ISet<ElementType> windowTypesSet = new HashSet<ElementType>();
            ISet<ElementType> otherTypesSet = new HashSet<ElementType>();


            foreach (Element elem in elems)
            {
                ElementType elemType = (ElementType)doc.GetElement(elem.GetTypeId());

                if (elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Walls && 
                    !wallTypesSet.Contains((WallType)elemType))
                {
                    wallTypesSet.Add((WallType)elemType);
                }
                
                else if(elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Floors &&
                    !floorTypesSet.Contains((FloorType)elemType))
                {
                    floorTypesSet.Add((FloorType)elemType);
                }
                
                else if (elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Roofs &&
                    !roofTypesSet.Contains((RoofType)elemType))
                {
                    roofTypesSet.Add((RoofType)elemType);
                }
                
                else if (elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Ceilings &&
                    !ceilingTypesSet.Contains(elemType))
                {
                    ceilingTypesSet.Add(elemType);
                }
                else if (elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors &&
                    !doorTypesSet.Contains(elemType))
                {
                    doorTypesSet.Add(elemType);
                }
                else if (elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Windows &&
                    !windowTypesSet.Contains(elemType))
                {
                    windowTypesSet.Add(elemType);
                }
                else if ((elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_GenericModel || elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFraming) &&
                    !otherTypesSet.Contains(elemType))
                {
                    otherTypesSet.Add(elemType);
                }
            }

            foreach(WallType wallType in wallTypesSet)
            {
                WallKind kind = wallType.Kind;
                string typeName = wallType.Name;
                Parameter vomCategoryParam = wallType.LookupParameter("INP_ВОМ_Категория");
                Parameter vomUnitsParam = wallType.LookupParameter("INP_ВОМ_Единица измерения");

                if (kind == WallKind.Curtain)
                {
                    SetParametersValues(doc, vomCategoryParam,"7.Окна", vomUnitsParam, "кв.м");
                }
                else if(typeName.Contains("Наружная_Цоколь"))
                {
                    SetParametersValues(doc, vomCategoryParam, "1.Цоколь", vomUnitsParam, "кв.м");
                }
                else if (typeName.Contains("Наружная_Сэндвич-панель"))
                {
                    SetParametersValues(doc, vomCategoryParam, "2.Наружные стены", vomUnitsParam, "кв.м");
                }
                else if (typeName.Contains("Внутренняя"))
                {
                    SetParametersValues(doc, vomCategoryParam, "3.Внутренние стены", vomUnitsParam, "куб.м");
                }
                else
                {
                    continue;
                }
            }

            foreach (FloorType floorType in floorTypesSet)
            {
                string typeName = floorType.Name;
                Parameter vomCategoryParam = floorType.LookupParameter("INP_ВОМ_Категория");
                Parameter vomUnitsParam = floorType.LookupParameter("INP_ВОМ_Единица измерения");
                if(typeName.Contains("Пол"))
                {
                    SetParametersValues(doc, vomCategoryParam, "5.Покрытие пола", vomUnitsParam, "кв.м");
                }
            }

            foreach (RoofType roofType in roofTypesSet)
            {
                Parameter vomCategoryParam = roofType.LookupParameter("INP_ВОМ_Категория");
                Parameter vomUnitsParam = roofType.LookupParameter("INP_ВОМ_Единица измерения");
                SetParametersValues(doc, vomCategoryParam, "4.Кровельное покрытие", vomUnitsParam, "кв.м");                
            }

            foreach (ElementType ceilingType in ceilingTypesSet)
            {
                Parameter vomCategoryParam = ceilingType.LookupParameter("INP_ВОМ_Категория");
                Parameter vomUnitsParam = ceilingType.LookupParameter("INP_ВОМ_Единица измерения");
                SetParametersValues(doc, vomCategoryParam, "8.Потолок", vomUnitsParam, "кв.м");
            }

            foreach (ElementType doorType in doorTypesSet)
            {
                Parameter vomCategoryParam = doorType.LookupParameter("INP_ВОМ_Категория");
                Parameter vomUnitsParam = doorType.LookupParameter("INP_ВОМ_Единица измерения");
                SetParametersValues(doc, vomCategoryParam, "6.Ворота, двери", vomUnitsParam, "шт");
            }

            foreach (ElementType windowType in windowTypesSet)
            {
                string typeName = windowType.Name;
                Parameter vomCategoryParam = windowType.LookupParameter("INP_ВОМ_Категория");
                Parameter vomUnitsParam = windowType.LookupParameter("INP_ВОМ_Единица измерения");
                if(!typeName.Contains("Отлив") && !typeName.Contains("ПВХ"))
                {
                    SetParametersValues(doc, vomCategoryParam, "7.Окна", vomUnitsParam, "шт");
                }
                else
                {
                    continue;
                }
            }
            foreach (ElementType otherType in otherTypesSet)
            {
                string typeName = otherType.Name;
                Parameter vomCategoryParam = otherType.LookupParameter("INP_ВОМ_Категория");
                Parameter vomUnitsParam = otherType.LookupParameter("INP_ВОМ_Единица измерения");
                if (otherType.FamilyName.Contains("BIMLIB_ФасонныеИзделия"))
                {
                    SetParametersValues(doc, vomCategoryParam, "9.Фасонные изделия", vomUnitsParam, "пог.м");
                }
                else if(otherType.FamilyName.Contains("Перемычка_share"))
                {
                    SetParametersValues(doc, vomCategoryParam, "10.Перемычки", vomUnitsParam, "шт");
                }
                else if (otherType.FamilyName.Contains("Уголок"))
                {
                    SetParametersValues(doc, vomCategoryParam, "10.Перемычки", vomUnitsParam, "пог.м");
                }
                else
                {
                    continue;
                }
            }


            TaskDialog.Show("Revit", "Параметры элементов успешно заполнены!");
            return Result.Succeeded;
        }

        

        public void SetParametersValues(Document doc, Parameter param1, string value1, Parameter param2, string value2)
        {
            using(Transaction t = new Transaction(doc, "Заполнение параметров типоразмера"))
            {
                t.Start();
                param1.Set(value1);
                param2.Set(value2);
                t.Commit();
            }
        }
    }
}
