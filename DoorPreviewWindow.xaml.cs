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

// Алиасы
using Path = System.Windows.Shapes.Path;
using RevitLine = Autodesk.Revit.DB.Line;
using RevitRectangle = Autodesk.Revit.DB.Rectangle;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGrid = System.Windows.Controls.Grid;
using WpfColor = System.Windows.Media.Color;
using RevitColor = Autodesk.Revit.DB.Color;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfBorder = System.Windows.Controls.Border;
using WpfScrollViewer = System.Windows.Controls.ScrollViewer;
using WpfStackPanel = System.Windows.Controls.StackPanel;

namespace PlugINPRO
{
    // 1️⃣ Добавлен enum для типов параметров
    public enum ParamKind
    {
        String,
        Double,
        Integer,
        Material,
        FamilyType,
        ElementId
    }

    // 1️⃣ Обновленный класс с Kind вместо IsMaterial
    public class MaterialParameterInfo
    {
        public string Name { get; set; }
        public Parameter Parameter { get; set; }
        public string DisplayValue { get; set; }
        public ParamKind Kind { get; set; }
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

        // Materials
        private List<MaterialParameterInfo> _materialParameters = new List<MaterialParameterInfo>();
        private Dictionary<string, Dictionary<string, object>> _tempMaterialValues = new Dictionary<string, Dictionary<string, object>>();

        // Construction
        private List<MaterialParameterInfo> _constructionParameters = new List<MaterialParameterInfo>();
        private Dictionary<string, Dictionary<string, object>> _tempConstructionValues = new Dictionary<string, Dictionary<string, object>>();

        // Other
        private List<MaterialParameterInfo> _otherParameters = new List<MaterialParameterInfo>();
        private Dictionary<string, Dictionary<string, object>> _tempOtherValues = new Dictionary<string, Dictionary<string, object>>();

        private List<Material> _allMaterials = new List<Material>();

        public DoorPreviewWindow(string imagePath, Document doc, Element door, UIDocument uidoc, ElementId tempViewId, View previousView)
        {
            InitializeComponent();

            _currentImagePath = imagePath;
            _doc = doc;
            _door = door;
            _uidoc = uidoc;
            _tempViewId = tempViewId;
            _previousView = previousView;

            ElementId typeId = _door.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                _currentType = _doc.GetElement(typeId) as FamilySymbol;
                _selectedType = _currentType;
            }

            LoadImage();
            LoadDoorParameters();
            LoadMaterials();
            LoadFamilyTypes();
            LoadMaterialParameters();
            LoadConstructionParameters();
            LoadOtherParameters();

            UpdateCurrentTypeDisplay();
            DrawDoorDiagram();

            UpdateAndRefreshButton.IsEnabled = false;
            ApplyButton.IsEnabled = false;
        }

        // ========================= ВАШИ ОРИГИНАЛЬНЫЕ МЕТОДЫ =========================
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
                UpdateCurrentTypeDisplay();
                LoadMaterialParameters();
                LoadConstructionParameters();
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

                DrawDoorDiagram();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка обновления параметров: {ex.Message}";
            }
        }

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
                LoadMaterialParameters();
                LoadConstructionParameters();
                LoadOtherParameters();

                StatusText.Text = $"Создан новый тип: {_selectedType.Name}";
                UpdateAndRefreshButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка создания типа: {ex.Message}");
            }
        }

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

                DrawDoorDiagram();

                UpdateAndRefreshButton.IsEnabled = (Math.Abs(_newRoughWidth - _originalRoughWidth) > 0.01 ||
                                                    Math.Abs(_newRoughHeight - _originalRoughHeight) > 0.01 ||
                                                    _selectedType?.Id != _currentType?.Id);
            }
            else
            {
                UpdateAndRefreshButton.IsEnabled = false;
            }
        }

        private void UpdateAndRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isUpdating = true;
                UpdateAndRefreshButton.IsEnabled = false;
                ApplyButton.IsEnabled = false;

                StatusText.Text = "Применение изменений...";

                ApplyChanges();

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

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyChanges();
                StatusText.Text = "Изменения применены успешно!";
                UpdateAndRefreshButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка применения изменений: {ex.Message}");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            try
            {
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

        // 2️⃣ НОВЫЙ МЕТОД: Определение типа параметра
        // 2️⃣ ИСПРАВЛЕННЫЙ МЕТОД: Определение типа параметра
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
                    // Пробуем определить по имени параметра
                    string paramName = param.Definition.Name.ToLower();

                    // Проверяем по ключевым словам для FamilyType
                    string[] familyTypeKeywords = new string[]
                    {
                "тип полотна двери",
                "тип полотна",
                "panel type",
                "door panel type",
                "тип панели",
                "тип створки",
                "leaf type"
                    };

                    if (familyTypeKeywords.Any(keyword => paramName.Contains(keyword)))
                    {
                        return ParamKind.FamilyType;
                    }

                    // Проверяем по ключевым словам для Material
                    string[] materialKeywords = new string[]
                    {
                "материал",
                "material",
                "отделка",
                "finish"
                    };

                    if (materialKeywords.Any(keyword => paramName.Contains(keyword)))
                    {
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
                                    return ParamKind.Material;

                                if (elem is ElementType)
                                    return ParamKind.FamilyType;
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
                        MaterialParameterInfo info = new MaterialParameterInfo
                        {
                            Name = param.Definition.Name,
                            Parameter = param,
                            Kind = GetParameterKind(param) // 3️⃣ Используем новый метод
                        };

                        // Получаем отображаемое значение
                        if (param.StorageType == StorageType.ElementId && param.HasValue)
                        {
                            ElementId id = param.AsElementId();
                            if (id != null && id != ElementId.InvalidElementId)
                            {
                                Element elem = _doc.GetElement(id);
                                if (elem != null)
                                    info.DisplayValue = elem.Name;
                                else
                                    info.DisplayValue = "<Элемент не найден>";
                            }
                            else
                            {
                                info.DisplayValue = "<Не задано>";
                            }
                        }
                        else if (param.StorageType == StorageType.String && param.HasValue)
                        {
                            info.DisplayValue = param.AsString();
                        }
                        else if (param.StorageType == StorageType.Integer && param.HasValue)
                        {
                            info.DisplayValue = param.AsInteger().ToString();
                        }
                        else if (param.StorageType == StorageType.Double && param.HasValue)
                        {
                            double valueInFeet = param.AsDouble();
                            if (IsLinearDimension(param))
                            {
                                double valueInMm = valueInFeet * 304.8;
                                info.DisplayValue = Math.Round(valueInMm, 0).ToString();
                            }
                            else
                            {
                                info.DisplayValue = valueInFeet.ToString("F2");
                            }
                        }
                        else
                        {
                            info.DisplayValue = "";
                        }

                        _materialParameters.Add(info);
                    }
                }

                _materialParameters = _materialParameters.OrderBy(p => p.Name).ToList();
                DisplayMaterialParameters();

                if (_materialParameters.Count > 0)
                    StatusText.Text = $"Найдено параметров материалов: {_materialParameters.Count}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка загрузки материалов: {ex.Message}";
            }
        }

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
                        MaterialParameterInfo info = new MaterialParameterInfo
                        {
                            Name = param.Definition.Name,
                            Parameter = param,
                            Kind = GetParameterKind(param) // 3️⃣ Используем новый метод
                        };

                        // Получаем отображаемое значение
                        if (param.StorageType == StorageType.ElementId && param.HasValue)
                        {
                            ElementId id = param.AsElementId();
                            if (id != null && id != ElementId.InvalidElementId)
                            {
                                Element elem = _doc.GetElement(id);
                                if (elem != null)
                                    info.DisplayValue = elem.Name;
                                else
                                    info.DisplayValue = "<Элемент не найден>";
                            }
                            else
                            {
                                info.DisplayValue = "<Не задано>";
                            }
                        }
                        else if (param.StorageType == StorageType.String && param.HasValue)
                        {
                            info.DisplayValue = param.AsString();
                        }
                        else if (param.StorageType == StorageType.Integer && param.HasValue)
                        {
                            info.DisplayValue = param.AsInteger().ToString();
                        }
                        else if (param.StorageType == StorageType.Double && param.HasValue)
                        {
                            double valueInFeet = param.AsDouble();
                            if (IsLinearDimension(param))
                            {
                                double valueInMm = valueInFeet * 304.8;
                                info.DisplayValue = Math.Round(valueInMm, 0).ToString();
                            }
                            else
                            {
                                info.DisplayValue = valueInFeet.ToString("F2");
                            }
                        }
                        else
                        {
                            info.DisplayValue = "";
                        }

                        _constructionParameters.Add(info);
                    }
                }

                _constructionParameters = _constructionParameters.OrderBy(p => p.Name).ToList();
                DisplayConstructionParameters();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка загрузки Construction: {ex.Message}";
            }
        }

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

        // ========================= OTHER =========================
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
                        MaterialParameterInfo info = new MaterialParameterInfo
                        {
                            Name = param.Definition.Name,
                            Parameter = param,
                            Kind = GetParameterKind(param) // 3️⃣ Используем новый метод
                        };

                        // Получаем отображаемое значение
                        if (param.StorageType == StorageType.ElementId && param.HasValue)
                        {
                            ElementId id = param.AsElementId();
                            if (id != null && id != ElementId.InvalidElementId)
                            {
                                Element elem = _doc.GetElement(id);
                                if (elem != null)
                                    info.DisplayValue = elem.Name;
                                else
                                    info.DisplayValue = "<Элемент не найден>";
                            }
                            else
                            {
                                info.DisplayValue = "<Не задано>";
                            }
                        }
                        else if (param.StorageType == StorageType.String && param.HasValue)
                        {
                            info.DisplayValue = param.AsString();
                        }
                        else if (param.StorageType == StorageType.Integer && param.HasValue)
                        {
                            info.DisplayValue = param.AsInteger().ToString();
                        }
                        else if (param.StorageType == StorageType.Double && param.HasValue)
                        {
                            double valueInFeet = param.AsDouble();
                            if (IsLinearDimension(param))
                            {
                                double valueInMm = valueInFeet * 304.8;
                                info.DisplayValue = Math.Round(valueInMm, 0).ToString();
                            }
                            else
                            {
                                info.DisplayValue = valueInFeet.ToString("F2");
                            }
                        }
                        else
                        {
                            info.DisplayValue = "";
                        }

                        _otherParameters.Add(info);
                    }
                }

                _otherParameters = _otherParameters.OrderBy(p => p.Name).ToList();
                DisplayOtherParameters();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка загрузки Other: {ex.Message}";
            }
        }

        private bool IsOtherParameter(Parameter param)
        {
            if (param == null || param.Definition == null) return false;

            try
            {
                ForgeTypeId groupTypeId = param.Definition.GetGroupTypeId();
                return groupTypeId != GroupTypeId.Materials && groupTypeId != GroupTypeId.Construction;
            }
            catch
            {
                return true;
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

        // ========================= ОБЩИЕ МЕТОДЫ =========================
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

        // 4️⃣ ОБНОВЛЕННЫЙ МЕТОД: Создание контрола с учетом Kind
        private void CreateParameterControl(MaterialParameterInfo paramInfo, WpfStackPanel targetPanel, Dictionary<string, Dictionary<string, object>> tempStorage)
        {
            WpfBorder paramBorder = new WpfBorder
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(5),
                Background = new SolidColorBrush(WpfColor.FromRgb(245, 245, 245)),
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
                    nameText.Text += " (тип)";
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
                // 4️⃣ Material - ComboBox со списком материалов
                WpfComboBox materialCombo = new WpfComboBox
                {
                    Height = 30,
                    DisplayMemberPath = "Name",
                    Tag = new Tuple<MaterialParameterInfo, Dictionary<string, Dictionary<string, object>>>(paramInfo, tempStorage)
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
                paramGrid.Children.Add(materialCombo);
            }
            else if (paramInfo.Kind == ParamKind.FamilyType)
            {
                // 4️⃣ FamilyType - ComboBox со списком типоразмеров текущего семейства
                WpfComboBox familyTypeCombo = new WpfComboBox
                {
                    Height = 30,
                    DisplayMemberPath = "Name",
                    Tag = new Tuple<MaterialParameterInfo, Dictionary<string, Dictionary<string, object>>>(paramInfo, tempStorage)
                };

                // Получаем все типы текущего семейства
                var familyTypes = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(f => f.Family != null && f.Family.Id == _currentType.Family.Id)
                    .OrderBy(f => f.Name)
                    .ToList();

                var typeItems = new List<FamilyTypeItem>();
                typeItems.Add(new FamilyTypeItem { Id = ElementId.InvalidElementId, Name = "<Не задано>" });

                foreach (var ft in familyTypes)
                {
                    typeItems.Add(new FamilyTypeItem
                    {
                        Id = ft.Id,
                        Name = ft.Name,
                        FamilyName = ft.Family?.Name ?? ""
                    });
                }

                familyTypeCombo.ItemsSource = typeItems;

                if (paramInfo.Parameter.HasValue)
                {
                    ElementId currentId = paramInfo.Parameter.AsElementId();
                    var selectedItem = typeItems.FirstOrDefault(t => t.Id == currentId);
                    if (selectedItem != null)
                        familyTypeCombo.SelectedItem = selectedItem;
                }

                familyTypeCombo.SelectionChanged += FamilyTypeCombo_SelectionChanged;
                WpfGrid.SetRow(familyTypeCombo, 1);
                paramGrid.Children.Add(familyTypeCombo);
            }
            else
            {
                // String, Double, Integer, ElementId - TextBox
                WpfTextBox paramTextBox = new WpfTextBox
                {
                    Height = 30,
                    Padding = new Thickness(5),
                    Text = paramInfo.DisplayValue ?? "",
                    Tag = new Tuple<MaterialParameterInfo, Dictionary<string, Dictionary<string, object>>>(paramInfo, tempStorage)
                };

                if (paramInfo.Kind == ParamKind.Double && IsLinearDimension(paramInfo.Parameter))
                {
                    paramTextBox.ToolTip = "Введите значение в миллиметрах";
                }

                paramTextBox.TextChanged += ParameterTextBox_TextChanged;
                WpfGrid.SetRow(paramTextBox, 1);
                paramGrid.Children.Add(paramTextBox);
            }

            paramBorder.Child = paramGrid;
            targetPanel.Children.Add(paramBorder);
        }

        // НОВЫЙ обработчик для FamilyType
        private void FamilyTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdating) return;

            WpfComboBox combo = sender as WpfComboBox;
            if (combo == null) return;

            var tag = combo.Tag as Tuple<MaterialParameterInfo, Dictionary<string, Dictionary<string, object>>>;
            if (tag == null) return;

            MaterialParameterInfo paramInfo = tag.Item1;
            var tempStorage = tag.Item2;

            FamilyTypeItem selectedItem = combo.SelectedItem as FamilyTypeItem;
            if (selectedItem == null) return;

            if (!tempStorage.ContainsKey(paramInfo.Name))
            {
                tempStorage[paramInfo.Name] = new Dictionary<string, object>();
            }

            tempStorage[paramInfo.Name]["Value"] = selectedItem.Id;
            tempStorage[paramInfo.Name]["Kind"] = ParamKind.FamilyType;

            UpdateAndRefreshButton.IsEnabled = true;
        }

        private void MaterialCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdating) return;

            WpfComboBox combo = sender as WpfComboBox;
            if (combo == null) return;

            var tag = combo.Tag as Tuple<MaterialParameterInfo, Dictionary<string, Dictionary<string, object>>>;
            if (tag == null) return;

            MaterialParameterInfo paramInfo = tag.Item1;
            var tempStorage = tag.Item2;

            MaterialItem selectedItem = combo.SelectedItem as MaterialItem;
            if (selectedItem == null) return;

            if (!tempStorage.ContainsKey(paramInfo.Name))
            {
                tempStorage[paramInfo.Name] = new Dictionary<string, object>();
            }

            tempStorage[paramInfo.Name]["Value"] = selectedItem.Id;
            tempStorage[paramInfo.Name]["Kind"] = ParamKind.Material;

            UpdateAndRefreshButton.IsEnabled = true;
        }

        private void ParameterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;

            WpfTextBox textBox = sender as WpfTextBox;
            if (textBox == null) return;

            var tag = textBox.Tag as Tuple<MaterialParameterInfo, Dictionary<string, Dictionary<string, object>>>;
            if (tag == null) return;

            MaterialParameterInfo paramInfo = tag.Item1;
            var tempStorage = tag.Item2;

            if (!tempStorage.ContainsKey(paramInfo.Name))
            {
                tempStorage[paramInfo.Name] = new Dictionary<string, object>();
            }

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
                    tempStorage[paramInfo.Name]["Value"] = doubleValue;
                }
            }
            else if (paramInfo.Kind == ParamKind.Integer)
            {
                if (int.TryParse(textBox.Text, out int intValue))
                {
                    tempStorage[paramInfo.Name]["Value"] = intValue;
                }
            }
            else
            {
                tempStorage[paramInfo.Name]["Value"] = textBox.Text;
            }

            tempStorage[paramInfo.Name]["Kind"] = paramInfo.Kind;

            UpdateAndRefreshButton.IsEnabled = true;
        }

        private void SaveMaterialParameters()
        {
            SaveParameters(_tempMaterialValues, _materialParameters);
        }

        private void SaveConstructionParameters()
        {
            SaveParameters(_tempConstructionValues, _constructionParameters);
        }

        private void SaveOtherParameters()
        {
            SaveParameters(_tempOtherValues, _otherParameters);
        }

        // 5️⃣ ОБНОВЛЕННЫЙ МЕТОД: Сохранение с учетом Kind
        private void SaveParameters(Dictionary<string, Dictionary<string, object>> tempStorage, List<MaterialParameterInfo> paramList)
        {
            try
            {
                foreach (var tempValue in tempStorage)
                {
                    string paramName = tempValue.Key;
                    object value = tempValue.Value["Value"];
                    ParamKind kind = (ParamKind)tempValue.Value["Kind"];

                    var paramInfo = paramList.FirstOrDefault(p => p.Name == paramName);
                    if (paramInfo != null && !paramInfo.Parameter.IsReadOnly)
                    {
                        if (kind == ParamKind.Material || kind == ParamKind.FamilyType || kind == ParamKind.ElementId)
                        {
                            if (value is ElementId)
                            {
                                paramInfo.Parameter.Set((ElementId)value);
                            }
                        }
                        else if (kind == ParamKind.String && value is string)
                        {
                            paramInfo.Parameter.Set((string)value);
                        }
                        else if (kind == ParamKind.Integer && value is int)
                        {
                            paramInfo.Parameter.Set((int)value);
                        }
                        else if (kind == ParamKind.Double && value is double)
                        {
                            paramInfo.Parameter.Set((double)value);
                        }
                    }
                }

                tempStorage.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения параметров: {ex.Message}");
            }
        }

        private void ApplyChanges()
        {
            bool widthUpdated = false;
            bool heightUpdated = false;

            using (Transaction tx = new Transaction(_doc, "Изменение параметров двери"))
            {
                tx.Start();

                if (_selectedType != null && _door.GetTypeId() != _selectedType.Id)
                {
                    _door.ChangeTypeId(_selectedType.Id);
                    _currentType = _selectedType;

                    _parameters.Clear();
                    LoadTypeParameter(ROUGH_WIDTH_KEY, WidthTextBox, _roughWidthParam);
                    LoadTypeParameter(ROUGH_HEIGHT_KEY, HeightTextBox, _roughHeightParam);

                    LoadMaterialParameters();
                    LoadConstructionParameters();
                    LoadOtherParameters();
                }

                if (WidthTextBox != null)
                {
                    widthUpdated = SaveTypeParameter(ROUGH_WIDTH_KEY, WidthTextBox, _roughWidthParam, true);
                }

                if (HeightTextBox != null)
                {
                    heightUpdated = SaveTypeParameter(ROUGH_HEIGHT_KEY, HeightTextBox, _roughHeightParam, true);
                }

                SaveMaterialParameters();
                SaveConstructionParameters();
                SaveOtherParameters();

                tx.Commit();
            }

            _originalRoughWidth = _newRoughWidth;
            _originalRoughHeight = _newRoughHeight;
            ApplyButton.IsEnabled = false;

            UpdateCurrentTypeDisplay();

            if (widthUpdated || heightUpdated)
            {
                StatusText.Text = "Параметры двери обновлены!";
            }
            else
            {
                StatusText.Text = "Не удалось обновить параметры";
            }
        }

        private void DrawDoorDiagram()
        {
            if (DiagramCanvas == null) return;

            DiagramCanvas.Children.Clear();

            DiagramCanvas.Width = 300;
            DiagramCanvas.Height = 400;

            double widthMm = _newRoughWidth;
            double heightMm = _newRoughHeight;

            if (widthMm <= 0 || heightMm <= 0)
            {
                WpfTextBlock errorText = new WpfTextBlock
                {
                    Text = "Некорректные размеры",
                    Foreground = Brushes.Red,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Arrow
                };
                Canvas.SetLeft(errorText, DiagramCanvas.Width / 2 - 50);
                Canvas.SetTop(errorText, DiagramCanvas.Height / 2);
                DiagramCanvas.Children.Add(errorText);
                return;
            }

            double maxWidth = DiagramCanvas.Width * 0.7;
            double maxHeight = DiagramCanvas.Height * 0.7;

            double scale = Math.Min(maxWidth / widthMm, maxHeight / heightMm) * 0.9;

            double rectWidth = widthMm * scale;
            double rectHeight = heightMm * scale;

            double rectX = (DiagramCanvas.Width - rectWidth) / 2;
            double rectY = (DiagramCanvas.Height - rectHeight) / 2;

            WpfRectangle doorRect = new WpfRectangle
            {
                Width = rectWidth,
                Height = rectHeight,
                Stroke = Brushes.Black,
                StrokeThickness = 2,
                Fill = Brushes.LightBlue,
                Opacity = 0.6,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Дверной проем"
            };

            ToolTipService.SetToolTip(doorRect, $"Ширина: {widthMm:F0} мм\nВысота: {heightMm:F0} мм");

            Canvas.SetLeft(doorRect, rectX);
            Canvas.SetTop(doorRect, rectY);
            DiagramCanvas.Children.Add(doorRect);

            double fontSize = 12;
            double offset = 5;

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
            widthLine1.MouseLeftButtonUp += (s, ev) => {
                WidthTextBox.Focus();
                WidthTextBox.SelectAll();
                StatusText.Text = "Редактирование ширины двери";
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
            widthLine2.MouseLeftButtonUp += (s, ev) => {
                WidthTextBox.Focus();
                WidthTextBox.SelectAll();
                StatusText.Text = "Редактирование ширины двери";
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
            widthLine3.MouseLeftButtonUp += (s, ev) => {
                WidthTextBox.Focus();
                WidthTextBox.SelectAll();
                StatusText.Text = "Редактирование ширины двери";
            };

            DiagramCanvas.Children.Add(widthLine1);
            DiagramCanvas.Children.Add(widthLine2);
            DiagramCanvas.Children.Add(widthLine3);

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
            widthText.MouseLeftButtonUp += (s, ev) => {
                WidthTextBox.Focus();
                WidthTextBox.SelectAll();
                StatusText.Text = "Редактирование ширины двери";
            };

            Canvas.SetLeft(widthText, rectX + (rectWidth / 2) - 25);
            Canvas.SetTop(widthText, rectY + rectHeight + offset + 5);
            DiagramCanvas.Children.Add(widthText);

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
            heightLine1.MouseLeftButtonUp += (s, ev) => {
                HeightTextBox.Focus();
                HeightTextBox.SelectAll();
                StatusText.Text = "Редактирование высоты двери";
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
            heightLine2.MouseLeftButtonUp += (s, ev) => {
                HeightTextBox.Focus();
                HeightTextBox.SelectAll();
                StatusText.Text = "Редактирование высоты двери";
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
            heightLine3.MouseLeftButtonUp += (s, ev) => {
                HeightTextBox.Focus();
                HeightTextBox.SelectAll();
                StatusText.Text = "Редактирование высоты двери";
            };

            DiagramCanvas.Children.Add(heightLine1);
            DiagramCanvas.Children.Add(heightLine2);
            DiagramCanvas.Children.Add(heightLine3);

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
            heightText.MouseLeftButtonUp += (s, ev) => {
                HeightTextBox.Focus();
                HeightTextBox.SelectAll();
                StatusText.Text = "Редактирование высоты двери";
            };

            Canvas.SetLeft(heightText, rectX - offset - 60);
            Canvas.SetTop(heightText, rectY + (rectHeight / 2) - 10);
            DiagramCanvas.Children.Add(heightText);

            StatusText.Text = "Кликните на размер на схеме для быстрого редактирования";
        }
    }

    public class MaterialItem
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }
    }

    public class FamilyTypeItem
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }
        public string FamilyName { get; set; }

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(FamilyName))
                return $"{FamilyName} : {Name}";
            return Name;
        }
    }
}