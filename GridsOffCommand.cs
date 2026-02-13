using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlugINPRO
{
    [Transaction(TransactionMode.Manual)]
    internal class GridsOffCommand : IExternalCommand
    {
        public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            Document doc = uiApp.ActiveUIDocument.Document;

            // --- ОКНО ВВОДА ---
            GridsOffWindow window = new GridsOffWindow();
            // привязываем к окну Revit
            var helper = new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = uiApp.MainWindowHandle
            };

            bool? dialogResult = window.ShowDialog();

            if (dialogResult != true)
                return Result.Cancelled;

            //получаем требуемый суффикс для фильтра
            string suffix = window.Suffix;

            string filterName = "ОСИ_УРОВНИ_Связанных файлов";

            using (Transaction t = new Transaction(doc, "Создание фильтра осей и уровней"))
            {
                t.Start();

                // --- ИЩЕМ СУЩЕСТВУЮЩИЙ ФИЛЬТР ---
                ParameterFilterElement existingFilter =
                    new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .FirstOrDefault(f => f.Name == filterName);

                // --- СОЗДАЁМ ПРАВИЛО ---
                ParameterValueProvider provider =
                    new ParameterValueProvider(
                        new ElementId(BuiltInParameter.ALL_MODEL_TYPE_NAME));

                FilterRule containsRule = new FilterStringRule(
                    provider,
                    new FilterStringContains(),
                    suffix
                );

                FilterRule inverseRule = new FilterInverseRule(containsRule);

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
                    categories = new List<ElementId>
                    {
                        new ElementId(BuiltInCategory.OST_Levels),
                        new ElementId(BuiltInCategory.OST_Grids)
                    };
                }

                // --- СОЗДАЁМ ФИЛЬТР ---
                ParameterFilterElement newFilter =
                    ParameterFilterElement.Create(
                        doc,
                        filterName,
                        categories,
                        elementFilter);

                // --- ДОБАВЛЯЕМ ВО ВСЕ ШАБЛОНЫ ВИДОВ ---
                IEnumerable<View> viewTemplates =
                    new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v =>
                        v.IsTemplate &&
                        (v is ViewPlan || v is ViewSection || v is View3D));

                foreach (View view in viewTemplates)
                {
                    if (!view.GetFilters().Contains(newFilter.Id))
                    {
                        view.AddFilter(newFilter.Id);
                        view.SetFilterVisibility(newFilter.Id, false);
                    }
                }

                t.Commit();
            }
            TaskDialog.Show("Готово", $"Фильтр \"{filterName}\" создан и применён к шаблонам видов.");
            return Result.Succeeded;
        }
    }
}
