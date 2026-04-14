using System.Windows;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.IO;
using System.Windows.Media;
using System.Windows.Shapes;

// Алиас для разрешения конфликта имён с Visibility
using WpfVisibility = System.Windows.Visibility;

// Алиасы
using Path = System.Windows.Shapes.Path;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfBorder = System.Windows.Controls.Border;
using WpfScrollViewer = System.Windows.Controls.ScrollViewer;
using WpfStackPanel = System.Windows.Controls.StackPanel;

namespace PlugINPRO.UI.Windows.AR
{
    // Enum для типов параметров
    public enum ParamKind
    {
        String,
        Double,
        Integer,
        Material,
        FamilyType,
        ElementId
    }

    // Класс для информации о параметре
    public class ParameterInfo
    {
        public string Name { get; set; }
        public Parameter Parameter { get; set; }
        public ParamKind Kind { get; set; }
        public string DisplayValue { get; set; }
    }

    // Класс для элементов материала
    public class MaterialItem
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }
    }

    // Класс для элементов типа семейства
    public class FamilyTypeItem
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }
        public string FamilyName { get; set; }

        // Свойство для отображения в комбобоксе
        public string DisplayName
        {
            get
            {
                if (Id == ElementId.InvalidElementId)
                    return Name;
                if (!string.IsNullOrEmpty(FamilyName))
                    return $"{Name} ({FamilyName})";
                return Name;
            }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public partial class DoorPreviewWindow : Window
    {
        private Document _doc;
        private Element _door;
        private UIDocument _uidoc;
        private string _currentImagePath;
        private FamilySymbol _currentType;
        private FamilySymbol _selectedType;
        private bool _isUpdating = false;
        private ElementId _tempViewId;
        private View _previousView;

        private double _originalRoughWidth;
        private double _originalRoughHeight;
        private double _newRoughWidth;
        private double _newRoughHeight;

        private Dictionary<string, Parameter> _parameters = new Dictionary<string, Parameter>();

        private const string ROUGH_WIDTH_KEY = "Rough Width";
        private const string ROUGH_HEIGHT_KEY = "Rough Height";

        private readonly BuiltInParameter _roughWidthParam = BuiltInParameter.FAMILY_ROUGH_WIDTH_PARAM;
        private readonly BuiltInParameter _roughHeightParam = BuiltInParameter.FAMILY_ROUGH_HEIGHT_PARAM;

        // Системные параметры двери
        private readonly BuiltInParameter _doorWidthParam = BuiltInParameter.DOOR_WIDTH;
        private readonly BuiltInParameter _doorHeightParam = BuiltInParameter.DOOR_HEIGHT;

        // Параметры по группам
        private List<ParameterInfo> _materialParameters = new List<ParameterInfo>();
        private List<ParameterInfo> _constructionParameters = new List<ParameterInfo>();
        private List<ParameterInfo> _visibilityParameters = new List<ParameterInfo>();
        private List<ParameterInfo> _otherParameters = new List<ParameterInfo>();

        private Dictionary<string, object> _tempMaterialValues = new Dictionary<string, object>();
        private Dictionary<string, object> _tempConstructionValues = new Dictionary<string, object>();
        private Dictionary<string, object> _tempVisibilityValues = new Dictionary<string, object>();
        private Dictionary<string, object> _tempOtherValues = new Dictionary<string, object>();

        private List<Material> _allMaterials = new List<Material>();

        // Кэш для типов вложенного семейства АС-Полотно
        private List<FamilyTypeItem> _nestedFamilyTypes = new List<FamilyTypeItem>();
        private bool _nestedTypesLoaded = false;

        // Словарь для хранения состояния групп (открыта/закрыта)
        private Dictionary<string, bool> _groupStates = new Dictionary<string, bool>
        {
            { "Materials", false },
            { "Construction", false },
            { "Visibility", false },
            { "Other", false }
        };

        // Словарь для хранения ссылок на элементы групп
        private Dictionary<string, (Button Header, Border Content)> _groupControls;

        public DoorPreviewWindow(string imagePath, Document doc, Element door, UIDocument uidoc, ElementId tempViewId, View previousView)
        {
            InitializeComponent();

            _currentImagePath = imagePath;
            _doc = doc;
            _door = door;
            _uidoc = uidoc;
            _tempViewId = tempViewId;
            _previousView = previousView;

            // Инициализация групп аккордеона
            InitializeGroups();

            ElementId typeId = _door.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                _currentType = _doc.GetElement(typeId) as FamilySymbol;
                _selectedType = _currentType;
            }

            // Загружаем типы вложенного семейства АС-Полотно
            LoadNestedFamilyTypes();

            LoadImage();
            LoadDoorParameters();
            LoadDoorSystemSizeParameters();
            LoadMaterials();
            LoadFamilyTypes();

            // Загружаем параметры
            LoadMaterialParameters();
            LoadConstructionParameters();
            LoadVisibilityParameters();
            LoadOtherParameters();

            UpdateCurrentTypeDisplay();
            DrawDoorDiagram();

            UpdateAndRefreshButton.IsEnabled = false;
        }

        // Вспомогательный метод для создания кисти из RGB
        private SolidColorBrush ColorFromRgb(byte r, byte g, byte b)
        {
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        }

        // ========================= ИНИЦИАЛИЗАЦИЯ ГРУПП АККОРДЕОНА =========================
        private void InitializeGroups()
        {
            _groupControls = new Dictionary<string, (Button Header, Border Content)>
            {
                { "Materials", (MaterialsHeaderButton, MaterialsContent) },
                { "Construction", (ConstructionHeaderButton, ConstructionContent) },
                { "Visibility", (VisibilityHeaderButton, VisibilityContent) },
                { "Other", (OtherHeaderButton, OtherContent) }
            };

            // Восстанавливаем состояние групп
            foreach (var group in _groupControls)
            {
                UpdateGroupArrow(group.Value.Header, _groupStates[group.Key]);
                group.Value.Content.Visibility = _groupStates[group.Key] ?
                    WpfVisibility.Visible : WpfVisibility.Collapsed;
            }
        }

        // Обработчик клика по заголовку группы
        private void ToggleGroup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Button headerButton = sender as Button;
                if (headerButton == null) return;

                string groupName = headerButton.Tag as string;
                if (string.IsNullOrEmpty(groupName) || !_groupControls.ContainsKey(groupName)) return;

                // Переключаем состояние
                bool newState = !_groupStates[groupName];
                _groupStates[groupName] = newState;

                var group = _groupControls[groupName];

                // Обновляем видимость контента
                if (group.Content != null)
                {
                    group.Content.Visibility = newState ? WpfVisibility.Visible : WpfVisibility.Collapsed;
                }

                // Обновляем стрелку
                UpdateGroupArrow(group.Header, newState);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка в ToggleGroup_Click: {ex.Message}");
            }
        }

        // Обновление стрелки в заголовке группы
        private void UpdateGroupArrow(Button headerButton, bool isExpanded)
        {
            if (headerButton == null) return;

            try
            {
                // Ищем Path с стрелкой в визуальном дереве
                var arrowPath = FindVisualChild<Path>(headerButton);
                if (arrowPath != null)
                {
                    var rotateTransform = arrowPath.RenderTransform as RotateTransform;
                    if (rotateTransform != null)
                    {
                        rotateTransform.Angle = isExpanded ? 180 : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка в UpdateGroupArrow: {ex.Message}");
            }
        }

        // Вспомогательный метод для поиска дочернего элемента в визуальном дереве
        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T)
                    return (T)child;

                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                    return descendant;
            }
            return null;
        }

        // ========================= ЗАГРУЗКА ТИПОВ ВЛОЖЕННОГО СЕМЕЙСТВА =========================
        private void LoadNestedFamilyTypes()
        {
            try
            {
                _nestedFamilyTypes.Clear();

                // Собираем все семейства с именем содержащим "АС-Полотно"
                FilteredElementCollector collector = new FilteredElementCollector(_doc);
                var families = collector
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Where(f => f != null && f.Name.Contains("АС-Полотно"))
                    .ToList();

                foreach (var family in families)
                {
                    // Получаем все типы этого семейства
                    FilteredElementCollector typeCollector = new FilteredElementCollector(_doc);
                    var symbols = typeCollector
                        .OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>()
                        .Where(fs => fs.Family != null && fs.Family.Id == family.Id)
                        .ToList();

                    foreach (var symbol in symbols)
                    {
                        _nestedFamilyTypes.Add(new FamilyTypeItem
                        {
                            Id = symbol.Id,
                            Name = symbol.Name,
                            FamilyName = family.Name
                        });
                    }
                }

                // Сортируем по имени
                _nestedFamilyTypes = _nestedFamilyTypes.OrderBy(x => x.Name).ToList();

                _nestedTypesLoaded = true;

                if (_nestedFamilyTypes.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("Внимание: не найдено семейство АС-Полотно");
                }

                // Обновляем комбобоксы после загрузки типов
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    RefreshAllFamilyTypeCombos();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки типов вложенного семейства: {ex.Message}");
                _nestedTypesLoaded = false;
            }
        }

        // ========================= ОБНОВЛЕНИЕ ВСЕХ КОМБОБОКСОВ FAMILYTYPE =========================
        private void RefreshAllFamilyTypeCombos()
        {
            try
            {
                if (ConstructionStackPanel == null) return;

                // Ищем все комбобоксы в ConstructionStackPanel
                foreach (var child in ConstructionStackPanel.Children)
                {
                    if (child is WpfBorder border && border.Child is WpfGrid grid)
                    {
                        foreach (var gridChild in grid.Children)
                        {
                            if (gridChild is WpfComboBox combo)
                            {
                                var tag = combo.Tag as Tuple<ParameterInfo, Dictionary<string, object>>;
                                if (tag != null && tag.Item1.Kind == ParamKind.FamilyType)
                                {
                                    var currentSelection = combo.SelectedItem as FamilyTypeItem;

                                    // Создаем новый список с пустым элементом
                                    List<FamilyTypeItem> items = new List<FamilyTypeItem>
                                    {
                                        new FamilyTypeItem { Id = ElementId.InvalidElementId, Name = "<Не задано>", FamilyName = "" }
                                    };

                                    if (_nestedFamilyTypes.Count > 0)
                                    {
                                        items.AddRange(_nestedFamilyTypes);
                                    }

                                    combo.ItemsSource = null;
                                    combo.ItemsSource = items;

                                    // Восстанавливаем выделение
                                    if (currentSelection != null)
                                    {
                                        var matchingItem = items.FirstOrDefault(x => x.Id == currentSelection.Id);
                                        if (matchingItem != null)
                                            combo.SelectedItem = matchingItem;
                                        else
                                            combo.SelectedItem = items[0];
                                    }
                                    else
                                    {
                                        combo.SelectedItem = items[0];
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка обновления комбобоксов: {ex.Message}");
            }
        }

        // ========================= ЗАГРУЗКА ИЗОБРАЖЕНИЯ =========================
        private void LoadImage()
        {
            try
            {
                if (!File.Exists(_currentImagePath))
                {
                    StatusText.Text = "Файл изображения не найден";
                    return;
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(_currentImagePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();

                DoorImage.Source = bitmap;
                StatusText.Text = "Изображение загружено";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка загрузки изображения: {ex.Message}";
            }
        }

        // ========================= ЗАГРУЗКА ПАРАМЕТРОВ ДВЕРИ =========================
        private void LoadDoorParameters()
        {
            try
            {
                if (_currentType == null)
                {
                    StatusText.Text = "Не удалось определить тип двери";
                    return;
                }

                LoadTypeParameter(ROUGH_WIDTH_KEY, WidthTextBox, _roughWidthParam);
                LoadTypeParameter(ROUGH_HEIGHT_KEY, HeightTextBox, _roughHeightParam);

                StatusText.Text = $"Параметры загружены: Примерная ширина {_originalRoughWidth:F0} x Примерная высота {_originalRoughHeight:F0} мм";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка загрузки параметров: {ex.Message}";
            }
        }

        private void LoadTypeParameter(string key, WpfTextBox textBox, BuiltInParameter builtInParam)
        {
            try
            {
                var param = _currentType.get_Parameter(builtInParam);
                if (param != null && param.HasValue)
                {
                    _parameters[key] = param;

                    if (param.StorageType == StorageType.Double)
                    {
                        double valueInFeet = param.AsDouble();
                        double valueInMm = valueInFeet * 304.8;
                        textBox.Text = Math.Round(valueInMm, 0).ToString();

                        if (key == ROUGH_WIDTH_KEY)
                        {
                            _originalRoughWidth = valueInMm;
                            _newRoughWidth = valueInMm;
                        }
                        else if (key == ROUGH_HEIGHT_KEY)
                        {
                            _originalRoughHeight = valueInMm;
                            _newRoughHeight = valueInMm;
                        }
                    }
                }
                else if (param != null)
                {
                    _parameters[key] = param;
                    textBox.Text = "";
                }
            }
            catch (Exception ex)
            {
                textBox.Text = "";
            }
        }

        // ========================= ЗАГРУЗКА СИСТЕМНЫХ РАЗМЕРОВ ДВЕРИ =========================
        private void LoadDoorSystemSizeParameters()
        {
            try
            {
                if (_door == null)
                {
                    DoorWidthTextBox.Text = "-";
                    DoorHeightTextBox.Text = "-";
                    return;
                }

                // ВАЖНО: Используем _door (экземпляр), а не _currentType (тип)
                // Загружаем системную ширину (DOOR_WIDTH) - ПАРАМЕТР ЭКЗЕМПЛЯРА
                Parameter doorWidthParam = _door.get_Parameter(_doorWidthParam);
                if (doorWidthParam != null && doorWidthParam.HasValue)
                {
                    double widthInFeet = doorWidthParam.AsDouble();
                    double widthInMm = widthInFeet * 304.8;
                    DoorWidthTextBox.Text = $"{Math.Round(widthInMm, 0)} мм";

                    DoorWidthTextBox.ToolTip = $"Точное значение: {widthInMm:F2} мм\n(в футах: {widthInFeet:F4})";
                }
                else
                {
                    DoorWidthTextBox.Text = "—";
                    DoorWidthTextBox.ToolTip = "Системный параметр ширины не найден или не имеет значения";
                }

                // Загружаем системную высоту (DOOR_HEIGHT) - ПАРАМЕТР ЭКЗЕМПЛЯРА
                Parameter doorHeightParam = _door.get_Parameter(_doorHeightParam);
                if (doorHeightParam != null && doorHeightParam.HasValue)
                {
                    double heightInFeet = doorHeightParam.AsDouble();
                    double heightInMm = heightInFeet * 304.8;
                    DoorHeightTextBox.Text = $"{Math.Round(heightInMm, 0)} мм";

                    DoorHeightTextBox.ToolTip = $"Точное значение: {heightInMm:F2} мм\n(в футах: {heightInFeet:F4})";
                }
                else
                {
                    DoorHeightTextBox.Text = "—";
                    DoorHeightTextBox.ToolTip = "Системный параметр высоты не найден или не имеет значения";
                }

                HighlightIfMismatch();
            }
            catch (Exception ex)
            {
                DoorWidthTextBox.Text = "Ошибка";
                DoorHeightTextBox.Text = "Ошибка";
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки системных размеров: {ex.Message}");
            }
        }

        // ========================= ПРОВЕРКА НА СООТВЕТСТВИЕ РАЗМЕРОВ =========================
        private void HighlightIfMismatch()
        {
            try
            {
                // Получаем системные размеры из экземпляра для сравнения
                double systemWidth = 0;
                double systemHeight = 0;

                Parameter doorWidthParam = _door?.get_Parameter(_doorWidthParam);
                Parameter doorHeightParam = _door?.get_Parameter(_doorHeightParam);

                if (doorWidthParam != null && doorWidthParam.HasValue)
                    systemWidth = doorWidthParam.AsDouble() * 304.8;

                if (doorHeightParam != null && doorHeightParam.HasValue)
                    systemHeight = doorHeightParam.AsDouble() * 304.8;

                // Сравниваем с примерными размерами
                if (systemWidth > 0 && double.TryParse(WidthTextBox.Text, out double roughWidth))
                {
                    if (Math.Abs(roughWidth - systemWidth) > 1.0) // Расхождение больше 1 мм
                    {
                        DoorWidthTextBox.Background = new SolidColorBrush(Colors.LightYellow);
                        DoorWidthTextBox.ToolTip += $"\n\n⚠️ Внимание! Расхождение с примерной шириной: {Math.Abs(roughWidth - systemWidth):F1} мм";
                    }
                    else
                    {
                        DoorWidthTextBox.Background = ColorFromRgb(240, 240, 240);
                    }
                }

                if (systemHeight > 0 && double.TryParse(HeightTextBox.Text, out double roughHeight))
                {
                    if (Math.Abs(roughHeight - systemHeight) > 1.0) // Расхождение больше 1 мм
                    {
                        DoorHeightTextBox.Background = new SolidColorBrush(Colors.LightYellow);
                        DoorHeightTextBox.ToolTip += $"\n\n⚠️ Внимание! Расхождение с примерной высотой: {Math.Abs(roughHeight - systemHeight):F1} мм";
                    }
                    else
                    {
                        DoorHeightTextBox.Background = ColorFromRgb(240, 240, 240);
                    }
                }
            }
            catch { }
        }

        // ========================= ЗАГРУЗКА ТИПОВ ДВЕРИ =========================
        private void LoadFamilyTypes()
        {
            try
            {
                if (_currentType == null) return;

                Family family = _currentType.Family;
                FilteredElementCollector collector = new FilteredElementCollector(_doc);
                List<FamilySymbol> types = collector
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.Family != null && fs.Family.Id == family.Id)
                    .OrderBy(fs => fs.Name)
                    .ToList();

                TypeComboBox.ItemsSource = types;
                TypeComboBox.DisplayMemberPath = "Name";
                TypeComboBox.SelectedItem = _currentType;
                _selectedType = _currentType;

                UpdateCurrentTypeDisplay();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка загрузки типов: {ex.Message}";
            }
        }

        private void UpdateCurrentTypeDisplay()
        {
            if (_currentType != null)
            {
                CurrentTypeText.Text = _currentType.Name;

                Family family = _currentType.Family;
                if (family != null)
                {
                    CurrentTypeFamilyText.Text = $"Семейство: {family.Name}";
                }
                else
                {
                    CurrentTypeFamilyText.Text = "Семейство: не определено";
                }
            }
            else
            {
                CurrentTypeText.Text = "Не выбран";
                CurrentTypeFamilyText.Text = "Семейство: не определено";
            }
        }

        private void TypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdating) return;

            FamilySymbol selectedType = TypeComboBox.SelectedItem as FamilySymbol;
            if (selectedType != null && selectedType.Id != _selectedType?.Id)
            {
                _selectedType = selectedType;
                StatusText.Text = $"Выбран тип: {_selectedType.Name}";

                UpdateParametersFromType(selectedType);
                LoadDoorSystemSizeParameters(); // Обновляем системные размеры
                UpdateCurrentTypeDisplay();

                // Перезагружаем все параметры для нового типа
                LoadMaterialParameters();
                LoadConstructionParameters();
                LoadVisibilityParameters();
                LoadOtherParameters();

                UpdateAndRefreshButton.IsEnabled = true;
            }
        }

        private void UpdateParametersFromType(FamilySymbol type)
        {
            try
            {
                Parameter roughWidthParam = type.get_Parameter(_roughWidthParam);
                if (roughWidthParam != null && roughWidthParam.HasValue)
                {
                    _newRoughWidth = roughWidthParam.AsDouble() * 304.8;
                    WidthTextBox.Text = _newRoughWidth.ToString("F0");
                }

                Parameter roughHeightParam = type.get_Parameter(_roughHeightParam);
                if (roughHeightParam != null && roughHeightParam.HasValue)
                {
                    _newRoughHeight = roughHeightParam.AsDouble() * 304.8;
                    HeightTextBox.Text = _newRoughHeight.ToString("F0");
                }

                LoadDoorSystemSizeParameters(); // Обновляем системные размеры
                DrawDoorDiagram(); // Перерисовываем схему
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка обновления параметров: {ex.Message}";
            }
        }

        // ========================= СОЗДАНИЕ НОВОГО ТИПА =========================
        private void CreateTypeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string newTypeName = NewTypeNameTextBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(newTypeName))
                {
                    StatusText.Text = "Введите имя нового типоразмера";
                    NewTypeNameTextBox.Focus();
                    return;
                }

                Family family = _currentType.Family;

                FilteredElementCollector collector = new FilteredElementCollector(_doc);
                var existingTypes = collector
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.Family != null && fs.Family.Id == family.Id)
                    .ToList();

                if (existingTypes.Any(t => t.Name.Equals(newTypeName, StringComparison.CurrentCultureIgnoreCase)))
                {
                    StatusText.Text = $"Тип '{newTypeName}' уже существует";
                    return;
                }

                using (Transaction tx = new Transaction(_doc, "Создание нового типа двери"))
                {
                    tx.Start();

                    ElementType newType = _currentType.Duplicate(newTypeName);
                    _selectedType = newType as FamilySymbol;
                    _currentType = _selectedType;

                    tx.Commit();
                }

                NewTypeNameTextBox.Text = "";

                LoadFamilyTypes();
                UpdateCurrentTypeDisplay();
                UpdateParametersFromType(_currentType);

                // Перезагружаем все параметры
                LoadMaterialParameters();
                LoadConstructionParameters();
                LoadVisibilityParameters();
                LoadOtherParameters();

                StatusText.Text = $"Создан новый тип: {_selectedType.Name}";
                UpdateAndRefreshButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка создания типа: {ex.Message}");
            }
        }

        // ========================= ОБРАБОТЧИКИ ИЗМЕНЕНИЙ =========================
        private void Parameter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;

            double width, height;
            bool widthValid = double.TryParse(WidthTextBox.Text, out width);
            bool heightValid = double.TryParse(HeightTextBox.Text, out height);

            if (widthValid && heightValid && width > 0 && height > 0)
            {
                _newRoughWidth = width;
                _newRoughHeight = height;

                // Перерисовываем схему
                DrawDoorDiagram();
                HighlightIfMismatch();

                UpdateAndRefreshButton.IsEnabled = (Math.Abs(_newRoughWidth - _originalRoughWidth) > 0.01 ||
                                                    Math.Abs(_newRoughHeight - _originalRoughHeight) > 0.01 ||
                                                    _selectedType?.Id != _currentType?.Id);
            }
            else
            {
                UpdateAndRefreshButton.IsEnabled = false;
            }
        }

        // ========================= ОБНОВЛЕНИЕ ИЗОБРАЖЕНИЯ =========================
        private void UpdateAndRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isUpdating = true;
                UpdateAndRefreshButton.IsEnabled = false;

                StatusText.Text = "Применение изменений...";

                ApplyChanges();

                // Обновляем системные размеры после применения изменений
                LoadDoorSystemSizeParameters();

                // Перерисовываем схему
                DrawDoorDiagram();

                // Обновляем параметры в интерфейсе
                RefreshParameters();

                StatusText.Text = "Создание нового изображения...";

                string newImagePath = CreateNewDoorImage();

                if (!string.IsNullOrEmpty(newImagePath) && File.Exists(newImagePath))
                {
                    try
                    {
                        if (File.Exists(_currentImagePath) && _currentImagePath != newImagePath)
                        {
                            File.Delete(_currentImagePath);
                        }
                    }
                    catch { }

                    _currentImagePath = newImagePath;
                    LoadImage();

                    StatusText.Text = "Изображение обновлено!";
                }
                else
                {
                    StatusText.Text = "Ошибка создания изображения";
                }

                _isUpdating = false;
                UpdateAndRefreshButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обновления: {ex.Message}");
                _isUpdating = false;
                UpdateAndRefreshButton.IsEnabled = true;
            }
        }

        private string CreateNewDoorImage()
        {
            try
            {
                string newImagePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"door_{DateTime.Now:yyyyMMddHHmmss}.png");

                View currentView = _uidoc.ActiveView;

                ViewSection tempView = _doc.GetElement(_tempViewId) as ViewSection;

                if (tempView == null)
                {
                    throw new Exception("Временный разрез не найден");
                }

                _uidoc.ActiveView = tempView;

                System.Threading.Thread.Sleep(200);

                ImageExportOptions options = new ImageExportOptions
                {
                    FilePath = newImagePath,
                    FitDirection = FitDirectionType.Horizontal,
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_150,
                    ExportRange = ExportRange.CurrentView,
                    ZoomType = ZoomFitType.FitToPage,
                    ShadowViewsFileType = ImageFileType.PNG
                };

                _doc.ExportImage(options);

                System.Threading.Thread.Sleep(500);

                if (currentView != null)
                {
                    _uidoc.ActiveView = currentView;
                }

                return File.Exists(newImagePath) ? newImagePath : null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка создания изображения: {ex.Message}");
                return null;
            }
        }

        // ========================= СОХРАНЕНИЕ ПАРАМЕТРОВ ТИПА =========================
        private bool SaveTypeParameter(string key, WpfTextBox textBox, BuiltInParameter builtInParam, bool convertToInternalUnits = false)
        {
            if (_parameters.ContainsKey(key))
            {
                var param = _parameters[key];
                string value = textBox.Text;

                if (!string.IsNullOrEmpty(value))
                {
                    if (param.StorageType == StorageType.Double)
                    {
                        if (double.TryParse(value, out double doubleValue))
                        {
                            doubleValue = Math.Round(doubleValue, 0);

                            if (convertToInternalUnits)
                            {
                                doubleValue = doubleValue / 304.8;
                            }
                            param.Set(doubleValue);
                            return true;
                        }
                    }
                    else if (param.StorageType == StorageType.String)
                    {
                        param.Set(value);
                        return true;
                    }
                }
            }
            return false;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                // Возвращаем предыдущий активный вид
                if (_previousView != null && _uidoc.ActiveView.Id != _previousView.Id)
                {
                    _uidoc.ActiveView = _previousView;
                }
                if (_tempViewId != null && _tempViewId != ElementId.InvalidElementId)
                {
                    using (Transaction tx = new Transaction(_doc, "Удаление временного вида"))
                    {
                        tx.Start();

                        Element tempView = _doc.GetElement(_tempViewId);
                        if (tempView != null)
                        {
                            _doc.Delete(_tempViewId);
                        }

                        tx.Commit();
                    }
                }
                

                // Удаляем временный файл изображения
                DoorImage.Source = null;
                if (File.Exists(_currentImagePath))
                {
                    File.Delete(_currentImagePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при закрытии окна: {ex.Message}");
            }
        }

        // ========================= ЗАГРУЗКА МАТЕРИАЛОВ =========================
        private void LoadMaterials()
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(_doc);
                _allMaterials = collector
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .OrderBy(m => m.Name)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки материалов: {ex.Message}");
            }
        }

        // ========================= ОПРЕДЕЛЕНИЕ ТИПА ПАРАМЕТРА =========================
        private ParamKind GetParameterKind(Parameter param)
        {
            if (param.StorageType == StorageType.String)
                return ParamKind.String;

            if (param.StorageType == StorageType.Double)
                return ParamKind.Double;

            if (param.StorageType == StorageType.Integer)
                return ParamKind.Integer;

            if (param.StorageType == StorageType.ElementId)
            {
                try
                {
                    // Проверяем по имени параметра
                    if (param.Definition != null)
                    {
                        string paramName = param.Definition.Name.ToLower();

                        // Для Construction - приоритет FamilyType
                        string[] familyTypeKeywords = new string[]
                        {
                            "тип полотна",
                            "panel type",
                            "тип панели",
                            "тип створки",
                            "leaf type",
                            "выбор полотна",
                            "полотно",
                            "door panel",
                            "тип полотна двери"
                        };

                        if (familyTypeKeywords.Any(keyword => paramName.Contains(keyword)))
                        {
                            // Проверяем, в какой группе находится параметр
                            if (param.Definition != null)
                            {
                                ForgeTypeId groupTypeId = param.Definition.GetGroupTypeId();

                                // Если это группа Construction - точно FamilyType
                                if (groupTypeId == GroupTypeId.Construction)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Construction FamilyType param: {param.Definition.Name}");
                                    return ParamKind.FamilyType;
                                }

                                // Если это группа Materials, но имя содержит ключевые слова FamilyType,
                                // все равно проверяем значение
                                if (groupTypeId == GroupTypeId.Materials)
                                {
                                    // Для группы Materials проверяем, что именно хранится в параметре
                                    if (param.HasValue)
                                    {
                                        ElementId id = param.AsElementId();
                                        if (id != null && id != ElementId.InvalidElementId)
                                        {
                                            Element elem = _doc.GetElement(id);
                                            if (elem is Material)
                                            {
                                                System.Diagnostics.Debug.WriteLine($"Materials Material param: {param.Definition.Name}");
                                                return ParamKind.Material;
                                            }
                                            if (elem is FamilySymbol)
                                            {
                                                System.Diagnostics.Debug.WriteLine($"Materials FamilyType param: {param.Definition.Name}");
                                                return ParamKind.FamilyType;
                                            }
                                        }
                                    }
                                }
                            }

                            // По умолчанию для ключевых слов FamilyType
                            System.Diagnostics.Debug.WriteLine($"Default FamilyType param: {param.Definition.Name}");
                            return ParamKind.FamilyType;
                        }

                        // Ключевые слова для Material
                        string[] materialKeywords = new string[]
                        {
                            "материал",
                            "material",
                            "отделка",
                            "finish"
                        };

                        if (materialKeywords.Any(keyword => paramName.Contains(keyword)))
                        {
                            System.Diagnostics.Debug.WriteLine($"Material param: {param.Definition.Name}");
                            return ParamKind.Material;
                        }

                        // Если есть значение, проверяем его тип
                        if (param.HasValue)
                        {
                            ElementId id = param.AsElementId();
                            if (id != null && id != ElementId.InvalidElementId)
                            {
                                Element elem = _doc.GetElement(id);
                                if (elem != null)
                                {
                                    if (elem is Material)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Material by value: {param.Definition.Name}");
                                        return ParamKind.Material;
                                    }
                                    if (elem is FamilySymbol)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"FamilyType by value: {param.Definition.Name}");
                                        return ParamKind.FamilyType;
                                    }
                                }
                            }
                        }
                    }

                    return ParamKind.ElementId;
                }
                catch
                {
                    return ParamKind.ElementId;
                }
            }

            return ParamKind.String;
        }

        // ========================= MATERIALS =========================
        private bool IsMaterialAndFinishesParameter(Parameter param)
        {
            if (param == null || param.Definition == null) return false;

            try
            {
                ForgeTypeId groupTypeId = param.Definition.GetGroupTypeId();
                return groupTypeId == GroupTypeId.Materials;
            }
            catch
            {
                return false;
            }
        }

        private void LoadMaterialParameters()
        {
            try
            {
                if (_currentType == null) return;

                _materialParameters.Clear();
                _tempMaterialValues.Clear();

                foreach (Parameter param in _currentType.Parameters)
                {
                    if (IsMaterialAndFinishesParameter(param))
                    {
                        ParamKind kind = GetParameterKind(param);

                        ParameterInfo info = new ParameterInfo
                        {
                            Name = param.Definition.Name,
                            Parameter = param,
                            Kind = kind,
                            DisplayValue = GetParameterDisplayValue(param)
                        };

                        _materialParameters.Add(info);
                    }
                }

                _materialParameters = _materialParameters.OrderBy(p => p.Name).ToList();
                DisplayMaterialParameters();

                if (_materialParameters.Count > 0)
                {
                    StatusText.Text = $"Найдено параметров материалов: {_materialParameters.Count}";

                    // Автоматически раскрываем группу, если в ней есть параметры
                    if (_groupStates.ContainsKey("Materials") && !_groupStates["Materials"])
                    {
                        _groupStates["Materials"] = true;
                        if (_groupControls.ContainsKey("Materials"))
                        {
                            var group = _groupControls["Materials"];
                            group.Content.Visibility = WpfVisibility.Visible;
                            UpdateGroupArrow(group.Header, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка загрузки материалов: {ex.Message}";
            }
        }

        private string GetParameterDisplayValue(Parameter param)
        {
            try
            {
                if (param.StorageType == StorageType.ElementId && param.HasValue)
                {
                    ElementId id = param.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId)
                    {
                        Element elem = _doc.GetElement(id);
                        if (elem != null)
                            return elem.Name;
                        else
                            return "<Элемент не найден>";
                    }
                    else
                    {
                        return "<Не задано>";
                    }
                }
                else if (param.StorageType == StorageType.String && param.HasValue)
                {
                    return param.AsString();
                }
                else if (param.StorageType == StorageType.Integer && param.HasValue)
                {
                    return param.AsInteger().ToString();
                }
                else if (param.StorageType == StorageType.Double && param.HasValue)
                {
                    double valueInFeet = param.AsDouble();
                    if (IsLinearDimension(param))
                    {
                        double valueInMm = valueInFeet * 304.8;
                        return Math.Round(valueInMm, 0).ToString();
                    }
                    else
                    {
                        return valueInFeet.ToString("F2");
                    }
                }
            }
            catch { }

            return "";
        }

        private void DisplayMaterialParameters()
        {
            try
            {
                MaterialsStackPanel.Children.Clear();

                if (_materialParameters.Count == 0)
                {
                    WpfTextBlock noParamsText = new WpfTextBlock
                    {
                        Text = "Параметры материалов не найдены",
                        Foreground = Brushes.Gray,
                        FontStyle = FontStyles.Italic,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(10)
                    };
                    MaterialsStackPanel.Children.Add(noParamsText);
                    return;
                }

                foreach (var paramInfo in _materialParameters)
                {
                    CreateParameterControl(paramInfo, MaterialsStackPanel, _tempMaterialValues);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка отображения материалов: {ex.Message}";
            }
        }

        // ========================= CONSTRUCTION =========================
        private bool IsConstructionParameter(Parameter param)
        {
            if (param == null || param.Definition == null) return false;

            try
            {
                ForgeTypeId groupTypeId = param.Definition.GetGroupTypeId();
                return groupTypeId == GroupTypeId.Construction;
            }
            catch
            {
                return false;
            }
        }

        private void LoadConstructionParameters()
        {
            try
            {
                if (_currentType == null) return;

                _constructionParameters.Clear();
                _tempConstructionValues.Clear();

                foreach (Parameter param in _currentType.Parameters)
                {
                    if (IsConstructionParameter(param))
                    {
                        ParamKind kind = GetParameterKind(param);

                        System.Diagnostics.Debug.WriteLine($"Construction param: {param.Definition.Name}, Kind: {kind}");

                        ParameterInfo info = new ParameterInfo
                        {
                            Name = param.Definition.Name,
                            Parameter = param,
                            Kind = kind,
                            DisplayValue = GetParameterDisplayValue(param)
                        };

                        _constructionParameters.Add(info);
                    }
                }

                _constructionParameters = _constructionParameters.OrderBy(p => p.Name).ToList();
                DisplayConstructionParameters();

                if (_constructionParameters.Count > 0)
                {
                    StatusText.Text = $"Найдено параметров Construction: {_constructionParameters.Count}";

                    // Автоматически раскрываем группу, если в ней есть параметры
                    if (_groupStates.ContainsKey("Construction") && !_groupStates["Construction"])
                    {
                        _groupStates["Construction"] = true;
                        if (_groupControls.ContainsKey("Construction"))
                        {
                            var group = _groupControls["Construction"];
                            group.Content.Visibility = WpfVisibility.Visible;
                            UpdateGroupArrow(group.Header, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка загрузки Construction: {ex.Message}";
            }
        }

        private void DisplayConstructionParameters()
        {
            try
            {
                if (ConstructionStackPanel == null) return;
                ConstructionStackPanel.Children.Clear();

                if (_constructionParameters.Count == 0)
                {
                    WpfTextBlock noParamsText = new WpfTextBlock
                    {
                        Text = "Параметры Construction не найдены",
                        Foreground = Brushes.Gray,
                        FontStyle = FontStyles.Italic,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(10)
                    };
                    ConstructionStackPanel.Children.Add(noParamsText);
                    return;
                }

                foreach (var paramInfo in _constructionParameters)
                {
                    CreateParameterControl(paramInfo, ConstructionStackPanel, _tempConstructionValues);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка отображения Construction: {ex.Message}";
            }
        }

        // ========================= VISIBILITY =========================
        private bool IsVisibilityParameter(Parameter param)
        {
            if (param == null || param.Definition == null) return false;

            try
            {
                ForgeTypeId groupTypeId = param.Definition.GetGroupTypeId();
                return groupTypeId == GroupTypeId.Visibility;
            }
            catch
            {
                return false;
            }
        }

        private void LoadVisibilityParameters()
        {
            try
            {
                if (_currentType == null) return;

                _visibilityParameters.Clear();
                _tempVisibilityValues.Clear();

                foreach (Parameter param in _currentType.Parameters)
                {
                    if (IsVisibilityParameter(param))
                    {
                        ParamKind kind = GetParameterKind(param);

                        System.Diagnostics.Debug.WriteLine($"Visibility param: {param.Definition.Name}, Kind: {kind}");

                        ParameterInfo info = new ParameterInfo
                        {
                            Name = param.Definition.Name,
                            Parameter = param,
                            Kind = kind,
                            DisplayValue = GetParameterDisplayValue(param)
                        };

                        _visibilityParameters.Add(info);
                    }
                }

                _visibilityParameters = _visibilityParameters.OrderBy(p => p.Name).ToList();
                DisplayVisibilityParameters();

                if (_visibilityParameters.Count > 0)
                {
                    StatusText.Text = $"Найдено параметров Visibility: {_visibilityParameters.Count}";

                    // Автоматически раскрываем группу, если в ней есть параметры
                    if (_groupStates.ContainsKey("Visibility") && !_groupStates["Visibility"])
                    {
                        _groupStates["Visibility"] = true;
                        if (_groupControls.ContainsKey("Visibility"))
                        {
                            var group = _groupControls["Visibility"];
                            group.Content.Visibility = WpfVisibility.Visible;
                            UpdateGroupArrow(group.Header, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка загрузки Visibility: {ex.Message}";
            }
        }

        private void DisplayVisibilityParameters()
        {
            try
            {
                if (VisibilityStackPanel == null) return;
                VisibilityStackPanel.Children.Clear();

                if (_visibilityParameters.Count == 0)
                {
                    WpfTextBlock noParamsText = new WpfTextBlock
                    {
                        Text = "Параметры Visibility не найдены",
                        Foreground = Brushes.Gray,
                        FontStyle = FontStyles.Italic,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(10)
                    };
                    VisibilityStackPanel.Children.Add(noParamsText);
                    return;
                }

                foreach (var paramInfo in _visibilityParameters)
                {
                    CreateParameterControl(paramInfo, VisibilityStackPanel, _tempVisibilityValues);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка отображения Visibility: {ex.Message}";
            }
        }

        // ========================= OTHER =========================
        private bool IsOtherParameter(Parameter param)
        {
            if (param == null || param.Definition == null) return false;

            try
            {
                ForgeTypeId groupTypeId = param.Definition.GetGroupTypeId();
                return groupTypeId != GroupTypeId.Materials &&
                       groupTypeId != GroupTypeId.Construction &&
                       groupTypeId != GroupTypeId.Visibility;
            }
            catch
            {
                return true;
            }
        }

        private void LoadOtherParameters()
        {
            try
            {
                if (_currentType == null) return;

                _otherParameters.Clear();
                _tempOtherValues.Clear();

                foreach (Parameter param in _currentType.Parameters)
                {
                    if (IsOtherParameter(param))
                    {
                        ParamKind kind = GetParameterKind(param);

                        ParameterInfo info = new ParameterInfo
                        {
                            Name = param.Definition.Name,
                            Parameter = param,
                            Kind = kind,
                            DisplayValue = GetParameterDisplayValue(param)
                        };

                        _otherParameters.Add(info);
                    }
                }

                _otherParameters = _otherParameters.OrderBy(p => p.Name).ToList();
                DisplayOtherParameters();

                if (_otherParameters.Count > 0)
                {
                    StatusText.Text = $"Найдено параметров Other: {_otherParameters.Count}";

                    // Автоматически раскрываем группу, если в ней есть параметры
                    if (_groupStates.ContainsKey("Other") && !_groupStates["Other"])
                    {
                        _groupStates["Other"] = true;
                        if (_groupControls.ContainsKey("Other"))
                        {
                            var group = _groupControls["Other"];
                            group.Content.Visibility = WpfVisibility.Visible;
                            UpdateGroupArrow(group.Header, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка загрузки Other: {ex.Message}";
            }
        }

        private void DisplayOtherParameters()
        {
            try
            {
                if (OtherStackPanel == null) return;
                OtherStackPanel.Children.Clear();

                if (_otherParameters.Count == 0)
                {
                    WpfTextBlock noParamsText = new WpfTextBlock
                    {
                        Text = "Параметры Other не найдены",
                        Foreground = Brushes.Gray,
                        FontStyle = FontStyles.Italic,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(10)
                    };
                    OtherStackPanel.Children.Add(noParamsText);
                    return;
                }

                foreach (var paramInfo in _otherParameters)
                {
                    CreateParameterControl(paramInfo, OtherStackPanel, _tempOtherValues);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка отображения Other: {ex.Message}";
            }
        }

        // ========================= СОЗДАНИЕ КОНТРОЛОВ ПАРАМЕТРОВ =========================
        private bool IsLinearDimension(Parameter param)
        {
            if (param == null || param.Definition == null) return false;

            string paramName = param.Definition.Name.ToLower();
            string[] linearKeywords = new string[]
            {
                "width", "ширина",
                "height", "высота",
                "depth", "глубина",
                "length", "длина",
                "thickness", "толщина",
                "offset", "смещение",
                "radius", "радиус",
                "diameter", "диаметр",
                "rough", "грубый",
                "frame", "рама",
                "panel", "панель",
                "leaf", "створка",
                "size", "размер"
            };

            return linearKeywords.Any(keyword => paramName.Contains(keyword));
        }

        private void CreateParameterControl(ParameterInfo paramInfo, WpfStackPanel targetPanel, Dictionary<string, object> tempStorage)
        {
            WpfBorder paramBorder = new WpfBorder
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(5),
                Background = ColorFromRgb(245, 245, 245),
                CornerRadius = new CornerRadius(3)
            };

            WpfGrid paramGrid = new WpfGrid();
            paramGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            paramGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            WpfTextBlock nameText = new WpfTextBlock
            {
                Text = paramInfo.Name,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 3),
                TextWrapping = TextWrapping.Wrap
            };

            // Добавляем индикатор типа параметра
            switch (paramInfo.Kind)
            {
                case ParamKind.Material:
                    nameText.Text += " (материал)";
                    break;
                case ParamKind.FamilyType:
                    nameText.Text += " (тип полотна)";
                    if (_nestedFamilyTypes.Count > 0)
                        nameText.ToolTip = $"Доступно типов полотна: {_nestedFamilyTypes.Count}";
                    else
                        nameText.ToolTip = "Не найдено семейство АС-Полотно";
                    break;
                case ParamKind.ElementId:
                    nameText.Text += " (ID)";
                    break;
            }

            WpfGrid.SetRow(nameText, 0);
            paramGrid.Children.Add(nameText);

            // Создаем контрол в зависимости от типа параметра
            if (paramInfo.Kind == ParamKind.Material)
            {
                CreateMaterialCombo(paramInfo, paramGrid, tempStorage);
            }
            else if (paramInfo.Kind == ParamKind.FamilyType)
            {
                CreateNestedFamilyTypeCombo(paramInfo, paramGrid, tempStorage);
            }
            else
            {
                CreateParameterTextBox(paramInfo, paramGrid, tempStorage);
            }

            paramBorder.Child = paramGrid;
            targetPanel.Children.Add(paramBorder);
        }

        private void CreateMaterialCombo(ParameterInfo paramInfo, WpfGrid parentGrid, Dictionary<string, object> tempStorage)
        {
            WpfComboBox materialCombo = new WpfComboBox
            {
                Height = 30,
                DisplayMemberPath = "Name",
                Tag = new Tuple<ParameterInfo, Dictionary<string, object>>(paramInfo, tempStorage)
            };

            var materialItems = _allMaterials.Select(m => new MaterialItem { Id = m.Id, Name = m.Name }).ToList();
            materialItems.Insert(0, new MaterialItem { Id = ElementId.InvalidElementId, Name = "<Не задано>" });
            materialCombo.ItemsSource = materialItems;

            if (paramInfo.Parameter.HasValue)
            {
                ElementId currentId = paramInfo.Parameter.AsElementId();
                var selectedItem = materialItems.FirstOrDefault(m => m.Id == currentId);
                if (selectedItem != null)
                    materialCombo.SelectedItem = selectedItem;
            }

            materialCombo.SelectionChanged += MaterialCombo_SelectionChanged;
            WpfGrid.SetRow(materialCombo, 1);
            parentGrid.Children.Add(materialCombo);
        }

        private void CreateNestedFamilyTypeCombo(ParameterInfo paramInfo, WpfGrid parentGrid, Dictionary<string, object> tempStorage)
        {
            WpfComboBox combo = new WpfComboBox
            {
                Height = 30,
                Tag = new Tuple<ParameterInfo, Dictionary<string, object>>(paramInfo, tempStorage),
                DisplayMemberPath = "DisplayName"
            };

            List<FamilyTypeItem> items = new List<FamilyTypeItem>();

            try
            {
                // Добавляем пустой элемент
                items.Add(new FamilyTypeItem { Id = ElementId.InvalidElementId, Name = "<Не задано>", FamilyName = "" });

                if (_nestedFamilyTypes.Count > 0)
                {
                    items.AddRange(_nestedFamilyTypes);
                }

                combo.ItemsSource = items;

                // Устанавливаем текущее значение
                if (paramInfo.Parameter.HasValue)
                {
                    ElementId currentId = paramInfo.Parameter.AsElementId();
                    var selectedItem = items.FirstOrDefault(x => x.Id == currentId);
                    if (selectedItem != null)
                    {
                        combo.SelectedItem = selectedItem;
                    }
                    else
                    {
                        combo.SelectedItem = items[0];
                    }
                }
                else
                {
                    combo.SelectedItem = items[0];
                }

                if (_nestedFamilyTypes.Count > 0)
                {
                    combo.ToolTip = "Выберите тип полотна из семейства АС-Полотно";
                }
                else
                {
                    combo.ToolTip = "Убедитесь, что семейство АС-Полотно загружено в проект";
                }

                combo.SelectionChanged += NestedFamilyTypeCombo_SelectionChanged;

                // Добавляем обработчик загрузки для принудительного обновления
                combo.Loaded += (s, e) =>
                {
                    var cmb = s as WpfComboBox;
                    if (cmb != null && cmb.Items.Count > 0 && cmb.SelectedItem == null)
                    {
                        cmb.SelectedIndex = 0;
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка в CreateNestedFamilyTypeCombo: {ex.Message}");
                items.Clear();
                items.Add(new FamilyTypeItem { Id = ElementId.InvalidElementId, Name = "<Ошибка загрузки>", FamilyName = "" });
                combo.ItemsSource = items;
                combo.SelectedIndex = 0;
            }

            WpfGrid.SetRow(combo, 1);
            parentGrid.Children.Add(combo);
        }

        private void CreateParameterTextBox(ParameterInfo paramInfo, WpfGrid parentGrid, Dictionary<string, object> tempStorage)
        {
            WpfTextBox paramTextBox = new WpfTextBox
            {
                Height = 30,
                Padding = new Thickness(5),
                Text = paramInfo.DisplayValue ?? "",
                Tag = new Tuple<ParameterInfo, Dictionary<string, object>>(paramInfo, tempStorage)
            };

            if (paramInfo.Kind == ParamKind.Double && IsLinearDimension(paramInfo.Parameter))
            {
                paramTextBox.ToolTip = "Введите значение в миллиметрах";
            }

            paramTextBox.TextChanged += ParameterTextBox_TextChanged;
            WpfGrid.SetRow(paramTextBox, 1);
            parentGrid.Children.Add(paramTextBox);
        }

        // ========================= ОБРАБОТЧИКИ ИЗМЕНЕНИЙ ПАРАМЕТРОВ =========================
        private void MaterialCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdating) return;

            WpfComboBox combo = sender as WpfComboBox;
            if (combo == null) return;

            var tag = combo.Tag as Tuple<ParameterInfo, Dictionary<string, object>>;
            if (tag == null) return;

            ParameterInfo paramInfo = tag.Item1;
            var tempStorage = tag.Item2;

            MaterialItem selectedItem = combo.SelectedItem as MaterialItem;
            if (selectedItem == null) return;

            tempStorage[paramInfo.Name] = selectedItem.Id;

            UpdateAndRefreshButton.IsEnabled = true;
        }

        private void NestedFamilyTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdating) return;

            WpfComboBox combo = sender as WpfComboBox;
            if (combo == null) return;

            var tag = combo.Tag as Tuple<ParameterInfo, Dictionary<string, object>>;
            if (tag == null) return;

            ParameterInfo paramInfo = tag.Item1;
            var tempStorage = tag.Item2;

            FamilyTypeItem selectedItem = combo.SelectedItem as FamilyTypeItem;
            if (selectedItem == null) return;

            tempStorage[paramInfo.Name] = selectedItem.Id;

            // Обновляем статус
            if (selectedItem.Id == ElementId.InvalidElementId)
            {
                StatusText.Text = $"Параметр '{paramInfo.Name}': сброшен";
            }
            else
            {
                StatusText.Text = $"Параметр '{paramInfo.Name}': выбран тип '{selectedItem.DisplayName}'";
            }

            UpdateAndRefreshButton.IsEnabled = true;
        }

        private void ParameterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;

            WpfTextBox textBox = sender as WpfTextBox;
            if (textBox == null) return;

            var tag = textBox.Tag as Tuple<ParameterInfo, Dictionary<string, object>>;
            if (tag == null) return;

            ParameterInfo paramInfo = tag.Item1;
            var tempStorage = tag.Item2;

            // Сохраняем значение в зависимости от типа
            if (paramInfo.Kind == ParamKind.Double)
            {
                if (double.TryParse(textBox.Text, out double doubleValue))
                {
                    if (IsLinearDimension(paramInfo.Parameter))
                    {
                        // Конвертируем из мм в футы
                        doubleValue = doubleValue / 304.8;
                    }
                    tempStorage[paramInfo.Name] = doubleValue;
                }
            }
            else if (paramInfo.Kind == ParamKind.Integer)
            {
                if (int.TryParse(textBox.Text, out int intValue))
                {
                    tempStorage[paramInfo.Name] = intValue;
                }
            }
            else
            {
                tempStorage[paramInfo.Name] = textBox.Text;
            }

            UpdateAndRefreshButton.IsEnabled = true;
        }

        // ========================= СОХРАНЕНИЕ ПАРАМЕТРОВ (БЕЗ ТРАНЗАКЦИИ) =========================
        private void SaveMaterialParameters()
        {
            SaveParameters(_tempMaterialValues);
        }

        private void SaveConstructionParameters()
        {
            SaveParameters(_tempConstructionValues);
        }

        private void SaveVisibilityParameters()
        {
            SaveParameters(_tempVisibilityValues);
        }

        private void SaveOtherParameters()
        {
            SaveParameters(_tempOtherValues);
        }

        private void SaveParameters(Dictionary<string, object> tempStorage)
        {
            try
            {
                foreach (var tempValue in tempStorage)
                {
                    string paramName = tempValue.Key;
                    object value = tempValue.Value;

                    // Ищем параметр в типе двери
                    Parameter param = _currentType.LookupParameter(paramName);

                    if (param != null && !param.IsReadOnly)
                    {
                        if (value is ElementId elementId)
                        {
                            param.Set(elementId);
                        }
                        else if (value is double doubleValue)
                        {
                            param.Set(doubleValue);
                        }
                        else if (value is int intValue)
                        {
                            param.Set(intValue);
                        }
                        else if (value is string stringValue)
                        {
                            param.Set(stringValue);
                        }
                    }
                }

                tempStorage.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения параметров: {ex.Message}");
                throw;
            }
        }

        // ========================= ОБНОВЛЕНИЕ ИНТЕРФЕЙСА =========================
        private void RefreshParameters()
        {
            try
            {
                _isUpdating = true;

                // Перезагружаем все параметры
                LoadMaterialParameters();
                LoadConstructionParameters();
                LoadVisibilityParameters();
                LoadOtherParameters();

                _isUpdating = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка обновления параметров: {ex.Message}");
                _isUpdating = false;
            }
        }

        // ========================= ПРИМЕНЕНИЕ ИЗМЕНЕНИЙ =========================
        private void ApplyChanges()
        {
            bool typeChanged = false;
            bool widthUpdated = false;
            bool heightUpdated = false;
            bool parametersSaved = false;

            using (Transaction tx = new Transaction(_doc, "Изменение параметров двери"))
            {
                try
                {
                    tx.Start();

                    // 1. Смена типа двери если нужно
                    if (_selectedType != null && _door.GetTypeId() != _selectedType.Id)
                    {
                        string oldTypeName = _currentType?.Name ?? "неизвестно";
                        string newTypeName = _selectedType.Name;

                        StatusText.Text = $"Смена типа с '{oldTypeName}' на '{newTypeName}'...";

                        // Меняем тип у экземпляра
                        _door.ChangeTypeId(_selectedType.Id);
                        typeChanged = true;

                        // Обновляем текущий тип
                        _currentType = _selectedType;

                        // Очищаем кэш параметров и перезагружаем грубые размеры из нового типа
                        _parameters.Clear();
                        LoadTypeParameter(ROUGH_WIDTH_KEY, WidthTextBox, _roughWidthParam);
                        LoadTypeParameter(ROUGH_HEIGHT_KEY, HeightTextBox, _roughHeightParam);

                        StatusText.Text = $"Тип изменен на '{newTypeName}'";
                    }

                    // 2. Сохраняем грубые размеры (параметры типа)
                    if (WidthTextBox != null && _currentType != null)
                    {
                        // Проверяем, изменилось ли значение
                        if (double.TryParse(WidthTextBox.Text, out double newWidthMm))
                        {
                            double newWidthFeet = newWidthMm / 304.8;
                            Parameter widthParam = _currentType.get_Parameter(_roughWidthParam);

                            if (widthParam != null && !widthParam.IsReadOnly)
                            {
                                double currentWidthFeet = widthParam.AsDouble();
                                if (Math.Abs(newWidthFeet - currentWidthFeet) > 0.0001) // 0.1мм точность
                                {
                                    widthParam.Set(newWidthFeet);
                                    widthUpdated = true;
                                }
                            }
                        }
                    }

                    if (HeightTextBox != null && _currentType != null)
                    {
                        // Проверяем, изменилось ли значение
                        if (double.TryParse(HeightTextBox.Text, out double newHeightMm))
                        {
                            double newHeightFeet = newHeightMm / 304.8;
                            Parameter heightParam = _currentType.get_Parameter(_roughHeightParam);

                            if (heightParam != null && !heightParam.IsReadOnly)
                            {
                                double currentHeightFeet = heightParam.AsDouble();
                                if (Math.Abs(newHeightFeet - currentHeightFeet) > 0.0001) // 0.1мм точность
                                {
                                    heightParam.Set(newHeightFeet);
                                    heightUpdated = true;
                                }
                            }
                        }
                    }

                    // 3. Сохраняем остальные параметры (материалы, конструктив и т.д.)
                    if (_tempMaterialValues.Count > 0)
                    {
                        SaveMaterialParameters();
                        parametersSaved = true;
                    }

                    if (_tempConstructionValues.Count > 0)
                    {
                        SaveConstructionParameters();
                        parametersSaved = true;
                    }

                    if (_tempVisibilityValues.Count > 0)
                    {
                        SaveVisibilityParameters();
                        parametersSaved = true;
                    }

                    if (_tempOtherValues.Count > 0)
                    {
                        SaveOtherParameters();
                        parametersSaved = true;
                    }

                    // 4. Обновляем экземпляр в документе (нужно для корректного отображения)
                    if (typeChanged || widthUpdated || heightUpdated || parametersSaved)
                    {
                        // Регенерация документа для применения всех изменений
                        _doc.Regenerate();

                        // Перезагружаем экземпляр из документа (на случай если он изменился)
                        _door = _doc.GetElement(_door.Id);

                        tx.Commit();

                        // Обновляем значения после успешного коммита
                        _originalRoughWidth = _newRoughWidth;
                        _originalRoughHeight = _newRoughHeight;

                        // Очищаем временные хранилища
                        _tempMaterialValues.Clear();
                        _tempConstructionValues.Clear();
                        _tempVisibilityValues.Clear();
                        _tempOtherValues.Clear();

                        UpdateCurrentTypeDisplay();

                        // Формируем сообщение о результате
                        List<string> changes = new List<string>();
                        if (typeChanged) changes.Add("тип");
                        if (widthUpdated) changes.Add("ширина");
                        if (heightUpdated) changes.Add("высота");
                        if (parametersSaved) changes.Add("параметры");

                        string changesStr = string.Join(", ", changes);
                        StatusText.Text = $"✓ Изменения применены: {changesStr}";
                    }
                    else
                    {
                        tx.RollBack();
                        StatusText.Text = "Нет изменений для сохранения";
                    }
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    string errorMessage = $"Ошибка при применении изменений: {ex.Message}";
                    StatusText.Text = $"❌ {errorMessage}";

                    // Детальный лог для отладки
                    System.Diagnostics.Debug.WriteLine($"ApplyChanges Error: {ex}");
                    if (ex.InnerException != null)
                        System.Diagnostics.Debug.WriteLine($"Inner Exception: {ex.InnerException}");

                    MessageBox.Show(errorMessage, "Ошибка",
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ========================= ВЫБОР ПАРАМЕТРА РУЧКИ (ТОЧНЫЙ ПОИСК) =========================
        private void SelectHandleParameter(string paramName)
        {
            try
            {
                // Раскрываем группу Visibility
                if (_groupStates.ContainsKey("Visibility") && !_groupStates["Visibility"])
                {
                    _groupStates["Visibility"] = true;

                    if (_groupControls.ContainsKey("Visibility"))
                    {
                        var group = _groupControls["Visibility"];
                        group.Content.Visibility = WpfVisibility.Visible;
                        UpdateGroupArrow(group.Header, true);
                    }
                }

                // Ищем ТОЧНОЕ совпадение имени параметра (без учета регистра)
                var paramInfo = _visibilityParameters.FirstOrDefault(p =>
                    p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));

                if (paramInfo != null)
                {
                    HighlightParameterControl(paramInfo.Name);
                    StatusText.Text = $"Редактирование: {paramInfo.Name}";
                }
                else
                {
                    StatusText.Text = $"Параметр '{paramName}' не найден";

                    // Для отладки выведем все доступные параметры Visibility
                    System.Diagnostics.Debug.WriteLine("Доступные параметры Visibility:");
                    foreach (var p in _visibilityParameters)
                    {
                        System.Diagnostics.Debug.WriteLine($"  - {p.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка выбора ручки: {ex.Message}");
            }
        }

        // ========================= ВЫБОР ПАРАМЕТРА В ГРУППЕ OTHER =========================
        private void SelectOtherParameter(string paramName)
        {
            try
            {
                // Раскрываем группу Other
                if (_groupStates.ContainsKey("Other") && !_groupStates["Other"])
                {
                    _groupStates["Other"] = true;

                    if (_groupControls.ContainsKey("Other"))
                    {
                        var group = _groupControls["Other"];
                        group.Content.Visibility = WpfVisibility.Visible;
                        UpdateGroupArrow(group.Header, true);
                    }
                }

                // Ищем ТОЧНОЕ совпадение имени параметра
                var paramInfo = _otherParameters.FirstOrDefault(p =>
                    p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));

                if (paramInfo != null)
                {
                    HighlightParameterControl(paramInfo.Name);
                    StatusText.Text = $"Редактирование: {paramInfo.Name}";
                }
                else
                {
                    StatusText.Text = $"Параметр '{paramName}' не найден";

                    // Для отладки выведем все доступные параметры Other
                    System.Diagnostics.Debug.WriteLine("Доступные параметры Other:");
                    foreach (var p in _otherParameters)
                    {
                        System.Diagnostics.Debug.WriteLine($"  - {p.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка выбора параметра Other: {ex.Message}");
            }
        }

        // ========================= ВСПОМОГАТЕЛЬНЫЙ МЕТОД ДЛЯ ВЫБОРА ПАРАМЕТРА ТИПА ПОЛОТНА =========================
        private void SelectPanelTypeParameter(string paramName)
        {
            try
            {
                // Раскрываем группу Construction
                if (_groupStates.ContainsKey("Construction") && !_groupStates["Construction"])
                {
                    _groupStates["Construction"] = true;
                    if (_groupControls.ContainsKey("Construction"))
                    {
                        var group = _groupControls["Construction"];
                        group.Content.Visibility = WpfVisibility.Visible;
                        UpdateGroupArrow(group.Header, true);
                    }
                }

                // Ищем параметр в Construction параметрах (сначала точное совпадение, затем частичное)
                var paramInfo = _constructionParameters.FirstOrDefault(p =>
                    p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains(paramName) ||
                    paramName.Contains(p.Name) ||
                    p.Name.Contains("тип полотна") ||
                    p.Name.Contains("panel type"));

                if (paramInfo != null)
                {
                    // Подсвечиваем найденный параметр
                    HighlightParameterControl(paramInfo.Name);
                    StatusText.Text = $"Редактирование: {paramInfo.Name}";
                }
                else
                {
                    StatusText.Text = $"Параметр '{paramName}' не найден";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при выборе параметра: {ex.Message}");
            }
        }

        // ========================= ПОДСВЕТКА ПАРАМЕТРА В ИНТЕРФЕЙСЕ =========================
        private void HighlightParameterControl(string paramName)
        {
            try
            {
                // Ищем контрол в ConstructionStackPanel и VisibilityStackPanel
                WpfStackPanel[] panels = new WpfStackPanel[]
                {
                    ConstructionStackPanel,
                    VisibilityStackPanel,
                    MaterialsStackPanel,
                    OtherStackPanel
                };

                foreach (var panel in panels)
                {
                    if (panel == null) continue;

                    foreach (var child in panel.Children)
                    {
                        if (child is WpfBorder border && border.Child is WpfGrid grid)
                        {
                            // Ищем TextBlock с именем параметра
                            foreach (var gridChild in grid.Children)
                            {
                                if (gridChild is WpfTextBlock textBlock && textBlock.Text.Contains(paramName))
                                {
                                    // Подсвечиваем border
                                    border.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 255, 255, 0));
                                    border.BorderBrush = Brushes.Orange;
                                    border.BorderThickness = new Thickness(2);

                                    // Прокручиваем к этому элементу
                                    textBlock.BringIntoView();

                                    // Сбрасываем подсветку через 2 секунды
                                    System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer();
                                    timer.Interval = TimeSpan.FromSeconds(2);
                                    timer.Tick += (s, ev) => {
                                        border.Background = ColorFromRgb(245, 245, 245);
                                        border.BorderBrush = Brushes.Transparent;
                                        border.BorderThickness = new Thickness(0);
                                        timer.Stop();
                                    };
                                    timer.Start();

                                    return;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при подсветке параметра: {ex.Message}");
            }
        }

        // ========================= ВСПОМОГАТЕЛЬНЫЙ МЕТОД ДЛЯ ПОЛУЧЕНИЯ ЗНАЧЕНИЯ ПАРАМЕТРА =========================
        private double GetParameterValue(string paramName, double defaultValue)
        {
            try
            {
                if (_currentType != null)
                {
                    Parameter param = _currentType.LookupParameter(paramName);
                    if (param != null && param.HasValue && param.StorageType == StorageType.Double)
                    {
                        // Конвертируем из футов в мм
                        return param.AsDouble() * 304.8;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка получения параметра {paramName}: {ex.Message}");
            }

            return defaultValue;
        }

        // ========================= ВСПОМОГАТЕЛЬНЫЙ МЕТОД ДЛЯ ОТРИСОВКИ РАЗМЕРОВ ОСТЕКЛЕНИЯ =========================
        private void DrawGlassDimension(double x1, double y1, double x2, double y2,
                                        SolidColorBrush color, double valueMm, string tooltip,
                                        Action clickAction, double textX, double textY,
                                        bool isHorizontal = false)
        {
            // Основная линия
            System.Windows.Shapes.Line dimensionLine = new System.Windows.Shapes.Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = color,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = $"{tooltip}: {valueMm:F0} мм\nНажмите для изменения"
            };
            dimensionLine.MouseLeftButtonUp += (s, e) => clickAction();
            DiagramCanvas.Children.Add(dimensionLine);

            // Засечки
            if (isHorizontal)
            {
                // Горизонтальные засечки (вертикальные линии)
                System.Windows.Shapes.Line tick1 = new System.Windows.Shapes.Line
                {
                    X1 = x1,
                    Y1 = y1 - 5,
                    X2 = x1,
                    Y2 = y1 + 5,
                    Stroke = color,
                    StrokeThickness = 1.5,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                tick1.MouseLeftButtonUp += (s, e) => clickAction();
                DiagramCanvas.Children.Add(tick1);

                System.Windows.Shapes.Line tick2 = new System.Windows.Shapes.Line
                {
                    X1 = x2,
                    Y1 = y2 - 5,
                    X2 = x2,
                    Y2 = y2 + 5,
                    Stroke = color,
                    StrokeThickness = 1.5,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                tick2.MouseLeftButtonUp += (s, e) => clickAction();
                DiagramCanvas.Children.Add(tick2);
            }
            else
            {
                // Вертикальные засечки (горизонтальные линии)
                System.Windows.Shapes.Line tick1 = new System.Windows.Shapes.Line
                {
                    X1 = x1 - 5,
                    Y1 = y1,
                    X2 = x1 + 5,
                    Y2 = y1,
                    Stroke = color,
                    StrokeThickness = 1.5,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                tick1.MouseLeftButtonUp += (s, e) => clickAction();
                DiagramCanvas.Children.Add(tick1);

                System.Windows.Shapes.Line tick2 = new System.Windows.Shapes.Line
                {
                    X1 = x2 - 5,
                    Y1 = y2,
                    X2 = x2 + 5,
                    Y2 = y2,
                    Stroke = color,
                    StrokeThickness = 1.5,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                tick2.MouseLeftButtonUp += (s, e) => clickAction();
                DiagramCanvas.Children.Add(tick2);
            }

            // Текст с значением
            WpfTextBlock dimensionText = new WpfTextBlock
            {
                Text = $"{valueMm:F0} мм",
                Foreground = color,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Background = Brushes.White,
                Padding = new Thickness(2),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = $"{tooltip}: {valueMm:F0} мм\nНажмите для изменения"
            };
            dimensionText.MouseLeftButtonUp += (s, e) => clickAction();

            Canvas.SetLeft(dimensionText, textX);
            Canvas.SetTop(dimensionText, textY);
            DiagramCanvas.Children.Add(dimensionText);
        }

        // ========================= ОТРИСОВКА СХЕМЫ =========================
        private void DrawDoorDiagram()
        {
            if (DiagramCanvas == null) return;

            DiagramCanvas.Children.Clear();

            DiagramCanvas.Width = 300;
            DiagramCanvas.Height = 400;

            double widthMm = 0;
            double heightMm = 0;

            try
            {
                // Получаем размеры
                Parameter doorWidthParam = _door?.get_Parameter(_doorWidthParam);
                Parameter doorHeightParam = _door?.get_Parameter(_doorHeightParam);

                if (doorWidthParam != null && doorWidthParam.HasValue)
                {
                    widthMm = doorWidthParam.AsDouble() * 304.8;
                }
                else
                {
                    Parameter roughWidthParam = _currentType?.get_Parameter(_roughWidthParam);
                    if (roughWidthParam != null && roughWidthParam.HasValue)
                        widthMm = roughWidthParam.AsDouble() * 304.8;
                }

                if (doorHeightParam != null && doorHeightParam.HasValue)
                {
                    heightMm = doorHeightParam.AsDouble() * 304.8;
                }
                else
                {
                    Parameter roughHeightParam = _currentType?.get_Parameter(_roughHeightParam);
                    if (roughHeightParam != null && roughHeightParam.HasValue)
                        heightMm = roughHeightParam.AsDouble() * 304.8;
                }

                if (widthMm <= 0 || heightMm <= 0)
                {
                    WpfTextBlock errorText = new WpfTextBlock
                    {
                        Text = "Некорректные размеры",
                        Foreground = Brushes.Red,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Canvas.SetLeft(errorText, DiagramCanvas.Width / 2 - 50);
                    Canvas.SetTop(errorText, DiagramCanvas.Height / 2);
                    DiagramCanvas.Children.Add(errorText);
                    return;
                }

                // Масштабирование
                double maxWidth = DiagramCanvas.Width * 0.7;
                double maxHeight = DiagramCanvas.Height * 0.7;

                double scale = Math.Min(maxWidth / widthMm, maxHeight / heightMm) * 0.9;

                double rectWidth = widthMm * scale;
                double rectHeight = heightMm * scale;

                double rectX = (DiagramCanvas.Width - rectWidth) / 2;
                double rectY = (DiagramCanvas.Height - rectHeight) / 2;

                // Определяем тип двери по названию семейства
                string familyName = _currentType?.Family?.Name ?? "";
                bool isDoubleDoor = familyName.Contains("Двупольная") || familyName.Contains("Double") || familyName.Contains("Двухстворчатая");
                bool isSingleDoor = familyName.Contains("Однопольная") || familyName.Contains("Single") || familyName.Contains("Одностворчатая");

                // ===== НОВАЯ ЛОГИКА: Получаем значение параметра "Равные полотна" =====
                bool isEqualLeaves = GetEqualLeavesParameterValue();

                // Получаем значения ширины полотен
                double leaf1WidthMm;
                double leaf2WidthMm;

                if (isEqualLeaves)
                {
                    // Если галка нажата - полотна равны (симметричная дверь)
                    leaf1WidthMm = widthMm / 2;
                    leaf2WidthMm = widthMm / 2;
                    System.Diagnostics.Debug.WriteLine("Равные полотна ВКЛ: симметричная дверь");
                }
                else
                {
                    // Если галка отжата - берем реальные размеры из параметров
                    leaf1WidthMm = GetParameterValue("Полотно1_Ширина", widthMm / 2);
                    leaf2WidthMm = GetParameterValue("Полотно2_Ширина", widthMm / 2);
                    System.Diagnostics.Debug.WriteLine($"Равные полотна ВЫКЛ: реальные размеры {leaf1WidthMm:F0} / {leaf2WidthMm:F0}");
                }

                // Для однопольной двери используем только одно полотно
                if (!isDoubleDoor)
                {
                    leaf1WidthMm = widthMm;
                    leaf2WidthMm = 0;
                }

                string doorType = isDoubleDoor ? "Двупольная" : (isSingleDoor ? "Однопольная" : "Стандартная");

                // Получаем значения параметров остекления для левой створки (в мм)
                double leftTopOffsetMm = GetParameterValue("Рама двери1_Ширина вверху", 20);
                double leftBottomOffsetMm = GetParameterValue("Рама двери1_Ширина внизу", 20);
                double leftLeftOffsetMm = GetParameterValue("Рама двери1_Ширина слева", 20);
                double leftRightOffsetMm = GetParameterValue("Рама двери1_Ширина справа", 20);

                // Получаем значения параметров остекления для правой створки (в мм)
                double rightTopOffsetMm = GetParameterValue("Рама двери2_Ширина вверху", 20);
                double rightBottomOffsetMm = GetParameterValue("Рама двери2_Ширина внизу", 20);
                double rightLeftOffsetMm = GetParameterValue("Рама двери2_Ширина слева", 20);
                double rightRightOffsetMm = GetParameterValue("Рама двери2_Ширина справа", 20);

                // Цвет дерева для полотна
                SolidColorBrush woodColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 120, 80)); // Коричневый

                // Цвет для обводки створок и наличников (темно-коричневый)
                SolidColorBrush darkBrownBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(101, 67, 33));

                // Цвет для остекления - голубой полупрозрачный
                SolidColorBrush glassColor = new SolidColorBrush(System.Windows.Media.Color.FromArgb(150, 173, 216, 230)); // LightBlue с прозрачностью

                // Цвет для остекления при наведении - более яркий голубой
                SolidColorBrush glassHoverColor = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 100, 150, 255));

                // Цвет для размерных линий остекления - зеленый для левой створки, сине-зеленый для правой
                SolidColorBrush leftGlassDimColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 150, 0)); // Зеленый
                SolidColorBrush rightGlassDimColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 100, 150)); // Сине-зеленый

                // Цвет для размеров полотен
                SolidColorBrush leafDimColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 100, 0)); // Оранжево-коричневый

                // Рисуем основу двери (дерево)
                WpfRectangle doorRect = new WpfRectangle
                {
                    Width = rectWidth,
                    Height = rectHeight,
                    Stroke = Brushes.Black,
                    StrokeThickness = 2,
                    Fill = woodColor,
                    Opacity = 0.8,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = "DoorRect"
                };

                string sourceInfo = (doorWidthParam != null && doorWidthParam.HasValue) ? "системные" : "из типа";
                ToolTipService.SetToolTip(doorRect, $"Ширина: {widthMm:F0} мм\nВысота: {heightMm:F0} мм\nТип: {doorType}\n(размеры: {sourceInfo})");

                // Добавляем обработчики для параметра "Равные полотна"
                doorRect.MouseEnter += (s, e) =>
                {
                    doorRect.Stroke = Brushes.Blue;
                    doorRect.StrokeThickness = 3;
                    StatusText.Text = "Нажмите чтобы изменить параметр 'Равные полотна'";
                };

                doorRect.MouseLeave += (s, e) =>
                {
                    doorRect.Stroke = Brushes.Black;
                    doorRect.StrokeThickness = 2;
                    StatusText.Text = "Готово";
                };

                doorRect.MouseLeftButtonUp += (s, e) =>
                {
                    ToggleEqualLeavesParameter();
                };

                Canvas.SetLeft(doorRect, rectX);
                Canvas.SetTop(doorRect, rectY);
                DiagramCanvas.Children.Add(doorRect);

                // Рисуем наличники ВНУТРИ полотна - вплотную к границам
                double casingThickness = 5; // Толщина наличника

                // Верхний наличник (горизонтальный) - от левого до правого края
                WpfRectangle topCasing = new WpfRectangle
                {
                    Width = rectWidth,
                    Height = casingThickness,
                    Stroke = darkBrownBrush,
                    StrokeThickness = 1,
                    Fill = darkBrownBrush,
                    Opacity = 0.9
                };
                Canvas.SetLeft(topCasing, rectX);
                Canvas.SetTop(topCasing, rectY);
                DiagramCanvas.Children.Add(topCasing);

                // Нижний наличник (горизонтальный) - от левого до правого края
                WpfRectangle bottomCasing = new WpfRectangle
                {
                    Width = rectWidth,
                    Height = casingThickness,
                    Stroke = darkBrownBrush,
                    StrokeThickness = 1,
                    Fill = darkBrownBrush,
                    Opacity = 0.9
                };
                Canvas.SetLeft(bottomCasing, rectX);
                Canvas.SetTop(bottomCasing, rectY + rectHeight - casingThickness);
                DiagramCanvas.Children.Add(bottomCasing);

                // Левый наличник (вертикальный) - от верха до низа
                WpfRectangle leftCasing = new WpfRectangle
                {
                    Width = casingThickness,
                    Height = rectHeight,
                    Stroke = darkBrownBrush,
                    StrokeThickness = 1,
                    Fill = darkBrownBrush,
                    Opacity = 0.9
                };
                Canvas.SetLeft(leftCasing, rectX);
                Canvas.SetTop(leftCasing, rectY);
                DiagramCanvas.Children.Add(leftCasing);

                // Правый наличник (вертикальный) - от верха до низа
                WpfRectangle rightCasing = new WpfRectangle
                {
                    Width = casingThickness,
                    Height = rectHeight,
                    Stroke = darkBrownBrush,
                    StrokeThickness = 1,
                    Fill = darkBrownBrush,
                    Opacity = 0.9
                };
                Canvas.SetLeft(rightCasing, rectX + rectWidth - casingThickness);
                Canvas.SetTop(rightCasing, rectY);
                DiagramCanvas.Children.Add(rightCasing);

                // Рисуем дверь в зависимости от типа
                if (isDoubleDoor)
                {
                    // Для двупольной двери используем размеры полотен (симметричные или реальные)
                    double leaf1WidthPx = leaf1WidthMm * scale;
                    double leaf2WidthPx = leaf2WidthMm * scale;

                    // Корректируем, если сумма не совпадает с общей шириной
                    double totalLeafWidth = leaf1WidthPx + leaf2WidthPx;
                    if (Math.Abs(totalLeafWidth - rectWidth) > 1)
                    {
                        // Пропорционально корректируем
                        leaf1WidthPx = rectWidth * (leaf1WidthMm / (leaf1WidthMm + leaf2WidthMm));
                        leaf2WidthPx = rectWidth - leaf1WidthPx;
                    }

                    double leftLeafX = rectX;
                    double rightLeafX = rectX + leaf1WidthPx;

                    // Обводка левой створки
                    WpfRectangle leftLeaf = new WpfRectangle
                    {
                        Width = leaf1WidthPx - 1,
                        Height = rectHeight,
                        Stroke = darkBrownBrush,
                        StrokeThickness = 1.5,
                        Fill = Brushes.Transparent
                    };
                    Canvas.SetLeft(leftLeaf, leftLeafX);
                    Canvas.SetTop(leftLeaf, rectY);
                    DiagramCanvas.Children.Add(leftLeaf);

                    // Обводка правой створки
                    WpfRectangle rightLeaf = new WpfRectangle
                    {
                        Width = leaf2WidthPx - 1,
                        Height = rectHeight,
                        Stroke = darkBrownBrush,
                        StrokeThickness = 1.5,
                        Fill = Brushes.Transparent
                    };
                    Canvas.SetLeft(rightLeaf, rightLeafX + 1);
                    Canvas.SetTop(rightLeaf, rectY);
                    DiagramCanvas.Children.Add(rightLeaf);

                    // Вертикальная линия между створками
                    System.Windows.Shapes.Line dividerLine = new System.Windows.Shapes.Line
                    {
                        X1 = rightLeafX,
                        Y1 = rectY,
                        X2 = rightLeafX,
                        Y2 = rectY + rectHeight,
                        Stroke = darkBrownBrush,
                        StrokeThickness = 2,
                        StrokeDashArray = new DoubleCollection { 2, 2 }
                    };
                    DiagramCanvas.Children.Add(dividerLine);

                    // ========== РАЗМЕРЫ ПОЛОТЕН ==========
                    // Показываем размеры ТОЛЬКО когда галка НЕ нажата (isEqualLeaves = false)
                    if (!isEqualLeaves)
                    {
                        // Размер левого полотна - кликабельный
                        DrawLeafDimension(
                            leftLeafX, rectY + rectHeight + 25, rightLeafX, rectY + rectHeight + 25,
                            leafDimColor, leaf1WidthMm, "Полотно1_Ширина",
                            () => SelectOtherParameter("Полотно1_Ширина"),
                            leftLeafX + (leaf1WidthPx / 2) - 25, rectY + rectHeight + 30,
                            true,
                            true // кликабельный
                        );

                        // Размер правого полотна - НЕ кликабельный
                        DrawLeafDimension(
                            rightLeafX, rectY + rectHeight + 25, rectX + rectWidth, rectY + rectHeight + 25,
                            leafDimColor, leaf2WidthMm, "Полотно2_Ширина",
                            null, // нет действия при клике
                            rightLeafX + (leaf2WidthPx / 2) - 25, rectY + rectHeight + 30,
                            true,
                            false // не кликабельный
                        );
                    }

                    // ========== ОСТЕКЛЕНИЕ ЛЕВОЙ СТВОРКИ ==========

                    // Вычисляем позицию остекления левой створки на основе параметров (в пикселях)
                    double leftGlassTopY = rectY + (leftTopOffsetMm * scale);
                    double leftGlassBottomY = rectY + rectHeight - (leftBottomOffsetMm * scale);
                    double leftGlassLeftX = leftLeafX + (leftLeftOffsetMm * scale);
                    double leftGlassRightX = rightLeafX - (leftRightOffsetMm * scale);

                    // Размеры остекления левой створки
                    double leftGlassHeight = leftGlassBottomY - leftGlassTopY;
                    double leftGlassWidth = leftGlassRightX - leftGlassLeftX;

                    // Проверяем корректность размеров
                    if (leftGlassHeight <= 0 || leftGlassWidth <= 0)
                    {
                        // Используем значения по умолчанию
                        leftGlassHeight = rectHeight - 40;
                        leftGlassWidth = leaf1WidthPx * 0.6;
                        leftGlassTopY = rectY + 20;
                        leftGlassLeftX = leftLeafX + (leaf1WidthPx - leftGlassWidth) / 2;
                        leftGlassRightX = leftGlassLeftX + leftGlassWidth;
                        leftGlassBottomY = leftGlassTopY + leftGlassHeight;
                    }

                    // Остекление левой створки
                    WpfRectangle leftGlassRect = new WpfRectangle
                    {
                        Width = leftGlassWidth,
                        Height = leftGlassHeight,
                        Stroke = Brushes.DarkBlue,
                        StrokeThickness = 1.5,
                        Fill = glassColor,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Tag = "LeftGlass",
                        ToolTip = $"Левая створка - остекление\nВерх: {leftTopOffsetMm:F0} мм\nНиз: {leftBottomOffsetMm:F0} мм\nЛево: {leftLeftOffsetMm:F0} мм\nПраво: {leftRightOffsetMm:F0} мм"
                    };

                    // Добавляем обработчики событий для левого остекления
                    leftGlassRect.MouseEnter += (s, e) =>
                    {
                        leftGlassRect.Fill = glassHoverColor;
                        leftGlassRect.Stroke = Brushes.Blue;
                        leftGlassRect.StrokeThickness = 2;
                        StatusText.Text = "Нажмите чтобы выбрать тип левой створки";
                    };

                    leftGlassRect.MouseLeave += (s, e) =>
                    {
                        leftGlassRect.Fill = glassColor;
                        leftGlassRect.Stroke = Brushes.DarkBlue;
                        leftGlassRect.StrokeThickness = 1.5;
                        StatusText.Text = "Готово";
                    };

                    leftGlassRect.MouseLeftButtonUp += (s, e) =>
                    {
                        SelectPanelTypeParameter("Тип полотна двери 1");
                    };

                    Canvas.SetLeft(leftGlassRect, leftGlassLeftX);
                    Canvas.SetTop(leftGlassRect, leftGlassTopY);
                    DiagramCanvas.Children.Add(leftGlassRect);

                    // Крестик на левом остеклении
                    System.Windows.Shapes.Line leftGlassLine1 = new System.Windows.Shapes.Line
                    {
                        X1 = leftGlassLeftX,
                        Y1 = leftGlassTopY,
                        X2 = leftGlassLeftX + leftGlassWidth,
                        Y2 = leftGlassTopY + leftGlassHeight,
                        Stroke = Brushes.White,
                        StrokeThickness = 1.5,
                        IsHitTestVisible = false
                    };
                    DiagramCanvas.Children.Add(leftGlassLine1);

                    System.Windows.Shapes.Line leftGlassLine2 = new System.Windows.Shapes.Line
                    {
                        X1 = leftGlassLeftX + leftGlassWidth,
                        Y1 = leftGlassTopY,
                        X2 = leftGlassLeftX,
                        Y2 = leftGlassTopY + leftGlassHeight,
                        Stroke = Brushes.White,
                        StrokeThickness = 1.5,
                        IsHitTestVisible = false
                    };
                    DiagramCanvas.Children.Add(leftGlassLine2);

                    // ========== ИНТЕРАКТИВНЫЕ РАЗМЕРЫ ОСТЕКЛЕНИЯ ЛЕВОЙ СТВОРКИ ==========

                    // Верхний отступ левой створки - Рама двери1_Ширина вверху
                    DrawGlassDimension(
                        rectX - 25, rectY, rectX - 25, leftGlassTopY,
                        leftGlassDimColor, leftTopOffsetMm, "Левая створка - верхний отступ",
                        () => SelectOtherParameter("Рама двери1_Ширина вверху"),
                        rectX - 60, rectY + (leftGlassTopY - rectY) / 2 - 10
                    );

                    // Нижний отступ левой створки - Рама двери1_Ширина внизу
                    DrawGlassDimension(
                        rectX - 25, leftGlassBottomY, rectX - 25, rectY + rectHeight,
                        leftGlassDimColor, leftBottomOffsetMm, "Левая створка - нижний отступ",
                        () => SelectOtherParameter("Рама двери1_Ширина внизу"),
                        rectX - 60, leftGlassBottomY + ((rectY + rectHeight) - leftGlassBottomY) / 2 - 10
                    );

                    // Левый отступ левой створки - Рама двери1_Ширина слева
                    DrawGlassDimension(
                        leftLeafX, rectY - 25, leftGlassLeftX, rectY - 25,
                        leftGlassDimColor, leftLeftOffsetMm, "Левая створка - левый отступ",
                        () => SelectOtherParameter("Рама двери1_Ширина слева"),
                        leftLeafX + (leftGlassLeftX - leftLeafX) / 2 - 20, rectY - 45,
                        true
                    );

                    // Правый отступ левой створки - Рама двери1_Ширина справа
                    DrawGlassDimension(
                        leftGlassRightX, rectY - 25, rightLeafX, rectY - 25,
                        leftGlassDimColor, leftRightOffsetMm, "Левая створка - правый отступ",
                        () => SelectOtherParameter("Рама двери1_Ширина справа"),
                        leftGlassRightX + (rightLeafX - leftGlassRightX) / 2 - 20, rectY - 45,
                        true
                    );

                    // ========== ОСТЕКЛЕНИЕ ПРАВОЙ СТВОРКИ ==========

                    // Вычисляем позицию остекления правой створки на основе параметров (в пикселях)
                    double rightGlassTopY = rectY + (rightTopOffsetMm * scale);
                    double rightGlassBottomY = rectY + rectHeight - (rightBottomOffsetMm * scale);
                    double rightGlassLeftX = rightLeafX + 1 + (rightLeftOffsetMm * scale);
                    double rightGlassRightX = rectX + rectWidth - (rightRightOffsetMm * scale);

                    // Размеры остекления правой створки
                    double rightGlassHeight = rightGlassBottomY - rightGlassTopY;
                    double rightGlassWidth = rightGlassRightX - rightGlassLeftX;

                    // Проверяем корректность размеров
                    if (rightGlassHeight <= 0 || rightGlassWidth <= 0)
                    {
                        // Используем значения по умолчанию
                        rightGlassHeight = rectHeight - 40;
                        rightGlassWidth = leaf2WidthPx * 0.6;
                        rightGlassTopY = rectY + 20;
                        rightGlassLeftX = rightLeafX + 1 + (leaf2WidthPx - (leaf2WidthPx * 0.6)) / 2;
                        rightGlassRightX = rightGlassLeftX + rightGlassWidth;
                        rightGlassBottomY = rightGlassTopY + rightGlassHeight;
                    }

                    // Остекление правой створки
                    WpfRectangle rightGlassRect = new WpfRectangle
                    {
                        Width = rightGlassWidth,
                        Height = rightGlassHeight,
                        Stroke = Brushes.DarkBlue,
                        StrokeThickness = 1.5,
                        Fill = glassColor,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Tag = "RightGlass",
                        ToolTip = $"Правая створка - остекление\nВерх: {rightTopOffsetMm:F0} мм\nНиз: {rightBottomOffsetMm:F0} мм\nЛево: {rightLeftOffsetMm:F0} мм\nПраво: {rightRightOffsetMm:F0} мм"
                    };

                    // Добавляем обработчики событий для правого остекления
                    rightGlassRect.MouseEnter += (s, e) =>
                    {
                        rightGlassRect.Fill = glassHoverColor;
                        rightGlassRect.Stroke = Brushes.Blue;
                        rightGlassRect.StrokeThickness = 2;
                        StatusText.Text = "Нажмите чтобы выбрать тип правой створки";
                    };

                    rightGlassRect.MouseLeave += (s, e) =>
                    {
                        rightGlassRect.Fill = glassColor;
                        rightGlassRect.Stroke = Brushes.DarkBlue;
                        rightGlassRect.StrokeThickness = 1.5;
                        StatusText.Text = "Готово";
                    };

                    rightGlassRect.MouseLeftButtonUp += (s, e) =>
                    {
                        SelectPanelTypeParameter("Тип полотна двери 2");
                    };

                    Canvas.SetLeft(rightGlassRect, rightGlassLeftX);
                    Canvas.SetTop(rightGlassRect, rightGlassTopY);
                    DiagramCanvas.Children.Add(rightGlassRect);

                    // Крестик на правом остеклении
                    System.Windows.Shapes.Line rightGlassLine1 = new System.Windows.Shapes.Line
                    {
                        X1 = rightGlassLeftX,
                        Y1 = rightGlassTopY,
                        X2 = rightGlassLeftX + rightGlassWidth,
                        Y2 = rightGlassTopY + rightGlassHeight,
                        Stroke = Brushes.White,
                        StrokeThickness = 1.5,
                        IsHitTestVisible = false
                    };
                    DiagramCanvas.Children.Add(rightGlassLine1);

                    System.Windows.Shapes.Line rightGlassLine2 = new System.Windows.Shapes.Line
                    {
                        X1 = rightGlassLeftX + rightGlassWidth,
                        Y1 = rightGlassTopY,
                        X2 = rightGlassLeftX,
                        Y2 = rightGlassTopY + rightGlassHeight,
                        Stroke = Brushes.White,
                        StrokeThickness = 1.5,
                        IsHitTestVisible = false
                    };
                    DiagramCanvas.Children.Add(rightGlassLine2);

                    // ========== ИНТЕРАКТИВНЫЕ РАЗМЕРЫ ОСТЕКЛЕНИЯ ПРАВОЙ СТВОРКИ ==========

                    // Верхний отступ правой створки - Рама двери2_Ширина вверху
                    DrawGlassDimension(
                        rectX + rectWidth + 25, rectY, rectX + rectWidth + 25, rightGlassTopY,
                        rightGlassDimColor, rightTopOffsetMm, "Правая створка - верхний отступ",
                        () => SelectOtherParameter("Рама двери2_Ширина вверху"),
                        rectX + rectWidth + 30, rectY + (rightGlassTopY - rectY) / 2 - 10
                    );

                    // Нижний отступ правой створки - Рама двери2_Ширина внизу
                    DrawGlassDimension(
                        rectX + rectWidth + 25, rightGlassBottomY, rectX + rectWidth + 25, rectY + rectHeight,
                        rightGlassDimColor, rightBottomOffsetMm, "Правая створка - нижний отступ",
                        () => SelectOtherParameter("Рама двери2_Ширина внизу"),
                        rectX + rectWidth + 30, rightGlassBottomY + ((rectY + rectHeight) - rightGlassBottomY) / 2 - 10
                    );

                    // Левый отступ правой створки - Рама двери2_Ширина слева
                    DrawGlassDimension(
                        rightLeafX + 1, rectY - 25, rightGlassLeftX, rectY - 25,
                        rightGlassDimColor, rightLeftOffsetMm, "Правая створка - левый отступ",
                        () => SelectOtherParameter("Рама двери2_Ширина слева"),
                        rightLeafX + 1 + (rightGlassLeftX - (rightLeafX + 1)) / 2 - 20, rectY - 45,
                        true
                    );

                    // Правый отступ правой створки - Рама двери2_Ширина справа
                    DrawGlassDimension(
                        rightGlassRightX, rectY - 25, rectX + rectWidth, rectY - 25,
                        rightGlassDimColor, rightRightOffsetMm, "Правая створка - правый отступ",
                        () => SelectOtherParameter("Рама двери2_Ширина справа"),
                        rightGlassRightX + ((rectX + rectWidth) - rightGlassRightX) / 2 - 20, rectY - 45,
                        true
                    );

                    // РУЧКИ ДЛЯ ДВУПОЛЬНОЙ ДВЕРИ (ВОЗВРАЩАЕМ В ИСХОДНОЕ ПОЛОЖЕНИЕ)
                    System.Windows.Shapes.Ellipse leftHandle = new System.Windows.Shapes.Ellipse
                    {
                        Width = 10,
                        Height = 10,
                        Fill = Brushes.Gold,
                        Stroke = Brushes.Black,
                        StrokeThickness = 1,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Tag = "LeftHandle"
                    };

                    leftHandle.MouseEnter += (s, e) =>
                    {
                        leftHandle.Fill = Brushes.Orange;
                        leftHandle.StrokeThickness = 2;
                        StatusText.Text = "Нажмите чтобы изменить ручку полотна 1";
                    };

                    leftHandle.MouseLeave += (s, e) =>
                    {
                        leftHandle.Fill = Brushes.Gold;
                        leftHandle.StrokeThickness = 1;
                        StatusText.Text = "Готово";
                    };

                    leftHandle.MouseLeftButtonUp += (s, e) =>
                    {
                        SelectHandleParameter("Ручка полотна 1");
                    };

                    // Левая ручка - ближе к центру двери
                    Canvas.SetLeft(leftHandle, rectX + leaf1WidthPx - 18);
                    Canvas.SetTop(leftHandle, rectY + rectHeight / 2 - 5);
                    DiagramCanvas.Children.Add(leftHandle);

                    System.Windows.Shapes.Ellipse rightHandle = new System.Windows.Shapes.Ellipse
                    {
                        Width = 10,
                        Height = 10,
                        Fill = Brushes.Gold,
                        Stroke = Brushes.Black,
                        StrokeThickness = 1,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Tag = "RightHandle"
                    };

                    rightHandle.MouseEnter += (s, e) =>
                    {
                        rightHandle.Fill = Brushes.Orange;
                        rightHandle.StrokeThickness = 2;
                        StatusText.Text = "Нажмите чтобы изменить ручку полотна 2";
                    };

                    rightHandle.MouseLeave += (s, e) =>
                    {
                        rightHandle.Fill = Brushes.Gold;
                        rightHandle.StrokeThickness = 1;
                        StatusText.Text = "Готово";
                    };

                    rightHandle.MouseLeftButtonUp += (s, e) =>
                    {
                        SelectHandleParameter("Ручка полотна 2");
                    };

                    // Правая ручка - ближе к центру двери
                    Canvas.SetLeft(rightHandle, rectX + leaf1WidthPx + 10);
                    Canvas.SetTop(rightHandle, rectY + rectHeight / 2 - 5);
                    DiagramCanvas.Children.Add(rightHandle);
                }
                else
                {
                    // Однопольная или стандартная дверь - ОДНО ОСТЕКЛЕНИЕ С РАЗМЕРАМИ ИЗ ПАРАМЕТРОВ

                    // Вычисляем позицию остекления на основе параметров (в пикселях)
                    double glassTopY = rectY + (GetParameterValue("Рама двери_Ширина вверху", 20) * scale);
                    double glassBottomY = rectY + rectHeight - (GetParameterValue("Рама двери_Ширина внизу", 20) * scale);
                    double glassLeftX = rectX + (GetParameterValue("Рама двери_Ширина слева", 20) * scale);
                    double glassRightX = rectX + rectWidth - (GetParameterValue("Рама двери_Ширина справа", 20) * scale);

                    // Размеры остекления
                    double glassHeight = glassBottomY - glassTopY;
                    double glassWidth = glassRightX - glassLeftX;

                    // Проверяем корректность размеров
                    if (glassHeight <= 0 || glassWidth <= 0)
                    {
                        // Используем значения по умолчанию
                        glassHeight = rectHeight - 40;
                        glassWidth = rectWidth * 0.6;
                        glassTopY = rectY + 20;
                        glassLeftX = rectX + (rectWidth - glassWidth) / 2;
                        glassRightX = glassLeftX + glassWidth;
                        glassBottomY = glassTopY + glassHeight;
                    }

                    WpfRectangle glassRect = new WpfRectangle
                    {
                        Width = glassWidth,
                        Height = glassHeight,
                        Stroke = Brushes.DarkBlue,
                        StrokeThickness = 1.5,
                        Fill = glassColor,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Tag = "SingleGlass",
                        ToolTip = $"Остекление\nВерх: {GetParameterValue("Рама двери_Ширина вверху", 20):F0} мм\nНиз: {GetParameterValue("Рама двери_Ширина внизу", 20):F0} мм\nЛево: {GetParameterValue("Рама двери_Ширина слева", 20):F0} мм\nПраво: {GetParameterValue("Рама двери_Ширина справа", 20):F0} мм"
                    };

                    // Добавляем обработчики событий для остекления
                    glassRect.MouseEnter += (s, e) =>
                    {
                        glassRect.Fill = glassHoverColor;
                        glassRect.Stroke = Brushes.Blue;
                        glassRect.StrokeThickness = 2;
                        StatusText.Text = "Нажмите чтобы выбрать тип полотна двери";
                    };

                    glassRect.MouseLeave += (s, e) =>
                    {
                        glassRect.Fill = glassColor;
                        glassRect.Stroke = Brushes.DarkBlue;
                        glassRect.StrokeThickness = 1.5;
                        StatusText.Text = "Готово";
                    };

                    glassRect.MouseLeftButtonUp += (s, e) =>
                    {
                        SelectPanelTypeParameter("Тип полотна двери");
                    };

                    Canvas.SetLeft(glassRect, glassLeftX);
                    Canvas.SetTop(glassRect, glassTopY);
                    DiagramCanvas.Children.Add(glassRect);

                    // Крестики на остеклении
                    System.Windows.Shapes.Line glassLine1 = new System.Windows.Shapes.Line
                    {
                        X1 = glassLeftX,
                        Y1 = glassTopY,
                        X2 = glassLeftX + glassWidth,
                        Y2 = glassTopY + glassHeight,
                        Stroke = Brushes.White,
                        StrokeThickness = 1.5,
                        IsHitTestVisible = false
                    };
                    DiagramCanvas.Children.Add(glassLine1);

                    System.Windows.Shapes.Line glassLine2 = new System.Windows.Shapes.Line
                    {
                        X1 = glassLeftX + glassWidth,
                        Y1 = glassTopY,
                        X2 = glassLeftX,
                        Y2 = glassTopY + glassHeight,
                        Stroke = Brushes.White,
                        StrokeThickness = 1.5,
                        IsHitTestVisible = false
                    };
                    DiagramCanvas.Children.Add(glassLine2);

                    // ========== ИНТЕРАКТИВНЫЕ РАЗМЕРЫ ОСТЕКЛЕНИЯ ==========

                    SolidColorBrush glassDimColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 150, 0)); // Зеленый

                    // Верхний отступ - Рама двери_Ширина вверху
                    DrawGlassDimension(
                        rectX - 25, rectY, rectX - 25, glassTopY,
                        glassDimColor, GetParameterValue("Рама двери_Ширина вверху", 20), "Верхний отступ",
                        () => SelectOtherParameter("Рама двери_Ширина вверху"),
                        rectX - 60, rectY + (glassTopY - rectY) / 2 - 10
                    );

                    // Нижний отступ - Рама двери_Ширина внизу
                    DrawGlassDimension(
                        rectX - 25, glassBottomY, rectX - 25, rectY + rectHeight,
                        glassDimColor, GetParameterValue("Рама двери_Ширина внизу", 20), "Нижний отступ",
                        () => SelectOtherParameter("Рама двери_Ширина внизу"),
                        rectX - 60, glassBottomY + ((rectY + rectHeight) - glassBottomY) / 2 - 10
                    );

                    // Левый отступ - Рама двери_Ширина слева
                    DrawGlassDimension(
                        rectX, rectY - 25, glassLeftX, rectY - 25,
                        glassDimColor, GetParameterValue("Рама двери_Ширина слева", 20), "Левый отступ",
                        () => SelectOtherParameter("Рама двери_Ширина слева"),
                        rectX + (glassLeftX - rectX) / 2 - 20, rectY - 45,
                        true
                    );

                    // Правый отступ - Рама двери_Ширина справа
                    DrawGlassDimension(
                        glassRightX, rectY - 25, rectX + rectWidth, rectY - 25,
                        glassDimColor, GetParameterValue("Рама двери_Ширина справа", 20), "Правый отступ",
                        () => SelectOtherParameter("Рама двери_Ширина справа"),
                        glassRightX + ((rectX + rectWidth) - glassRightX) / 2 - 20, rectY - 45,
                        true
                    );

                    // РУЧКА ДЛЯ ОДНОПОЛЬНОЙ ДВЕРИ
                    System.Windows.Shapes.Ellipse handle = new System.Windows.Shapes.Ellipse
                    {
                        Width = 10,
                        Height = 10,
                        Fill = Brushes.Gold,
                        Stroke = Brushes.Black,
                        StrokeThickness = 1.5,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Tag = "SingleHandle"
                    };

                    handle.MouseEnter += (s, e) =>
                    {
                        handle.Fill = Brushes.Orange;
                        handle.StrokeThickness = 2.5;
                        StatusText.Text = "Нажмите чтобы изменить ручку";
                    };

                    handle.MouseLeave += (s, e) =>
                    {
                        handle.Fill = Brushes.Gold;
                        handle.StrokeThickness = 1.5;
                        StatusText.Text = "Готово";
                    };

                    handle.MouseLeftButtonUp += (s, e) =>
                    {
                        SelectHandleParameter("Ручка");
                    };

                    if (isSingleDoor)
                    {
                        Canvas.SetLeft(handle, rectX + rectWidth - 20);
                    }
                    else
                    {
                        Canvas.SetLeft(handle, rectX + rectWidth / 2 - 5);
                    }
                    Canvas.SetTop(handle, rectY + rectHeight / 2 - 5);
                    DiagramCanvas.Children.Add(handle);
                }

                // Добавляем основные размерные линии и текст (ширина/высота)
                double fontSize = 12;
                double offset = 5;

                // Линии ширины
                System.Windows.Shapes.Line widthLine1 = new System.Windows.Shapes.Line
                {
                    X1 = rectX,
                    Y1 = rectY + rectHeight + offset,
                    X2 = rectX + rectWidth,
                    Y2 = rectY + rectHeight + offset,
                    Stroke = Brushes.Red,
                    StrokeThickness = 1.5,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Изменить ширину"
                };
                widthLine1.MouseLeftButtonUp += (s, ev) =>
                {
                    WidthTextBox.Focus();
                    WidthTextBox.SelectAll();
                };

                System.Windows.Shapes.Line widthLine2 = new System.Windows.Shapes.Line
                {
                    X1 = rectX,
                    Y1 = rectY + rectHeight + offset - 3,
                    X2 = rectX,
                    Y2 = rectY + rectHeight + offset + 3,
                    Stroke = Brushes.Red,
                    StrokeThickness = 1.5,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Изменить ширину"
                };
                widthLine2.MouseLeftButtonUp += (s, ev) =>
                {
                    WidthTextBox.Focus();
                    WidthTextBox.SelectAll();
                };

                System.Windows.Shapes.Line widthLine3 = new System.Windows.Shapes.Line
                {
                    X1 = rectX + rectWidth,
                    Y1 = rectY + rectHeight + offset - 3,
                    X2 = rectX + rectWidth,
                    Y2 = rectY + rectHeight + offset + 3,
                    Stroke = Brushes.Red,
                    StrokeThickness = 1.5,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Изменить ширину"
                };
                widthLine3.MouseLeftButtonUp += (s, ev) =>
                {
                    WidthTextBox.Focus();
                    WidthTextBox.SelectAll();
                };

                DiagramCanvas.Children.Add(widthLine1);
                DiagramCanvas.Children.Add(widthLine2);
                DiagramCanvas.Children.Add(widthLine3);

                // Текст ширины
                WpfTextBlock widthText = new WpfTextBlock
                {
                    Text = $"{widthMm:F0} мм",
                    Foreground = Brushes.Red,
                    FontSize = fontSize,
                    FontWeight = FontWeights.Bold,
                    Background = Brushes.White,
                    Padding = new Thickness(2),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Изменить ширину"
                };
                widthText.MouseLeftButtonUp += (s, ev) =>
                {
                    WidthTextBox.Focus();
                    WidthTextBox.SelectAll();
                };

                Canvas.SetLeft(widthText, rectX + (rectWidth / 2) - 25);
                Canvas.SetTop(widthText, rectY + rectHeight + offset + 5);
                DiagramCanvas.Children.Add(widthText);

                // Линии высоты
                System.Windows.Shapes.Line heightLine1 = new System.Windows.Shapes.Line
                {
                    X1 = rectX - offset,
                    Y1 = rectY,
                    X2 = rectX - offset,
                    Y2 = rectY + rectHeight,
                    Stroke = Brushes.Blue,
                    StrokeThickness = 1.5,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Изменить высоту"
                };
                heightLine1.MouseLeftButtonUp += (s, ev) =>
                {
                    HeightTextBox.Focus();
                    HeightTextBox.SelectAll();
                };

                System.Windows.Shapes.Line heightLine2 = new System.Windows.Shapes.Line
                {
                    X1 = rectX - offset - 3,
                    Y1 = rectY,
                    X2 = rectX - offset + 3,
                    Y2 = rectY,
                    Stroke = Brushes.Blue,
                    StrokeThickness = 1.5,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Изменить высоту"
                };
                heightLine2.MouseLeftButtonUp += (s, ev) =>
                {
                    HeightTextBox.Focus();
                    HeightTextBox.SelectAll();
                };

                System.Windows.Shapes.Line heightLine3 = new System.Windows.Shapes.Line
                {
                    X1 = rectX - offset - 3,
                    Y1 = rectY + rectHeight,
                    X2 = rectX - offset + 3,
                    Y2 = rectY + rectHeight,
                    Stroke = Brushes.Blue,
                    StrokeThickness = 1.5,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Изменить высоту"
                };
                heightLine3.MouseLeftButtonUp += (s, ev) =>
                {
                    HeightTextBox.Focus();
                    HeightTextBox.SelectAll();
                };

                DiagramCanvas.Children.Add(heightLine1);
                DiagramCanvas.Children.Add(heightLine2);
                DiagramCanvas.Children.Add(heightLine3);

                // Текст высоты
                WpfTextBlock heightText = new WpfTextBlock
                {
                    Text = $"{heightMm:F0} мм",
                    Foreground = Brushes.Blue,
                    FontSize = fontSize,
                    FontWeight = FontWeights.Bold,
                    Background = Brushes.White,
                    Padding = new Thickness(2),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Изменить высоту"
                };
                heightText.MouseLeftButtonUp += (s, ev) =>
                {
                    HeightTextBox.Focus();
                    HeightTextBox.SelectAll();
                };

                Canvas.SetLeft(heightText, rectX - offset - 70);
                Canvas.SetTop(heightText, rectY + (rectHeight / 2) - 10);
                DiagramCanvas.Children.Add(heightText);

                // Индикатор источника размеров
                WpfTextBlock sourceIndicator = new WpfTextBlock
                {
                    Text = $"{sourceInfo} • {doorType}",
                    FontSize = 8,
                    Foreground = Brushes.Gray,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 255, 255, 255)),
                    Padding = new Thickness(2)
                };
                Canvas.SetLeft(sourceIndicator, rectX + 5);
                Canvas.SetTop(sourceIndicator, rectY + rectHeight - 15);
                DiagramCanvas.Children.Add(sourceIndicator);

                // ========== ДОБАВЛЯЕМ ТЕКСТ "РАВНЫЕ ПОЛОТНА" ПОД ИЗОБРАЖЕНИЕМ ==========
                AddEqualLeavesText(rectX, rectY, rectWidth, rectHeight);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка отрисовки схемы: {ex.Message}");
            }
        }
        // ========================= ПОЛУЧЕНИЕ ЗНАЧЕНИЯ ПАРАМЕТРА "РАВНЫЕ ПОЛОТНА" =========================
        private bool GetEqualLeavesParameterValue()
        {
            try
            {
                if (_currentType == null) return false;

                // Ищем параметр "Равные полотна" (можно добавить другие варианты названий)
                string[] paramNames = new string[]
                {
            "Равные полотна",
            "Равные створки",
            "Equal Leaves",
            "Equal Panels",
            "Симметричные полотна",
            "Симметричные створки"
                };

                foreach (string paramName in paramNames)
                {
                    Parameter param = _currentType.LookupParameter(paramName);
                    if (param != null && param.HasValue)
                    {
                        if (param.StorageType == StorageType.Integer)
                        {
                            return param.AsInteger() != 0;
                        }
                        else if (param.StorageType == StorageType.Double)
                        {
                            return Math.Abs(param.AsDouble()) > 0.001;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка получения параметра 'Равные полотна': {ex.Message}");
            }

            return false;
        }

        // ========================= ПЕРЕКЛЮЧЕНИЕ ПАРАМЕТРА "РАВНЫЕ ПОЛОТНА" =========================
        private void ToggleEqualLeavesParameter()
        {
            try
            {
                if (_currentType == null) return;

                // Ищем параметр "Равные полотна"
                string[] paramNames = new string[]
                {
            "Равные полотна",
            "Равные створки",
            "Equal Leaves",
            "Equal Panels",
            "Симметричные полотна",
            "Симметричные створки"
                };

                Parameter targetParam = null;

                foreach (string paramName in paramNames)
                {
                    targetParam = _currentType.LookupParameter(paramName);
                    if (targetParam != null) break;
                }

                if (targetParam == null)
                {
                    StatusText.Text = "Параметр 'Равные полотна' не найден";
                    return;
                }

                if (targetParam.IsReadOnly)
                {
                    StatusText.Text = "Параметр 'Равные полотна' доступен только для чтения";
                    return;
                }

                // Получаем текущее значение
                bool currentValue = false;
                if (targetParam.HasValue)
                {
                    if (targetParam.StorageType == StorageType.Integer)
                    {
                        currentValue = targetParam.AsInteger() != 0;
                    }
                    else if (targetParam.StorageType == StorageType.Double)
                    {
                        currentValue = Math.Abs(targetParam.AsDouble()) > 0.001;
                    }
                }

                // Новое значение (переключаем)
                bool newValue = !currentValue;

                // Сохраняем в транзакции
                using (Transaction tx = new Transaction(_doc, "Изменение параметра Равные полотна"))
                {
                    tx.Start();

                    if (targetParam.StorageType == StorageType.Integer)
                    {
                        targetParam.Set(newValue ? 1 : 0);
                    }
                    else if (targetParam.StorageType == StorageType.Double)
                    {
                        targetParam.Set(newValue ? 1.0 : 0.0);
                    }

                    tx.Commit();
                }

                // Обновляем интерфейс
                StatusText.Text = newValue ?
                    "✓ Равные полотна включены (симметричная дверь)" :
                    "✗ Равные полотна выключены (реальные размеры)";

                // Перерисовываем схему для обновления
                DrawDoorDiagram();

                // Активируем кнопку обновления
                UpdateAndRefreshButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка при изменении параметра: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Ошибка ToggleEqualLeavesParameter: {ex.Message}");
            }
        }

        // ========================= ДОБАВЛЕНИЕ ТЕКСТА "РАВНЫЕ ПОЛОТНА" ПОД СХЕМОЙ =========================
        private void AddEqualLeavesText(double rectX, double rectY, double rectWidth, double rectHeight)
        {
            try
            {
                // Получаем значение параметра "Равные полотна"
                bool isEqualLeaves = GetEqualLeavesParameterValue();

                // Создаем Border для фона текста
                WpfBorder textBorder = new WpfBorder
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 240, 240, 240)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 2, 5, 2),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Нажмите чтобы изменить параметр 'Равные полотна'",
                    Tag = "EqualLeavesText"
                };

                // Создаем горизонтальный StackPanel для текста и индикатора
                WpfStackPanel textPanel = new WpfStackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                // Текст "Равные полотна"
                WpfTextBlock textBlock = new WpfTextBlock
                {
                    Text = "Равные полотна:",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.Black,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 3, 0)
                };

                // Индикатор состояния (галочка или крестик)
                WpfBorder indicatorBorder = new WpfBorder
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(2),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Background = isEqualLeaves ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)) : Brushes.LightGray,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Текст внутри индикатора (✓ для включено, ✗ для выключено)
                WpfTextBlock indicatorText = new WpfTextBlock
                {
                    Text = isEqualLeaves ? "✓" : "✗",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                indicatorBorder.Child = indicatorText;

                // Добавляем элементы в панель
                textPanel.Children.Add(textBlock);
                textPanel.Children.Add(indicatorBorder);

                textBorder.Child = textPanel;

                // Добавляем обработчики событий
                textBorder.MouseEnter += (s, e) => {
                    textBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 220, 230, 240));
                    textBorder.BorderBrush = Brushes.Blue;
                    textBorder.BorderThickness = new Thickness(1);
                    StatusText.Text = "Нажмите чтобы изменить параметр 'Равные полотна'";
                };

                textBorder.MouseLeave += (s, e) => {
                    textBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 240, 240, 240));
                    textBorder.BorderBrush = Brushes.Transparent;
                    textBorder.BorderThickness = new Thickness(0);
                    StatusText.Text = "Готово";
                };

                textBorder.MouseLeftButtonUp += (s, e) => {
                    ToggleEqualLeavesParameter();
                };

                // Размещаем текст под изображением двери (по центру)
                double textX = rectX + (rectWidth / 2) - 45;
                double textY = rectY + rectHeight + 55; // Смещаем ниже, чтобы не перекрывать размеры

                Canvas.SetLeft(textBorder, textX);
                Canvas.SetTop(textBorder, textY);

                // Добавляем на канвас
                DiagramCanvas.Children.Add(textBorder);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при добавлении текста 'Равные полотна': {ex.Message}");
            }
        }

        // ========================= МЕТОД ДЛЯ ОТРИСОВКИ РАЗМЕРОВ ПОЛОТЕН =========================
        private void DrawLeafDimension(double x1, double y1, double x2, double y2,
                                      SolidColorBrush color, double valueMm, string tooltip,
                                      Action clickAction, double textX, double textY,
                                      bool isHorizontal = false, bool isClickable = true)
        {
            try
            {
                // Основная линия
                System.Windows.Shapes.Line dimensionLine = new System.Windows.Shapes.Line
                {
                    X1 = x1,
                    Y1 = y1,
                    X2 = x2,
                    Y2 = y2,
                    Stroke = color,
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 2, 2 },
                    Cursor = isClickable ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                    ToolTip = isClickable ? $"{tooltip}: {valueMm:F0} мм\nНажмите для изменения" : $"{tooltip}: {valueMm:F0} мм"
                };

                if (isClickable && clickAction != null)
                {
                    dimensionLine.MouseLeftButtonUp += (s, e) => clickAction();
                }
                DiagramCanvas.Children.Add(dimensionLine);

                // Засечки
                if (isHorizontal)
                {
                    // Горизонтальные засечки (вертикальные линии)
                    System.Windows.Shapes.Line tick1 = new System.Windows.Shapes.Line
                    {
                        X1 = x1,
                        Y1 = y1 - 5,
                        X2 = x1,
                        Y2 = y1 + 5,
                        Stroke = color,
                        StrokeThickness = 1.5,
                        Cursor = isClickable ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow
                    };
                    if (isClickable && clickAction != null)
                    {
                        tick1.MouseLeftButtonUp += (s, e) => clickAction();
                    }
                    DiagramCanvas.Children.Add(tick1);

                    System.Windows.Shapes.Line tick2 = new System.Windows.Shapes.Line
                    {
                        X1 = x2,
                        Y1 = y2 - 5,
                        X2 = x2,
                        Y2 = y2 + 5,
                        Stroke = color,
                        StrokeThickness = 1.5,
                        Cursor = isClickable ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow
                    };
                    if (isClickable && clickAction != null)
                    {
                        tick2.MouseLeftButtonUp += (s, e) => clickAction();
                    }
                    DiagramCanvas.Children.Add(tick2);
                }
                else
                {
                    // Вертикальные засечки (горизонтальные линии)
                    System.Windows.Shapes.Line tick1 = new System.Windows.Shapes.Line
                    {
                        X1 = x1 - 5,
                        Y1 = y1,
                        X2 = x1 + 5,
                        Y2 = y1,
                        Stroke = color,
                        StrokeThickness = 1.5,
                        Cursor = isClickable ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow
                    };
                    if (isClickable && clickAction != null)
                    {
                        tick1.MouseLeftButtonUp += (s, e) => clickAction();
                    }
                    DiagramCanvas.Children.Add(tick1);

                    System.Windows.Shapes.Line tick2 = new System.Windows.Shapes.Line
                    {
                        X1 = x2 - 5,
                        Y1 = y2,
                        X2 = x2 + 5,
                        Y2 = y2,
                        Stroke = color,
                        StrokeThickness = 1.5,
                        Cursor = isClickable ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow
                    };
                    if (isClickable && clickAction != null)
                    {
                        tick2.MouseLeftButtonUp += (s, e) => clickAction();
                    }
                    DiagramCanvas.Children.Add(tick2);
                }

                // Текст с значением
                WpfTextBlock dimensionText = new WpfTextBlock
                {
                    Text = $"{valueMm:F0} мм",
                    Foreground = color,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Background = Brushes.White,
                    Padding = new Thickness(2),
                    Cursor = isClickable ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                    ToolTip = isClickable ? $"{tooltip}: {valueMm:F0} мм\nНажмите для изменения" : $"{tooltip}: {valueMm:F0} мм"
                };

                if (isClickable && clickAction != null)
                {
                    dimensionText.MouseLeftButtonUp += (s, e) => clickAction();
                }

                Canvas.SetLeft(dimensionText, textX);
                Canvas.SetTop(dimensionText, textY);
                DiagramCanvas.Children.Add(dimensionText);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка в DrawLeafDimension: {ex.Message}");
            }
        }
    }
}