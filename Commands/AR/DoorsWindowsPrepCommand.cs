using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlugINPRO.Commands.AR
{
    [Transaction(TransactionMode.Manual)]
    public class DoorsWindowsPrepCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            List<Room> rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0)
                    .ToList();

            List<Element> elems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => 
                    e.Category != null &&
                    (
                    e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors ||
                    e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Windows
                    )
                )
                .ToList();

            using(Transaction t = new Transaction(doc, "Заполнение параметров окон и дверей"))
            {
                t.Start();

                foreach(Room room in rooms)
                {
                    string roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString();
                    string roomNumber = room.get_Parameter(BuiltInParameter.ROOM_NUMBER).AsString();

                    foreach (Element elem in elems)
                    {
                        FamilyInstance famInst = elem as FamilyInstance;

                        if (famInst.Symbol.Name == "ПВХ" || famInst.Symbol.Name.Contains("Отлив"))
                            continue;

                        

                        Parameter roomNameParam = famInst.LookupParameter("Помещение Имя");

                        if (roomNameParam == null)
                            continue;

                        Wall hostwall = famInst.Host as Wall;
                        if (hostwall.WallType.Function != WallFunction.Exterior)
                            continue;

                        Parameter orientaionParam = famInst.LookupParameter("INP_Ориентация по сторонам света");

                        if (orientaionParam == null)
                            continue;

                        if (string.IsNullOrEmpty(orientaionParam.AsString()))
                        {
                            Parameter wallOrientationParam = famInst.LookupParameter("INP_Ориентация по сторонам света");
                            if (wallOrientationParam != null)
                                orientaionParam.Set(wallOrientationParam.AsString());
                        }

                        ElementId phaseId = famInst.get_Parameter(BuiltInParameter.PHASE_CREATED).AsElementId();
                        Phase phase = doc.GetElement(phaseId) as Phase;
                        Room fromRoom = famInst.get_FromRoom(phase);
                        Room toRoom = famInst.get_ToRoom(phase);

                        bool fromRoomCorrect = fromRoom != null && fromRoom.Id == room.Id;
                        bool toRoomCorrect = toRoom != null && toRoom.Id == room.Id;


                        if (fromRoomCorrect || toRoomCorrect)
                            roomNameParam.Set($"{roomNumber} {roomName}");
                    }
                }
                t.Commit();
            }
            TaskDialog.Show("Успешно", "Параметры окон и дверей успешно заполнены");
            return Result.Succeeded;
        }
    }
}
