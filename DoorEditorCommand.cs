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

                    // ===== ПРАВИЛЬНЫЙ ПОРЯДОК =====
                    // 1. Сначала настраиваем свойства вида (скрытие категорий, детализация, реализм)
                    SetupViewVisibility(sectionView, doc);

                    // 2. Потом скрываем связи
                    HideRevitLinks(sectionView, doc);

                    // 3. И ТОЛЬКО ПОТОМ скрываем все элементы, кроме двери и её зависимых элементов
                    HideAllExceptDoor(sectionView, doc, door);

                    // ОТКЛЮЧАЕМ CROP VIEW
                    sectionView.CropBoxActive = false;
                    sectionView.CropBoxVisible = false;

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

        private void HideRevitLinks(View view, Document doc)
        {
            try
            {
                Category linkCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_RvtLinks);

                if (linkCategory != null)
                {
                    try
                    {
                        view.SetCategoryHidden(linkCategory.Id, true);
                        System.Diagnostics.Debug.WriteLine("Связи скрыты через категорию");
                    }
                    catch
                    {
                        FilteredElementCollector linkCollector = new FilteredElementCollector(doc, view.Id)
                            .OfClass(typeof(RevitLinkInstance));

                        List<ElementId> linkIds = linkCollector.Select(link => link.Id).ToList();

                        if (linkIds.Count > 0)
                        {
                            view.HideElements(linkIds);
                            System.Diagnostics.Debug.WriteLine($"Скрыто {linkIds.Count} экземпляров связей");
                        }
                    }
                }

                doc.Regenerate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при скрытии связей: {ex.Message}");
            }
        }

        private void SetupViewVisibility(ViewSection view, Document doc)
        {
            try
            {
                // НИКАКИХ ШАБЛОНОВ - просто настраиваем вид вручную
                System.Diagnostics.Debug.WriteLine("Настраиваем вид напрямую");

                // 1. Скрываем все категории модели кроме DOORS
                Categories categories = doc.Settings.Categories;
                int hiddenCount = 0;

                foreach (Category cat in categories)
                {
                    // Пропускаем категорию дверей
                    if (cat.Id.IntegerValue == (int)BuiltInCategory.OST_Doors)
                        continue;

                    // Скрываем категории модели
                    if (cat.CategoryType == CategoryType.Model)
                    {
                        try
                        {
                            if (view.CanCategoryBeHidden(cat.Id))
                            {
                                view.SetCategoryHidden(cat.Id, true);
                                hiddenCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Не удалось скрыть {cat.Name}: {ex.Message}");
                        }
                    }

                    // Скрываем аннотации
                    if (cat.CategoryType == CategoryType.Annotation)
                    {
                        try
                        {
                            if (view.CanCategoryBeHidden(cat.Id))
                            {
                                view.SetCategoryHidden(cat.Id, true);
                            }
                        }
                        catch { }
                    }
                }

                // 2. Убираем применение любого шаблона (если вдруг был)
                if (view.ViewTemplateId != ElementId.InvalidElementId)
                {
                    view.ViewTemplateId = ElementId.InvalidElementId;
                }

                // 3. Высокая детализация
                view.DetailLevel = ViewDetailLevel.Fine;

                // 4. Flat Colors (единообразный цвет)
                try
                {
                    view.DisplayStyle = DisplayStyle.FlatColors;
                    System.Diagnostics.Debug.WriteLine("Установлен режим Flat Colors");
                }
                catch
                {
                    // Если Flat Colors не поддерживается, пробуем Shading
                    try
                    {
                        view.DisplayStyle = DisplayStyle.Shading;
                        System.Diagnostics.Debug.WriteLine("Flat Colors не поддерживается, установлен Shading");
                    }
                    catch { }
                }

                doc.Regenerate();
                System.Diagnostics.Debug.WriteLine($"Вид настроен. Скрыто категорий модели: {hiddenCount}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка настройки вида: {ex.Message}");
            }
        }

        private ViewSection CreateSectionView(Document doc, Element door, BoundingBoxXYZ bb, Wall hostWall)
        {
            try
            {
                ViewFamilyType sectionFamilyType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Section);

                if (sectionFamilyType == null)
                {
                    System.Diagnostics.Debug.WriteLine("Ошибка: Не найден ViewFamilyType для разреза");
                    return null;
                }

                XYZ center = (bb.Min + bb.Max) / 2;
                XYZ viewDirection;

                // ===== ОПРЕДЕЛЯЕМ НАПРАВЛЕНИЕ ВЗГЛЯДА ПО ДВЕРИ =====
                if (door is FamilyInstance doorInstance)
                {
                    // Смотрим на лицевую сторону двери
                    viewDirection = -doorInstance.FacingOrientation.Normalize();
                    System.Diagnostics.Debug.WriteLine($"FacingOrientation: {doorInstance.FacingOrientation}");
                    System.Diagnostics.Debug.WriteLine($"viewDirection: {viewDirection}");
                }
                else
                {
                    viewDirection = new XYZ(0, -1, 0);
                }

                // ===== СТРОИМ ОРТОНОРМИРОВАННЫЙ БАЗИС =====
                XYZ up = XYZ.BasisZ;

                if (Math.Abs(viewDirection.DotProduct(up)) > 0.99)
                {
                    up = XYZ.BasisX;
                    System.Diagnostics.Debug.WriteLine("viewDirection почти вертикальный, используем BasisX как up");
                }

                XYZ right = up.CrossProduct(viewDirection).Normalize();
                up = viewDirection.CrossProduct(right).Normalize();

                Transform transform = Transform.Identity;
                transform.BasisX = right;
                transform.BasisY = up;
                transform.BasisZ = viewDirection;
                transform.Origin = center;

                BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
                sectionBox.Transform = transform;

                double width = (bb.Max.X - bb.Min.X) + 2.0;
                double height = (bb.Max.Z - bb.Min.Z) + 2.0;
                double depth = 2.0;

                sectionBox.Min = new XYZ(-width / 2, -depth / 2, -height / 2);
                sectionBox.Max = new XYZ(width / 2, depth / 2, height / 2);

                ViewSection sectionView = ViewSection.CreateSection(doc, sectionFamilyType.Id, sectionBox);
                sectionView.Name = "Временный разрез двери";

                System.Diagnostics.Debug.WriteLine($"Разрез успешно создан, Id: {sectionView.Id}");
                return sectionView;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания разреза: {ex.Message}");
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