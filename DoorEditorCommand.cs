using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI.Selection;
using System.IO;
using System.Linq;
using System;
using System.Windows;
using System.Collections.Generic;

namespace PlugINPRO
{
    [Transaction(TransactionMode.Manual)]
    public class DoorEditorCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument uidoc = uiapp.ActiveUIDocument;
                Document doc = uidoc.Document;

                // 1. Выбор двери
                Reference pickedRef;
                try
                {
                    pickedRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        new DoorSelectionFilter(),
                        "Выберите дверь");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                Element door = doc.GetElement(pickedRef.ElementId);

                if (door == null)
                {
                    MessageBox.Show("Не удалось получить элемент двери");
                    return Result.Failed;
                }

                // Сохраняем текущий активный вид
                View previousView = uidoc.ActiveView;

                // 2. Создаем временный разрез и сохраняем в PNG
                string imagePath = Path.Combine(Path.GetTempPath(), "door_preview.png");

                // ВСЯ РАБОТА С ДОКУМЕНТОМ ВНУТРИ ТРАНЗАКЦИИ
                ElementId tempViewId = ElementId.InvalidElementId;

                using (Transaction tx = new Transaction(doc, "Временный разрез двери"))
                {
                    tx.Start();

                    // Получаем границы двери
                    BoundingBoxXYZ bb = door.get_BoundingBox(null);

                    if (bb == null)
                    {
                        MessageBox.Show("У элемента нет bounding box");
                        tx.RollBack();
                        return Result.Failed;
                    }

                    // Находим стену, на которой установлена дверь
                    Wall hostWall = GetHostWall(door);

                    // Создаем временный разрез
                    ViewSection sectionView = CreateSectionView(doc, door, bb, hostWall);

                    if (sectionView == null)
                    {
                        MessageBox.Show("Не удалось создать разрез");
                        tx.RollBack();
                        return Result.Failed;
                    }

                    // Сохраняем ID вида
                    tempViewId = sectionView.Id;

                    // Скрываем все, кроме двери и её зависимых элементов
                    HideAllExceptDoor(sectionView, doc, door);

                    // ОТКЛЮЧАЕМ CROP VIEW
                    sectionView.CropBoxActive = false;
                    sectionView.CropBoxVisible = false;

                    // Применяем шаблон вида
                    ApplyViewTemplate(sectionView, doc);

                    tx.Commit();
                }

                // ТРАНЗАКЦИЯ ЗАКРЫТА - ТЕПЕРЬ МОЖНО МЕНЯТЬ АКТИВНЫЙ ВИД

                // 3. Активируем временный вид для экспорта
                ViewSection exportView = doc.GetElement(tempViewId) as ViewSection;
                if (exportView != null)
                {
                    uidoc.ActiveView = exportView;
                }
                else
                {
                    MessageBox.Show("Не удалось активировать вид для экспорта");
                    return Result.Failed;
                }

                // 4. Экспортируем изображение с этого вида
                try
                {
                    ImageExportOptions options = new ImageExportOptions
                    {
                        FilePath = imagePath,
                        FitDirection = FitDirectionType.Horizontal,
                        HLRandWFViewsFileType = ImageFileType.PNG,
                        ImageResolution = ImageResolution.DPI_150,
                        ExportRange = ExportRange.CurrentView,
                        ZoomType = ZoomFitType.FitToPage,
                        ShadowViewsFileType = ImageFileType.PNG
                    };

                    doc.ExportImage(options);

                    // Даем время на запись файла
                    System.Threading.Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при экспорте: {ex.Message}");

                    // Возвращаем предыдущий вид и удаляем временный
                    if (previousView != null)
                    {
                        uidoc.ActiveView = previousView;
                    }

                    SafeDeleteView(doc, tempViewId);
                    return Result.Failed;
                }

                // 5. Проверяем, создался ли файл
                if (!File.Exists(imagePath))
                {
                    MessageBox.Show("Файл изображения не был создан");

                    // Возвращаем предыдущий вид и удаляем временный
                    if (previousView != null)
                    {
                        uidoc.ActiveView = previousView;
                    }

                    SafeDeleteView(doc, tempViewId);
                    return Result.Failed;
                }

                // 6. Открываем окно с изображением И ПАРАМЕТРАМИ
                DoorPreviewWindow window = new DoorPreviewWindow(imagePath, doc, door, uidoc, tempViewId, previousView);
                window.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
                return Result.Failed;
            }
        }

        private void SafeDeleteView(Document doc, ElementId viewId)
        {
            if (viewId == null || viewId == ElementId.InvalidElementId)
                return;

            try
            {
                Element view = doc.GetElement(viewId);
                if (view != null)
                {
                    using (Transaction tx = new Transaction(doc, "Удаление временного вида"))
                    {
                        tx.Start();
                        doc.Delete(viewId);
                        tx.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                // Игнорируем ошибки удаления
                System.Diagnostics.Debug.WriteLine($"Ошибка удаления вида: {ex.Message}");
            }
        }

        private Wall GetHostWall(Element door)
        {
            Parameter hostIdParam = door.get_Parameter(BuiltInParameter.HOST_ID_PARAM);
            if (hostIdParam != null && hostIdParam.HasValue)
            {
                ElementId hostId = hostIdParam.AsElementId();
                if (hostId != null && hostId != ElementId.InvalidElementId)
                {
                    Element host = door.Document.GetElement(hostId);
                    return host as Wall;
                }
            }
            return null;
        }

        private void HideAllExceptDoor(View view, Document doc, Element door)
        {
            try
            {
                view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);

                // Получаем зависимые элементы двери (shared-вложения, фурнитура и т.д.)
                ICollection<ElementId> dependentIds = door.GetDependentElements(null);

                // Создаем множество элементов, которые нужно ОСТАВИТЬ
                HashSet<ElementId> keepIds = new HashSet<ElementId>(dependentIds);
                keepIds.Add(door.Id);

                // Собираем ВСЕ элементы в документе
                FilteredElementCollector collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();

                List<ElementId> toHide = new List<ElementId>();

                foreach (Element elem in collector)
                {
                    if (!keepIds.Contains(elem.Id))
                    {
                        // В Revit 2022 нет специального метода проверки,
                        // просто добавляем в список - если элемент нельзя скрыть,
                        // Revit проигнорирует его при HideElements
                        toHide.Add(elem.Id);
                    }
                }

                // Скрываем элементы с обработкой возможных ошибок
                if (toHide.Count > 0)
                {
                    try
                    {
                        view.HideElements(toHide);
                    }
                    catch (Exception ex)
                    {
                        // Если не удалось скрыть все сразу, пробуем по одному
                        System.Diagnostics.Debug.WriteLine($"Ошибка при массовом скрытии: {ex.Message}");

                        foreach (ElementId id in toHide)
                        {
                            try
                            {
                                view.HideElements(new List<ElementId> { id });
                            }
                            catch
                            {
                                // Игнорируем ошибки для отдельных элементов
                            }
                        }
                    }
                }

                doc.Regenerate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при скрытии элементов: {ex.Message}");
            }
        }

        private void ApplyViewTemplate(ViewSection view, Document doc)
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                View viewTemplate = collector
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => v.IsTemplate && v.Name == "ADSK_АР_О_Ф_Цветной");

                if (viewTemplate != null)
                {
                    view.ViewTemplateId = viewTemplate.Id;
                }
            }
            catch (Exception ex)
            {
                // Продолжаем без шаблона
            }
        }

        private ViewSection CreateSectionView(Document doc, Element door, BoundingBoxXYZ bb, Wall hostWall)
        {
            try
            {
                ViewFamilyType sectionFamilyType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .First(vft => vft.ViewFamily == ViewFamily.Section);

                XYZ viewDirection;
                XYZ rightDirection;
                XYZ center = (bb.Min + bb.Max) / 2;

                if (hostWall != null)
                {
                    LocationCurve wallCurve = hostWall.Location as LocationCurve;
                    if (wallCurve != null)
                    {
                        XYZ wallDirection = (wallCurve.Curve.GetEndPoint(1) - wallCurve.Curve.GetEndPoint(0)).Normalize();
                        XYZ wallNormal = new XYZ(wallDirection.Y, -wallDirection.X, 0).Normalize();
                        viewDirection = wallNormal;
                        rightDirection = wallDirection;
                    }
                    else
                    {
                        viewDirection = new XYZ(0, -1, 0);
                        rightDirection = new XYZ(1, 0, 0);
                    }
                }
                else
                {
                    viewDirection = new XYZ(0, -1, 0);
                    rightDirection = new XYZ(1, 0, 0);
                }

                // Создаем transform, но не применяем его сразу
                Transform transform = Transform.Identity;
                transform.BasisX = rightDirection;
                transform.BasisY = new XYZ(0, 0, 1);
                transform.BasisZ = viewDirection;
                transform.Origin = center;

                // Создаем bounding box в локальных координатах
                BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();

                // Устанавливаем transform
                sectionBox.Transform = transform;

                // Размеры в локальных координатах (намного меньше!)
                double width = (bb.Max.X - bb.Min.X) + 0.5; // +0.5 фута запаса
                double height = (bb.Max.Z - bb.Min.Z) + 0.5; // +0.5 фута запаса
                double depth = 0.1; // Очень маленькая глубина (около 3 см)

                // Устанавливаем min и max в локальных координатах
                sectionBox.Min = new XYZ(-width / 2, -depth / 2, -height / 2);
                sectionBox.Max = new XYZ(width / 2, depth / 2, height / 2);

                ViewSection sectionView = ViewSection.CreateSection(doc, sectionFamilyType.Id, sectionBox);
                sectionView.Name = "Временный разрез двери";

                return sectionView;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка создания разреза: {ex.Message}");
                return null;
            }
        }
    }

    public class DoorSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Doors;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}