using Autodesk.Revit.DB;
using PlugINPRO.Services.AR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PlugINPRO.UI.Windows.AR
{
    /// <summary>
    /// Interaction logic for FinishSettingsWindow.xaml
    /// </summary>
    public partial class FinishSettingsWindow : Window
    {
        public FinishWallsViewModel ViewModel { get; }
        public FinishSettingsWindow(List<WallType> wallTypes)
        {
            InitializeComponent();
            ViewModel = new FinishWallsViewModel();

            foreach (WallType wt in wallTypes)
                ViewModel.WallTypes.Add(wt);

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
                rule.Layers.Add(new FinishLayer());
            }
        }

        private void DeleteLayer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe &&
                fe.Tag is WallFinishRule rule &&
                rule.Layers.Count > 0)
            {
                rule.Layers.Remove(rule.Layers.Last());
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
    }
}
