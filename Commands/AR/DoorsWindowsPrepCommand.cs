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
    public class DoorsWindowsPrepCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uidoc =
                commandData.Application.ActiveUIDocument;

            if (uidoc == null)
            {
                message = "Не удалось получить UIDocument.";
                return Result.Failed;
            }

            Document doc =
                uidoc.Document;

            if (doc == null)
            {
                message = "Не удалось получить Document.";
                return Result.Failed;
            }


            // =====================================================
            // СОБИРАЕМ ДВЕРИ И ОКНА
            // =====================================================

            List<FamilyInstance> doorsAndWindows =
                new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(x =>
                        x.Category != null &&
                        (
                            x.Category.Id.IntegerValue ==
                                (int)BuiltInCategory.OST_Doors ||

                            x.Category.Id.IntegerValue ==
                                (int)BuiltInCategory.OST_Windows
                        ))
                    .ToList();


            if (doorsAndWindows.Count == 0)
            {
                TaskDialog.Show(
                    "Информация",
                    "В проекте не найдено ни одного окна или двери.");

                return Result.Cancelled;
            }


            int processed = 0;
            int skipped = 0;


            // =====================================================
            // ТРАНЗАКЦИЯ
            // =====================================================

            using (Transaction t =
                new Transaction(
                    doc,
                    "Заполнение параметров окон и дверей"))
            {
                t.Start();


                foreach (FamilyInstance famInst
                         in doorsAndWindows)
                {
                    try
                    {
                        // =================================================
                        // 1. ПРОВЕРЯЕМ FAMILY SYMBOL
                        // =================================================

                        if (famInst.Symbol == null)
                        {
                            skipped++;
                            continue;
                        }


                        string symbolName =
                            famInst.Symbol.Name ?? "";


                        // Пропускаем ненужные семейства
                        if (symbolName == "ПВХ" ||
                            symbolName.Contains("Отлив"))
                        {
                            skipped++;
                            continue;
                        }


                        // =================================================
                        // 2. ПОЛУЧАЕМ HOST
                        // =================================================

                        Wall hostWall =
                            famInst.Host as Wall;


                        // Не обычная стеновая дверь/окно
                        if (hostWall == null)
                        {
                            skipped++;
                            continue;
                        }


                        // =================================================
                        // 3. ПРОВЕРЯЕМ ТИП СТЕНЫ
                        // =================================================

                        WallType hostWallType =
                            hostWall.WallType;


                        if (hostWallType == null)
                        {
                            skipped++;
                            continue;
                        }


                        if (hostWallType.Function !=
                            WallFunction.Exterior)
                        {
                            skipped++;
                            continue;
                        }


                        // =================================================
                        // 4. ПАРАМЕТР "ПОМЕЩЕНИЕ ИМЯ"
                        // =================================================

                        Parameter roomNameParam =
                            famInst.LookupParameter(
                                "Помещение Имя");


                        if (roomNameParam == null)
                        {
                            skipped++;
                            continue;
                        }


                        if (roomNameParam.IsReadOnly)
                        {
                            skipped++;
                            continue;
                        }


                        if (roomNameParam.StorageType !=
                            StorageType.String)
                        {
                            skipped++;
                            continue;
                        }


                        // =================================================
                        // 5. ОРИЕНТАЦИЯ
                        // =================================================

                        Parameter orientationParam =
                            famInst.LookupParameter(
                                "INP_Ориентация по сторонам света");


                        // Если параметра на двери/окне нет —
                        // просто не заполняем его.
                        //
                        // Само помещение при этом всё равно
                        // может быть заполнено.


                        // =================================================
                        // 6. ПОЛУЧАЕМ ФАЗУ
                        // =================================================

                        Parameter phaseParam =
                            famInst.get_Parameter(
                                BuiltInParameter.PHASE_CREATED);


                        if (phaseParam == null)
                        {
                            skipped++;
                            continue;
                        }


                        ElementId phaseId =
                            phaseParam.AsElementId();


                        if (phaseId == null ||
                            phaseId == ElementId.InvalidElementId)
                        {
                            skipped++;
                            continue;
                        }


                        Phase phase =
                            doc.GetElement(phaseId) as Phase;


                        if (phase == null)
                        {
                            skipped++;
                            continue;
                        }


                        // =================================================
                        // 7. ПОЛУЧАЕМ ПОМЕЩЕНИЯ
                        // =================================================

                        Room fromRoom =
                            famInst.get_FromRoom(phase);

                        Room toRoom =
                            famInst.get_ToRoom(phase);


                        Room room = null;


                        if (fromRoom != null)
                        {
                            room = fromRoom;
                        }
                        else if (toRoom != null)
                        {
                            room = toRoom;
                        }


                        if (room == null)
                        {
                            skipped++;
                            continue;
                        }


                        // =================================================
                        // 8. ЗАПОЛНЯЕМ "ПОМЕЩЕНИЕ ИМЯ"
                        // =================================================

                        string roomNumber =
                            room.get_Parameter(
                                BuiltInParameter.ROOM_NUMBER)?
                                .AsString() ?? "";


                        string roomName =
                            room.get_Parameter(
                                BuiltInParameter.ROOM_NAME)?
                                .AsString() ?? "";


                        string roomValue;


                        if (!string.IsNullOrWhiteSpace(roomNumber) &&
                            !string.IsNullOrWhiteSpace(roomName))
                        {
                            roomValue =
                                $"{roomNumber} {roomName}";
                        }
                        else if (!string.IsNullOrWhiteSpace(roomNumber))
                        {
                            roomValue =
                                roomNumber;
                        }
                        else
                        {
                            roomValue =
                                roomName;
                        }


                        roomNameParam.Set(roomValue);


                        // =================================================
                        // 9. ОРИЕНТАЦИЯ СТЕНЫ
                        // =================================================

                        if (orientationParam != null &&
                            !orientationParam.IsReadOnly &&
                            orientationParam.StorageType ==
                                StorageType.String)
                        {
                            Parameter wallOrientationParam =
                                hostWall.LookupParameter(
                                    "INP_Ориентация по сторонам света");


                            if (wallOrientationParam != null &&
                                wallOrientationParam.StorageType ==
                                    StorageType.String)
                            {
                                string wallOrientation =
                                    wallOrientationParam.AsString();


                                if (!string.IsNullOrWhiteSpace(
                                        wallOrientation))
                                {
                                    orientationParam.Set(
                                        wallOrientation);
                                }
                            }
                        }


                        processed++;
                    }
                    catch (Exception ex)
                    {
                        // Не прекращаем обработку всего проекта
                        // из-за одного проблемного элемента.

                        skipped++;

                        System.Diagnostics.Debug.WriteLine(
                            $"Ошибка элемента {famInst.Id}: " +
                            ex);
                    }
                }


                t.Commit();
            }


            // =====================================================
            // РЕЗУЛЬТАТ
            // =====================================================

            TaskDialog.Show(
                "Готово",
                $"Обработано: {processed}\n" +
                $"Пропущено: {skipped}");


            return Result.Succeeded;
        }
    }
}