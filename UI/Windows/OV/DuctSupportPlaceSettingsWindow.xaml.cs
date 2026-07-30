using System;
using System.Windows;

namespace PlugINPRO.UI.Windows.OV
{
    public partial class DuctSupportPlaceSettingsWindow : Window
    {
        public double OffsetFromStart { get; private set; }
        public bool IsFromStart { get; private set; }
        public double UserOffset { get; private set; }

        private double _initialOffsetFromStart;
        private double _initialOffsetFromEnd;
        private double _totalLength;

        public DuctSupportPlaceSettingsWindow(
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

            txtOffset.Focus();
            txtOffset.SelectAll();
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
                UserOffset = userOffsetMm;

                if (userOffsetMm > _totalLength)
                {
                    MessageBox.Show($"Отступ не может быть больше длины воздуховода ({_totalLength:F0} мм)!",
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
                MessageBox.Show($"Отступ не может быть больше длины воздуховода ({_totalLength:F0} мм)!",
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