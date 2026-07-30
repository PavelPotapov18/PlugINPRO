using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using System;
using System.Windows;
using System.Windows.Input;

namespace PlugINPRO.UI.Windows.VK
{
    public partial class PipeSupportCopySettingsWindow : Window
    {
        public double Offset { get; private set; }
        public int Count { get; private set; }
        public double Spacing { get; private set; }
        public bool CopyMultiple { get; private set; }
        public bool ProjectToHorizontal { get; private set; }
        public FamilyInstance SelectedSupport { get; private set; }
        public Pipe SelectedPipe { get; private set; }
        private double _slope;
        private bool _isVertical;

        public PipeSupportCopySettingsWindow(FamilyInstance support, Pipe pipe, double slope, bool isVertical)
        {
            InitializeComponent();
            SelectedSupport = support;
            SelectedPipe = pipe;
            _slope = slope;
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

                if (SelectedPipe != null)
                {
                    double diameter = GetPipeDiameter(SelectedPipe);
                    string pipeInfo = $"Труба: Ø{diameter:F0} мм";

                    LocationCurve locationCurve = SelectedPipe.Location as LocationCurve;
                    if (locationCurve != null)
                    {
                        double length = UnitUtils.ConvertFromInternalUnits(locationCurve.Curve.Length, UnitTypeId.Millimeters);
                        pipeInfo += $", Длина: {length:F0} мм";
                    }

                    if (_isVertical)
                    {
                        pipeInfo += " (ВЕРТИКАЛЬНАЯ)";
                    }

                    tbPipeInfo.Text = pipeInfo;
                }
                else
                {
                    tbPipeInfo.Text = "Ошибка: труба не найдена!";
                    btnCopy.IsEnabled = false;
                }

                if (_isVertical)
                {
                    tbSlopeInfo.Text = "✓ Вертикальная труба (уклон не применяется)";
                    tbSlopeInfo.Foreground = System.Windows.Media.Brushes.DarkBlue;
                }
                else if (_slope != 0)
                {
                    double slopePercent = _slope * 100;
                    double slopeAngleDeg = Math.Atan(_slope) * 180 / Math.PI;
                    tbSlopeInfo.Text = $"⚠ Уклон трубы: {slopePercent:F1}% (угол {slopeAngleDeg:F1}°)\n" +
                                      $"Смещение по горизонтали будет пересчитано с учетом уклона";
                    tbSlopeInfo.Foreground = System.Windows.Media.Brushes.DarkOrange;
                }
                else
                {
                    tbSlopeInfo.Text = "✓ Горизонтальная труба (уклон 0%)";
                    tbSlopeInfo.Foreground = System.Windows.Media.Brushes.Green;
                }

                chkProjectToHorizontal.Checked += ChkProjectToHorizontal_Changed;
                chkProjectToHorizontal.Unchecked += ChkProjectToHorizontal_Changed;
                UpdateProjectionInfo();

                txtOffset.Focus();
                txtOffset.SelectAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ChkProjectToHorizontal_Changed(object sender, RoutedEventArgs e)
        {
            UpdateProjectionInfo();
        }

        private void UpdateProjectionInfo()
        {
            if (chkProjectToHorizontal.IsChecked == true)
            {
                tbProjectionInfo.Text = "✓ Включено: смещение по горизонтали (с учетом уклона трубы)";
                tbProjectionInfo.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                tbProjectionInfo.Text = "✗ Выключено: смещение по оси трубы (без учета уклона)";
                tbProjectionInfo.Foreground = System.Windows.Media.Brushes.DarkOrange;
            }
        }

        private double GetPipeDiameter(Pipe pipe)
        {
            try
            {
                Parameter diameterParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (diameterParam != null && diameterParam.HasValue)
                {
                    return UnitUtils.ConvertFromInternalUnits(diameterParam.AsDouble(), UnitTypeId.Millimeters);
                }

                diameterParam = pipe.LookupParameter("Диаметр");
                if (diameterParam != null && diameterParam.HasValue)
                {
                    if (diameterParam.StorageType == StorageType.Double)
                        return UnitUtils.ConvertFromInternalUnits(diameterParam.AsDouble(), UnitTypeId.Millimeters);
                    else if (diameterParam.StorageType == StorageType.String)
                    {
                        string stringValue = diameterParam.AsString();
                        if (!string.IsNullOrEmpty(stringValue) && double.TryParse(stringValue, out double result))
                            return result;
                    }
                }
                return 50.0;
            }
            catch
            {
                return 50.0;
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
                tbInfo.Text = "Будет создана одна копия опоры с указанным смещением вдоль трассы трубы";
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

                ProjectToHorizontal = chkProjectToHorizontal.IsChecked == true;

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