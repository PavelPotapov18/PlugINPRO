using System.Windows;

namespace PlugINPRO.Commands.VK.Windows
{
    public partial class PaintInputDialog : Window
    {
        public string PaintName { get; private set; }
        public double ConsumptionPerSquareMeter { get; private set; }

        public PaintInputDialog(double totalArea)
        {
            InitializeComponent();

            // Показываем пользователю рассчитанную площадь
            AreaInfoTextBlock.Text = $"Общая площадь системы: {totalArea:F2} м²";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PaintNameTextBox.Text))
            {
                MessageBox.Show("Введите наименование краски", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(ConsumptionTextBox.Text, out double consumption) || consumption <= 0)
            {
                MessageBox.Show("Введите корректное значение расхода (положительное число)", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PaintName = PaintNameTextBox.Text.Trim();
            ConsumptionPerSquareMeter = consumption;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}