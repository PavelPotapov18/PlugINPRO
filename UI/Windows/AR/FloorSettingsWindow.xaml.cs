using Autodesk.Revit.DB;
using PlugINPRO.Services.AR;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;



namespace PlugINPRO.UI.Windows.AR
{
    /// <summary>
    /// Interaction logic for FloorSettingsWindow.xaml
    /// </summary>
    public partial class FloorSettingsWindow : Window
    {
        public FloorSettingsViewModel ViewModel { get; }

        public FloorSettingsWindow(List<FloorType> floorTypes, List<string> roomParamNames)  
        {
            InitializeComponent();
            ViewModel = new FloorSettingsViewModel();

            foreach (FloorType ft in floorTypes)
                ViewModel.FloorTypes.Add(ft);

            if (ViewModel.FloorTypes.Count > 0)
                ViewModel.SelectedFloorType = ViewModel.FloorTypes[0];

            foreach (string p in roomParamNames)
                ViewModel.RoomParameterNames.Add(p);

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

        // Фильтр — параметр
        private void FilterParamCombo_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox combo && combo.Tag is RoomFilterRule rule)
                combo.SelectedItem = rule.ParameterName;
        }

        private void FilterParamCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo &&
                combo.Tag is RoomFilterRule rule &&
                combo.SelectedItem is string paramName)
            {
                rule.ParameterName = paramName;
            }
        }

        // Фильтр — условие
        private void FilterConditionCombo_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox combo && combo.Tag is RoomFilterRule rule)
            {
                foreach (ComboBoxItem item in combo.Items)
                {
                    if (item.Tag is RoomFilterCondition c && c == rule.Condition)
                    {
                        combo.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void FilterConditionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo &&
                combo.Tag is RoomFilterRule rule &&
                combo.SelectedItem is ComboBoxItem item &&
                item.Tag is RoomFilterCondition condition)
            {
                rule.Condition = condition;
            }
        }

        // Кнопки фильтра
        private void AddFilterRule_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.FilterRules.Add(new RoomFilterRule());
        }

        private void DeleteFilterRule_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.FilterRules.Count > 0)
                ViewModel.FilterRules.Remove(ViewModel.FilterRules.Last());
        }
    }
}
