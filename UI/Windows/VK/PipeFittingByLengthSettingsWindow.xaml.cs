using Autodesk.Revit.DB;
using PlugINPRO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace PlugINPRO.UI.Windows.VK
{
    public partial class PipeFittingByLengthSettingsWindow : Window
    {
        public FamilySymbol SelectedFamily { get; private set; }
        public double Spacing { get; private set; }
        public double Offset { get; private set; }
        public bool UsePreviousFamily { get; private set; }

        private Document _document;
        private List<FamilySymbol> _availableFamilies;

        public PipeFittingByLengthSettingsWindow(Document document)
        {
            InitializeComponent();
            _document = document;
            Loaded += Window_Loaded;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Загружаем доступные семейства с фильтром
                LoadAvailableFamiliesWithFilter();

                // Показываем информацию о предыдущем выборе
                UpdatePreviousFamilyInfo();

                // Устанавливаем фокус на поле ввода отступа
                txtOffset.Focus();
                txtOffset.SelectAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки настроек: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadAvailableFamiliesWithFilter()
        {
            try
            {
                // Ищем семейства в категории PIPE FITTINGS
                FilteredElementCollector collector = new FilteredElementCollector(_document)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_PipeFitting)
                    .WhereElementIsElementType();

                _availableFamilies = collector.Cast<FamilySymbol>().ToList();

                // ФИЛЬТР: оставляем только те, в названии которых есть "Крепление" или "Опора"
                _availableFamilies = _availableFamilies
                    .Where(fs =>
                        fs.Family.Name.IndexOf("крепление", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fs.Name.IndexOf("крепление", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fs.Family.Name.IndexOf("опора", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fs.Name.IndexOf("опора", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fs.Family.Name.IndexOf("support", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fs.Name.IndexOf("support", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                // Если нашли семейства
                if (_availableFamilies.Count > 0)
                {
                    // Создаем список для ComboBox
                    var familyItems = _availableFamilies.Select(fs => new
                    {
                        FamilySymbol = fs,
                        DisplayName = GetFamilyDisplayName(fs)
                    }).ToList();

                    cmbFamilies.ItemsSource = familyItems;
                    cmbFamilies.SelectedIndex = 0;
                }
                else
                {
                    tbInfo.Text = "В проекте не найдены семейства креплений для трубопроводов.\n" +
                                 "Загрузите семейства в категории PIPE FITTINGS\n" +
                                 "с названием, содержащим 'Крепление', 'Опора' или 'Support'.";
                    rbSelectNewFamily.IsEnabled = false;
                    rbPreviousFamily.IsChecked = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки семейств: {ex.Message}",
                    "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                _availableFamilies = new List<FamilySymbol>();
            }
        }

        private string GetFamilyDisplayName(FamilySymbol symbol)
        {
            if (symbol == null) return "Неизвестно";

            string name = symbol.Family.Name;
            if (!string.IsNullOrEmpty(symbol.Name) && symbol.Name != symbol.Family.Name)
            {
                name += $" : {symbol.Name}";
            }

            // Обрезаем слишком длинные названия
            if (name.Length > 60)
                name = name.Substring(0, 57) + "...";

            return name;
        }

        private void UpdatePreviousFamilyInfo()
        {
            if (App.SelectedPipeSupportFamily != null && App.SelectedPipeSupportFamily.IsValidObject)
            {
                tbPreviousInfo.Text =
                    $"ПРЕДЫДУЩИЙ ВЫБОР:\n" +
                    $"Семейство: {App.SelectedPipeSupportFamily.Family.Name}\n" +
                    $"Тип: {App.SelectedPipeSupportFamily.Name}\n\n" +
                    $"Можно использовать повторно или выбрать новое.";

                rbPreviousFamily.Content = $"Использовать '{App.SelectedPipeSupportFamily.Family.Name}'";

                // Находим предыдущее семейство в списке
                SelectPreviousFamilyInComboBox();
            }
            else
            {
                tbPreviousInfo.Text = "Ранее выбранное крепление не найдено.\nНеобходимо выбрать новое крепление.";
                rbPreviousFamily.IsEnabled = false;
                rbSelectNewFamily.IsChecked = true;
            }
        }

        private void SelectPreviousFamilyInComboBox()
        {
            if (_availableFamilies == null || App.SelectedPipeSupportFamily == null)
                return;

            // Ищем индекс предыдущего семейства в списке
            for (int i = 0; i < _availableFamilies.Count; i++)
            {
                if (_availableFamilies[i].Id == App.SelectedPipeSupportFamily.Id)
                {
                    cmbFamilies.SelectedIndex = i;
                    break;
                }
            }
        }

        private void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput())
                return;

            try
            {
                // Получаем выбранный шаг
                if (rb1500.IsChecked == true)
                    Spacing = 1500;
                else if (rb2000.IsChecked == true)
                    Spacing = 2000;
                else if (rb2500.IsChecked == true)
                    Spacing = 2500;
                else if (rb3000.IsChecked == true)
                    Spacing = 3000;
                else if (rbCustom.IsChecked == true)
                {
                    if (!double.TryParse(txtCustomSpacing.Text, out double customSpacing))
                    {
                        MessageBox.Show("Введите корректное число для шага!", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    Spacing = customSpacing;
                }

                // Получаем отступ
                if (!double.TryParse(txtOffset.Text, out double offset))
                {
                    MessageBox.Show("Введите корректное число для отступа!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Offset = offset;

                // Получаем выбранное семейство
                if (rbPreviousFamily.IsChecked == true && rbPreviousFamily.IsEnabled)
                {
                    UsePreviousFamily = true;
                    SelectedFamily = App.SelectedPipeSupportFamily;
                }
                else if (rbSelectNewFamily.IsChecked == true)
                {
                    UsePreviousFamily = false;

                    if (cmbFamilies.SelectedItem == null)
                    {
                        MessageBox.Show("Выберите крепление из списка!", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    dynamic selectedItem = cmbFamilies.SelectedItem;
                    SelectedFamily = selectedItem.FamilySymbol;
                }

                // Проверяем валидность семейства
                if (SelectedFamily == null || !SelectedFamily.IsValidObject)
                {
                    MessageBox.Show("Выбранное крепление не доступно!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка применения настроек: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidateInput()
        {
            // Проверка шага
            if (rbCustom.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(txtCustomSpacing.Text))
                {
                    MessageBox.Show("Введите значение шага!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtCustomSpacing.Focus();
                    return false;
                }

                if (!double.TryParse(txtCustomSpacing.Text, out double spacing) || spacing <= 0)
                {
                    MessageBox.Show("Шаг должен быть положительным числом!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtCustomSpacing.Focus();
                    txtCustomSpacing.SelectAll();
                    return false;
                }

                if (spacing > 10000)
                {
                    MessageBox.Show("Слишком большой шаг! Максимум 10000 мм.", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtCustomSpacing.Focus();
                    txtCustomSpacing.SelectAll();
                    return false;
                }
            }

            // Проверка отступа
            if (!double.TryParse(txtOffset.Text, out double offset))
            {
                MessageBox.Show("Отступ должен быть числом!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtOffset.Focus();
                txtOffset.SelectAll();
                return false;
            }

            if (offset < 0)
            {
                MessageBox.Show("Отступ не может быть отрицательным!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtOffset.Focus();
                txtOffset.SelectAll();
                return false;
            }

            // Проверка выбора крепления
            if (rbSelectNewFamily.IsChecked == true &&
                (cmbFamilies.Items.Count == 0 || cmbFamilies.SelectedItem == null))
            {
                MessageBox.Show("Выберите крепление из списка или загрузите семейства!",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Событие для проверки ввода в реальном времени
        private void txtCustomSpacing_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Разрешаем только цифры, точку и запятую
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c) && c != '.' && c != ',')
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        private void txtOffset_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Разрешаем только цифры, точку и запятую
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c) && c != '.' && c != ',')
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        private void txtCustomSpacing_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // Автоматически выбираем радио-кнопку "Другой шаг"
            if (!string.IsNullOrWhiteSpace(txtCustomSpacing.Text))
            {
                rbCustom.IsChecked = true;
            }
        }
    }
}