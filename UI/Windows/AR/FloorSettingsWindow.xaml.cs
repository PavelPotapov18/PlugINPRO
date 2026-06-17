using Autodesk.Revit.DB;
using PlugINPRO.Services.AR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PlugINPRO.UI.Windows.AR
{
    /// <summary>
    /// Interaction logic for FloorSettingsWindow.xaml
    /// </summary>
    public partial class FloorSettingsWindow : Window
    {
        public FloorSettingsViewModel ViewModel { get; }

        public FloorSettingsWindow(List<FloorType> floorTypes)  
        {
            InitializeComponent();
            ViewModel = new FloorSettingsViewModel();
            foreach (FloorType ft in floorTypes)
                ViewModel.FloorTypes.Add(ft);

            if (ViewModel.FloorTypes.Count > 0)
                ViewModel.SelectedFloorType = ViewModel.FloorTypes[0];

            DataContext = ViewModel;
        }
        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedFloorType == null)
            {
                MessageBox.Show(
                    "Выберите тип пола.",
                    "Внимание",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
