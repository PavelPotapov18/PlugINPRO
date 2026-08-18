using Autodesk.Revit.DB;
using PlugINPRO.Services.AR;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Visibility = System.Windows.Visibility;

namespace PlugINPRO.UI.Windows.AR
{
    /// <summary>
    /// Interaction logic for FinishSettingsWindow.xaml
    /// </summary>
    public partial class FinishSettingsWindow : Window
    {
        public FinishWallsViewModel ViewModel { get; }
        public FinishSettingsWindow(List<WallType> wallTypes, List<string> roomParamNames)
        {
            InitializeComponent();
            ViewModel = new FinishWallsViewModel();

            foreach (WallType wt in wallTypes)
                ViewModel.WallTypes.Add(wt);
            foreach (string p in roomParamNames)
                ViewModel.RoomParameterNames.Add(p);

            // Одно пустое правило с одним пустым слоем по умолчанию
            var rule = new WallFinishRule();
            rule.Layers.Add(new FinishLayer());
            ViewModel.Rules.Add(rule);

            DataContext = ViewModel;
        }
        private void AddRule_Click(object sender, RoutedEventArgs e)
        {
            var rule = new WallFinishRule();
            rule.Layers.Add(new FinishLayer());
            ViewModel.Rules.Add(rule);
        }

        private void DeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.Rules.Count > 0)
                ViewModel.Rules.Remove(ViewModel.Rules.Last());
        }

        private void AddLayer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe &&
                fe.Tag is WallFinishRule rule)
            {
                rule.AddLayer();
            }
        }

        private void DeleteLayer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe &&
                fe.Tag is WallFinishRule rule)
            {
                if (rule.Layers.Count <= 1)
                    return;

                rule.RemoveLastLayer();
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        private void EditTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.Focus();
                tb.SelectAll();
            }
        }

        private void SourceWallTypeCombo_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox combo && combo.Tag is WallFinishRule rule)
                combo.SelectedItem = rule.SourceWallType;
        }

        private void SourceWallTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo &&
                combo.Tag is WallFinishRule rule &&
                combo.SelectedItem is WallType wallType)
            {
                rule.SourceWallType = wallType;
            }
        }

        private void FinishWallTypeCombo_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox combo && combo.Tag is FinishLayer layer)
                combo.SelectedItem = layer.FinishWallType;
        }

        private void FinishWallTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo &&
                combo.Tag is FinishLayer layer &&
                combo.SelectedItem is WallType wallType)
            {
                layer.FinishWallType = wallType;
            }
        }

        // Параметр помещения
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

        // Условие
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

        // Кнопки
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
