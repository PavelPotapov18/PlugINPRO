using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Linq;
using System.Collections.Generic;

namespace PlugINPRO
{
    [Transaction(TransactionMode.Manual)]
    public class IFCByCoordinatesCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            IFCWindow window = new IFCWindow();
            bool? dialogResult = window.ShowDialog();

            if (dialogResult != true)
                return Result.Cancelled;

            try
            {
                // 1. ПОЛУЧАЕМ КООРДИНАТЫ ИЗ ПОЛЕЙ ОКНА
                double x_mm = window.OffsetX;
                double y_mm = window.OffsetY;
                double z_mm = window.OffsetZ;


                // 2. ВЫБОР IFC
                Reference selectedRef = null;
                try
                {
                    selectedRef = uiDoc.Selection.PickObject(
                        ObjectType.Element,
                        "Выберите IFC связь");
                }
                catch
                {
                    return Result.Cancelled;
                }

                if (selectedRef == null) return Result.Cancelled;

                // 3. ПРОВЕРКА
                Element selectedElement = doc.GetElement(selectedRef);
                RevitLinkInstance link = selectedElement as RevitLinkInstance;

                if (link == null)
                {
                    TaskDialog.Show("Ошибка", "Выбранный элемент не является IFC связью");
                    return Result.Cancelled;
                }
                // 4. ПОДТВЕРЖДЕНИЕ
                TaskDialogResult confirm = TaskDialog.Show(
                    "Подтверждение",
                    $"Переместить '{link.Name}'?\n" +
                    $"Координаты: X={x_mm} мм, Y={y_mm} мм, Z={z_mm} мм",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                if (confirm != TaskDialogResult.Yes) return Result.Cancelled;
                // 5. ПЕРЕМЕЩЕНИЕ
                using (Transaction trans = new Transaction(doc, "IFC по координатам"))
                {
                    trans.Start();

                    bool success = MoveIFC(doc, link, x_mm, y_mm, z_mm);

                    trans.Commit();

                    if (success)
                    {
                        TaskDialog.Show("Успех",
                            $"IFC перемещена на:\n" +
                            $"X={x_mm} мм, Y={y_mm} мм, Z={z_mm} мм");
                    }
                    else
                    {
                        TaskDialog.Show("Ошибка", "Не удалось переместить IFC");
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", ex.Message);
                return Result.Failed;
            }
        }


            //try
            //{
            //    // 1. ПОЛУЧАЕМ КООРДИНАТЫ ИЗ ПОЛЕЙ НА ЛЕНТЕ
            //    double x_mm = 0, y_mm = 0, z_mm = 0;

            //    if (!GetCoordinatesFromRibbon(out x_mm, out y_mm, out z_mm))
            //    {
            //        TaskDialog.Show("Ошибка",
            //            "Неверный формат координат.\n" +
            //            "Введите числа в поля X, Y, Z на панели ГЕНПЛАН.");
            //        return Result.Cancelled;
            //    }

            //    // 2. ВЫБОР IFC
            //    Reference selectedRef = null;
            //    try
            //    {
            //        selectedRef = uiDoc.Selection.PickObject(
            //            ObjectType.Element,
            //            "Выберите IFC связь");
            //    }
            //    catch
            //    {
            //        return Result.Cancelled;
            //    }

            //    if (selectedRef == null) return Result.Cancelled;

            //    // 3. ПРОВЕРКА
            //    Element selectedElement = doc.GetElement(selectedRef);
            //    RevitLinkInstance link = selectedElement as RevitLinkInstance;

            //    if (link == null)
            //    {
            //        TaskDialog.Show("Ошибка", "Выбранный элемент не является IFC связью");
            //        return Result.Cancelled;
            //    }

            //    // 4. ПОДТВЕРЖДЕНИЕ
            //    TaskDialogResult confirm = TaskDialog.Show(
            //        "Подтверждение",
            //        $"Переместить '{link.Name}'?\n" +
            //        $"Координаты: X={x_mm} мм, Y={y_mm} мм, Z={z_mm} мм",
            //        TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

            //    if (confirm != TaskDialogResult.Yes) return Result.Cancelled;

            //    // 5. ПЕРЕМЕЩЕНИЕ
            //    using (Transaction trans = new Transaction(doc, "IFC по координатам"))
            //    {
            //        trans.Start();

            //        bool success = MoveIFC(doc, link, x_mm, y_mm, z_mm);

            //        trans.Commit();

            //        if (success)
            //        {
            //            TaskDialog.Show("Успех",
            //                $"IFC перемещена на:\n" +
            //                $"X={x_mm} мм, Y={y_mm} мм, Z={z_mm} мм");
            //        }
            //        else
            //        {
            //            TaskDialog.Show("Ошибка", "Не удалось переместить IFC");
            //        }
            //    }

            //    return Result.Succeeded;
            //}
            //catch (Exception ex)
            //{
            //    TaskDialog.Show("Ошибка", ex.Message);
            //    return Result.Failed;
            //}
            //}

            //private bool GetCoordinatesFromRibbon(out double x, out double y, out double z)
            //{
            //    x = y = z = 0;

            //    try
            //    {
            //        // Получаем значения из MainApplication
            //        if (App.txtX != null && App.txtY != null && App.txtZ != null)
            //        {
            //            // Value возвращает object, нужно привести к string
            //            string strX = App.txtX.Value as string;
            //            string strY = App.txtY.Value as string;
            //            string strZ = App.txtZ.Value as string;

            //            // Если null, используем пустую строку
            //            strX = strX ?? "";
            //            strY = strY ?? "";
            //            strZ = strZ ?? "";

            //            if (double.TryParse(strX, out x) &&
            //                double.TryParse(strY, out y) &&
            //                double.TryParse(strZ, out z))
            //            {
            //                return true;
            //            }
            //        }

            //        return false;
            //    }
            //    catch
            //    {
            //        return false;
            //    }
            //}

        private bool MoveIFC(Document doc, RevitLinkInstance link, double x_mm, double y_mm, double z_mm)
        {
            try
            {
                const double MM_TO_FT = 0.00328084;
                double x_abs = x_mm * MM_TO_FT;
                double y_abs = y_mm * MM_TO_FT;
                double z_abs = z_mm * MM_TO_FT;

                ProjectLocation projectLocation = doc.ActiveProjectLocation;
                Transform sharedToInternal = projectLocation.GetTransform();

                XYZ sharedPoint = new XYZ(x_abs, y_abs, z_abs);
                XYZ internalPoint = sharedToInternal.OfPoint(sharedPoint);

                Transform linkTransform = link.GetTotalTransform();
                XYZ origin = linkTransform.Origin;
                XYZ moveVector = internalPoint - origin;

                Location location = link.Location;
                if (location != null)
                {
                    location.Move(moveVector);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка перемещения", ex.Message);
                return false;
            }
        }
    }
    
    [Transaction(TransactionMode.Manual)]
    public class ResetCoordinatesCommand : IExternalCommand
    {
        public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            // 2. ВЫБОР IFC
            Reference selectedRef;
            try
            {
                selectedRef = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    "Выберите IFC связь");
            }
            catch
            {
                return Result.Cancelled;
            }

            if (selectedRef == null) return Result.Cancelled;

            // 3. ПРОВЕРКА
            Element selectedElement = doc.GetElement(selectedRef);
            RevitLinkInstance link = selectedElement as RevitLinkInstance;
            if (link == null)
            {
                TaskDialog.Show("Ошибка", "Выбранный элемент не является IFC связью");
                return Result.Cancelled;
            }
            
            // 4. ПОДТВЕРЖДЕНИЕ
            TaskDialogResult confirm = TaskDialog.Show(
                "Подтверждение",
                $"Переместить '{link.Name}'\n" +
                $"в начало координат?",
                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

            if (confirm != TaskDialogResult.Yes) return Result.Cancelled;

            Transform linkTransform = link.GetTransform();
            XYZ currentOrigin = linkTransform.Origin;
            Location linkLocation = link.Location;

            // Вектор перемещения в (0,0,0)
            XYZ moveVector = new XYZ(
                -currentOrigin.X,
                -currentOrigin.Y,
                -currentOrigin.Z
            );

            using (Transaction t = new Transaction(doc, "Сброс координат IFC связи"))
            {
                t.Start();
                bool move = linkLocation.Move(moveVector);
            
                if (move)
                {
                    TaskDialog.Show("Успех",
                        $"IFC успешно перемещена\n" +
                        $"в начало координат");
                }
                else
                {
                    TaskDialog.Show("Ошибка", "Не удалось переместить IFC");
                }
                t.Commit();
            }
            return Result.Succeeded;
        }
    }
}