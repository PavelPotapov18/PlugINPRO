using System.Windows;
using PlugINPRO.Commands.VK;

namespace PlugINPRO.UI
{
    public partial class HydraulicCalcWindow : Window
    {
        public HydraulicCalcWindow(HydraulicCalcViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}