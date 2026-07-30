using System;
using System.Windows;

namespace PlugINPRO.UI.Windows.VK
{
    public partial class PipeSupportPlaceSettingsWindow : Window
    {
        public double OffsetFromStart { get; private set; } // Отступ от НАЧАЛА вдоль трубы (в мм, с учетом уклона)
        public bool IsFromStart { get; private set; }
        public bool ProjectToHorizontal { get; private set; }
        public double UserOffset { get; private set; } // То что ввел пользователь (в мм)

        private double _initialOffsetFromStart;
        private double _initialOffsetFromEnd;
        private double _totalLength;

        public PipeSupportPlaceSettingsWindow(
            double initialOffsetFromStart,
            double initialOffsetFromEnd,
            double totalLength)
        {
            InitializeComponent();

            _initialOffsetFromStart = initialOffsetFromStart;
            _initialOffsetFromEnd = initialOffsetFromEnd;
            _totalLength = totalLength;

            Loaded += Window_Loaded;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            tbDistanceFromStart.Text = $"От начала: {_initialOffsetFromStart:F0} мм";
            tbDistanceFromEnd.Text = $"От конца: {_initialOffsetFromEnd:F0} мм";
            tbTotalLength.Text = $"Общая длина: {_totalLength:F0} мм";

            txtOffset.Text = _initialOffsetFromStart.ToString("F0");

            chkProjectToHorizontal.Checked += ChkProjectToHorizontal_Changed;
            chkProjectToHorizontal.Unchecked += ChkProjectToHorizontal_Changed;
            UpdateProjectionInfo();

            txtOffset.Focus();
            txtOffset.SelectAll();
        }

        private void ChkProjectToHorizontal_Changed(object sender, RoutedEventArgs e)
        {
            UpdateProjectionInfo();
        }

        private void UpdateProjectionInfo()
        {
            if (chkProjectToHorizontal.IsChecked == true)
            {
                tbProjectionInfo.Text = "✓ Включено: отступ по горизонтали (с учетом уклона трубы)";
                tbProjectionInfo.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                tbProjectionInfo.Text = "✗ Выключено: отступ по оси трубы (без учета уклона)";
                tbProjectionInfo.Foreground = System.Windows.Media.Brushes.DarkOrange;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput())
                return;

            try
            {
                if (!double.TryParse(txtOffset.Text, out double userOffsetMm))
                {
                    MessageBox.Show("Введите корректное число для отступа!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                IsFromStart = rbFromStart.IsChecked == true;
                ProjectToHorizontal = chkProjectToHorizontal.IsChecked == true;
                UserOffset = userOffsetMm;

                // ═══════════════════════════════════════════════════════════
                // 🔥 СОХРАНЯЕМ СЫРЫЕ ДАННЫЕ, КОРРЕКЦИЯ БУДЕТ В КОМАНДЕ
                // ═══════════════════════════════════════════════════════════
                // Просто проверяем, что отступ не выходит за пределы
                // (проверка в сырых мм, без учета уклона)
                double maxOffset = _totalLength;
                if (userOffsetMm > maxOffset)
                {
                    MessageBox.Show($"Отступ не может быть больше длины трубы ({_totalLength:F0} мм)!",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
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
                MessageBox.Show("Введите отступ!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtOffset.Focus();
                return false;
            }

            if (!double.TryParse(txtOffset.Text, out double offset))
            {
                MessageBox.Show("Введите корректное число!", "Ошибка",
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

            if (offset > _totalLength)
            {
                MessageBox.Show($"Отступ не может быть больше длины трубы ({_totalLength:F0} мм)!",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtOffset.Focus();
                txtOffset.SelectAll();
                return false;
            }

            return true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void txtOffset_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c) && c != '.' && c != ',')
                {
                    e.Handled = true;
                    return;
                }
            }
        }
    }
}