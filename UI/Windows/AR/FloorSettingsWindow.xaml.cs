using Autodesk.Revit.DB;
using PlugINPRO.Services.AR;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Data;
using System;
using System.Globalization;
using Visibility = System.Windows.Visibility;


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
                MessageBox.Show("Выберите тип пола.", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
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

    // Конвертер bool → Visibility
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType,
            object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
