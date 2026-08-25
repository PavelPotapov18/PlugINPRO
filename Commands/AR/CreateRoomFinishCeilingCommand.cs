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
    public class CreateRoomFinishCeilingCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Собираем все типоразмеры полов
            List<CeilingType> ceilingTypes =
                new FilteredElementCollector(doc)
                    .OfClass(typeof(CeilingType))
                    .Cast<CeilingType>()
                    .OrderBy(x => x.Name)
                    .ToList();

            if (ceilingTypes.Count == 0)
            {
                TaskDialog.Show(
                    "Ошибка",
                    "В проекте не найдено ни одного типа пола.");
                return Result.Cancelled;
            }

            // Параметры помещений для фильтра
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
                        paramNames.Add(param.Definition.Name);
                }
            }

            // Открываем окно настроек
            CeilingSettingsWindow wnd = new CeilingSettingsWindow(ceilingTypes, paramNames.OrderBy(x => x).ToList());
            bool? result = wnd.ShowDialog();

            if (result != true)
                return Result.Cancelled;
            
            
            List<Room> rooms;

            // После Ok — применяем режим выбора помещений
            if (wnd.ViewModel.IsFilteredRooms)
            {
                rooms = allRooms
                    .Where(r =>
                        RoomFilterMatcher.Match(
                            r,
                            wnd.ViewModel.FilterRules.ToList(),
                            wnd.ViewModel.FilterMatchMode))
                    .ToList();
            }
            else
            {
                // IsSelectedRooms — прокликивание как было
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new RoomSelectionFilter(),
                    "Выберите помещения");

                rooms = refs.Select(r => doc.GetElement(r))
                    .OfType<Room>()
                    .Where(r => r.Area > 0)
                    .ToList();
            }

            CeilingType selectedType = wnd.ViewModel.SelectedCeilingType;

            

            if (rooms.Count == 0)
                return Result.Cancelled;

            RoomCeilingBuilder builder = new RoomCeilingBuilder();

            using (Transaction t = new Transaction(doc, "Создание потолков"))
            {
                t.Start();

                int created = 0;
                foreach (Room room in rooms)
                {
                    Ceiling ceiling =
                        builder.Create(doc, room, selectedType, wnd.ViewModel.CustomOffsetMm);
                    if (ceiling != null)
                        created++;
                }

                t.Commit();
                TaskDialog.Show(
                    "Готово",
                    $"Создано потолков: {created}");
            }

            return Result.Succeeded;
        }

        private class RoomSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
                => elem is Room;

            public bool AllowReference(
                Reference reference, XYZ position)
                => false;
        }
    }
}
