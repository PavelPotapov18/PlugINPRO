using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PlugINPRO.Commands.Common
{
    [Transaction(TransactionMode.Manual)]
    internal class ThreeDByWorksetsCommand : IExternalCommand
    {
        public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            Document doc = uiApp.ActiveUIDocument.Document;

            //Получаем все пользовательские рабочие наборы
            List<int> worksetIds = new List<int>();

            List<Workset> worksets = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .ToWorksets()
                .ToList();
            foreach (Workset workset in worksets)
            {
                worksetIds.Add(workset.Id.IntegerValue);
            }


            //Получаем вид {3D}
            View view = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => v.ViewType == ViewType.ThreeD && v.Name == "{3D}");

            //Получаем все элементы на виде {3D}
            List<Element> elems = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .ToList();
            
            //Получаем список с ID категорий
            List<ElementId> catIds = new List<ElementId>();
            foreach (Element element in elems)
            {
                if (element.Category != null)
                {
                    ElementId elemCatId = element.Category.Id;

                    if (!catIds.Contains(elemCatId))
                        catIds.Add(elemCatId);
                }
            }

            using(Transaction t = new Transaction(doc, "Создание видов и фильтров"))
            {
                t.Start();
                foreach (int id in worksetIds)
                {
                    WorksetTable table = doc.GetWorksetTable();
                    Workset workset = table.GetWorkset(new WorksetId(id));

                    string newName = "";
                    if (workset.Name.Contains("#"))
                        newName = workset.Name.Replace("#", "");
                    else
                        newName = workset.Name;

                    string filterName = $"Рабочий набор_{newName}";

                    // --- ИЩЕМ СУЩЕСТВУЮЩИЙ ФИЛЬТР ---
                    ParameterFilterElement existingFilter =
                        new FilteredElementCollector(doc)
                        .OfClass(typeof(ParameterFilterElement))
                        .Cast<ParameterFilterElement>()
                        .FirstOrDefault(f => f.Name == filterName);

                    // --- СОЗДАЁМ ПРАВИЛО ---
                    ParameterValueProvider provider =
                        new ParameterValueProvider(
                            new ElementId(BuiltInParameter.ELEM_PARTITION_PARAM));

                    FilterRule equalsRule = new FilterIntegerRule(
                    provider,
                    new FilterNumericEquals(),
                    id
                    );
                    FilterRule inverseRule = new FilterInverseRule(equalsRule);

                    IList<FilterRule> rules = new List<FilterRule> { inverseRule };
                    ElementParameterFilter elementFilter =
                        new ElementParameterFilter(rules);
                    
                    // --- КАТЕГОРИИ ---
                    IList<ElementId> categories;

                    if (existingFilter != null)
                    {
                        categories = existingFilter.GetCategories().ToList();
                        doc.Delete(existingFilter.Id);
                    }
                    else
                    {
                        categories = catIds;
                    }

                    // --- СОЗДАЁМ ФИЛЬТР ---
                    ParameterFilterElement newFilter =
                        ParameterFilterElement.Create(
                            doc,
                            filterName,
                            categories,
                            elementFilter);

                    // --- ДОБАВЛЯЕМ НА ВИД ---
                    View newView = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .FirstOrDefault(v => v.Name == filterName && v is View3D);

                    if(newView == null)
                    {
                        newView = (View)doc.GetElement(view.Duplicate(ViewDuplicateOption.Duplicate));
                        newView.Name = filterName;
                    }

                    if(!newView.IsFilterApplied(newFilter.Id))
                    {
                        newView.AddFilter(newFilter.Id);
                    }

                    newView.SetFilterVisibility(newFilter.Id, false);

                    newView.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Levels), true);
                    newView.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Site), true);
                }
                t.Commit();
            }    

            TaskDialog.Show("Revit", "3D виды по рабочим наборам успешно созданы");
            return Result.Succeeded;
        }
    }
}
