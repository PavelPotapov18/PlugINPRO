using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using PlugINPRO.Services.AR;
using PlugINPRO.UI.Windows.AR;
using System.Collections.Generic;
using System.Linq;

namespace PlugINPRO.Commands.AR
{
    [Transaction(TransactionMode.Manual)]
    public class CreateRoomFinishWallsCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;

            Document doc = uidoc.Document;

            //Собираем все типоразмеры стен:

            List<WallType> wallTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .OrderBy(x => x.Name)
                .ToList();

            // Собираем уникальные имена строковых параметров из помещений
            List<Room> allRooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            HashSet<string> paramNames = new HashSet<string>();
            foreach (Room room in allRooms)
            {
                foreach (Parameter param in room.Parameters)
                {
                    if (param.StorageType == StorageType.String &&
                        !string.IsNullOrEmpty(param.Definition.Name))
                    {
                        paramNames.Add(param.Definition.Name);
                    }
                }
            }

            FinishSettingsWindow wnd = new FinishSettingsWindow(wallTypes, paramNames.OrderBy(x => x).ToList());
            bool? result = wnd.ShowDialog();

            if (result != true)
            {
                return Result.Cancelled;
            }

            FinishWallsViewModel vm = wnd.ViewModel;

            TaskDialog.Show("Rules", $"Правил: {vm.Rules.Count}");

            // Добавь сразу после получения vm из окна
            string debug = "";
            foreach (var rule in vm.Rules)
            {
                debug += $"Правило: SourceWallType={rule.SourceWallType?.Name ?? "NULL"}\n";
                foreach (var layer in rule.Layers)
                {
                    debug += $"  Слой: FinishWallType={layer.FinishWallType?.Name ?? "NULL"}, " +
                             $"BaseOffset={layer.BaseOffsetMm}, Height={layer.HeightMm}\n";
                }
            }
            TaskDialog.Show("DEBUG Rules", debug);

            List<Room> rooms = new List<Room>();

            
            if(vm.IsSelectedRooms)
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new RoomSelectionFilter(),
                    "Выберите помещения");

                rooms = refs.Select(x => doc.GetElement(x))
                    .OfType<Room>()
                    .ToList();
            }
            else if (vm.IsFilteredRooms)
            {
                rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0)
                    .Where(r => RoomFilterMatcher.Match(r, vm.FilterRules.ToList(),vm.FilterMatchMode))
                    .ToList();
            }

            RoomBoundaryService service = new RoomBoundaryService();

            FinishWallBuilder builder = new FinishWallBuilder();

            
            using (Transaction t = new Transaction(doc, "Отделка"))
            {
                t.Start();

                foreach (Room room in rooms)
                {
                    List<FinishSegmentInfo> segments =
                        service.GetSegments(
                            doc,
                            room,
                            vm.Rules.ToList());

                    foreach (FinishSegmentInfo segment in segments)
                    {
                        Wall wall = builder.Create(doc, segment);
                    }
                }

                t.Commit();
            }
            TaskDialog.Show("Revit", "Отделка успешно построена");
            return Result.Succeeded;
        }

        public class RoomSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Room;
            }

            public bool AllowReference(
                Reference reference,
                XYZ position)
            {
                return false;
            }
        }
    }
}