using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using System;
using System.Windows;
using System.Windows.Input;

namespace PlugINPRO.UI.Windows.ES
{
    public partial class CableTraySupportCopySettingsWindow : Window
    {
        public double Offset { get; private set; }
        public int Count { get; private set; }
        public double Spacing { get; private set; }
        public bool CopyMultiple { get; private set; }
        public FamilyInstance SelectedSupport { get; private set; }
        public CableTray SelectedTray { get; private set; }
        private bool _isVertical;

        public CableTraySupportCopySettingsWindow(FamilyInstance support, CableTray tray, bool isVertical)
        {
            InitializeComponent();
            SelectedSupport = support;
            SelectedTray = tray;
            _isVertical = isVertical;

            chkCopyMultiple.IsChecked = false;
            txtCount.IsEnabled = false;
            txtSpacing.IsEnabled = false;

            Loaded += Window_Loaded;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string supportName = SelectedSupport?.Symbol?.Family?.Name ?? "Неизвестно";
                if (SelectedSupport?.Symbol != null && !string.IsNullOrEmpty(SelectedSupport.Symbol.Name))
                {
                    supportName += $" : {SelectedSupport.Symbol.Name}";
                }
                tbSupportInfo.Text = $"Выбрана опора: {supportName}";

                if (SelectedTray != null)
                {
                    string trayInfo = GetTrayInfo(SelectedTray);
                    tbTrayInfo.Text = trayInfo;
                }
                else
                {
                    tbTrayInfo.Text = "Ошибка: лоток не найден!";
                    btnCopy.IsEnabled = false;
                }

                txtOffset.Focus();
                txtOffset.SelectAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetTrayInfo(CableTray tray)
        {
            try
            {
                double width = 0, height = 0;

                Parameter widthParam = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);
                Parameter heightParam = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);

                if (widthParam != null && widthParam.HasValue)
                    width = UnitUtils.ConvertFromInternalUnits(widthParam.AsDouble(), UnitTypeId.Millimeters);
                if (heightParam != null && heightParam.HasValue)
                    height = UnitUtils.ConvertFromInternalUnits(heightParam.AsDouble(), UnitTypeId.Millimeters);

                LocationCurve locationCurve = tray.Location as LocationCurve;
                double length = 0;
                if (locationCurve != null)
                {
                    length = UnitUtils.ConvertFromInternalUnits(locationCurve.Curve.Length, UnitTypeId.Millimeters);
                }

                string sizeInfo = $"{width:F0}×{height:F0} мм";
                string verticalInfo = _isVertical ? " (ВЕРТИКАЛЬНЫЙ)" : "";

                return $"Лоток: {sizeInfo}, Длина: {length:F0} мм{verticalInfo}";
            }
            catch
            {
                return "Лоток";
            }
        }

        private void ChkCopyMultiple_Checked(object sender, RoutedEventArgs e)
        {
            txtCount.IsEnabled = true;
            txtSpacing.IsEnabled = true;
            CopyMultiple = true;
            UpdateInfoText();
        }

        private void ChkCopyMultiple_Unchecked(object sender, RoutedEventArgs e)
        {
            txtCount.IsEnabled = false;
            txtSpacing.IsEnabled = false;
            CopyMultiple = false;
            UpdateInfoText();
        }

        private void UpdateInfoText()
        {
            if (CopyMultiple)
            {
                tbInfo.Text = "Будет создано несколько копий опоры с указанным смещением и шагом";
            }
            else
            {
                tbInfo.Text = "Будет создана одна копия опоры с указанным смещением вдоль трассы лотка";
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput())
                return;

            try
            {
                if (!double.TryParse(txtOffset.Text, out double offset))
                {
                    MessageBox.Show("Введите корректное число для смещения!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Offset = offset;

                Count = 1;
                Spacing = 0;

                if (CopyMultiple)
                {
                    if (!int.TryParse(txtCount.Text, out int count) || count < 2)
                    {
                        MessageBox.Show("Введите корректное количество опор (минимум 2)!", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    Count = count;

                    if (!double.TryParse(txtSpacing.Text, out double spacing) || spacing == 0)
                    {
                        MessageBox.Show("Введите корректный шаг между опорами (не может быть 0)!", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    Spacing = spacing;
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(txtOffset.Text))
            {
                MessageBox.Show("Введите смещение!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtOffset.Focus();
                return false;
            }

            if (!double.TryParse(txtOffset.Text, out double offset))
            {
                MessageBox.Show("Введите корректное число для смещения!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtOffset.Focus();
                txtOffset.SelectAll();
                return false;
            }

            if (Math.Abs(offset) < 1)
            {
                MessageBox.Show("Смещение должно быть больше 1 мм!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtOffset.Focus();
                txtOffset.SelectAll();
                return false;
            }

            if (Math.Abs(offset) > 50000)
            {
                MessageBox.Show("Слишком большое смещение! Максимум 50000 мм.", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtOffset.Focus();
                txtOffset.SelectAll();
                return false;
            }

            if (chkCopyMultiple.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(txtCount.Text))
                {
                    MessageBox.Show("Введите количество опор!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtCount.Focus();
                    return false;
                }

                if (!int.TryParse(txtCount.Text, out int count) || count < 2)
                {
                    MessageBox.Show("Количество опор должно быть целым числом >= 2!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtCount.Focus();
                    txtCount.SelectAll();
                    return false;
                }

                if (count > 100)
                {
                    MessageBox.Show("Слишком много опор! Максимум 100.", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtCount.Focus();
                    txtCount.SelectAll();
                    return false;
                }

                if (string.IsNullOrWhiteSpace(txtSpacing.Text))
                {
                    MessageBox.Show("Введите шаг между опорами!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtSpacing.Focus();
                    return false;
                }

                if (!double.TryParse(txtSpacing.Text, out double spacing) || spacing == 0)
                {
                    MessageBox.Show("Шаг должен быть числом не равным 0!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtSpacing.Focus();
                    txtSpacing.SelectAll();
                    return false;
                }

                if (Math.Abs(spacing) < 50)
                {
                    MessageBox.Show("Слишком маленький шаг! Минимум 50 мм.", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtSpacing.Focus();
                    txtSpacing.SelectAll();
                    return false;
                }

                if (Math.Abs(spacing) > 10000)
                {
                    MessageBox.Show("Слишком большой шаг! Максимум 10000 мм.", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtSpacing.Focus();
                    txtSpacing.SelectAll();
                    return false;
                }
            }

            return true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void txtOffset_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c) && c != '.' && c != ',' && c != '-')
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        private void txtCount_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        private void txtSpacing_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c) && c != '.' && c != ',' && c != '-')
                {
                    e.Handled = true;
                    return;
                }
            }
        }
    }
}