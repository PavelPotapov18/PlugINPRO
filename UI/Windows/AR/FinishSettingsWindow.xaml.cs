using Autodesk.Revit.DB;
using PlugINPRO.Services.AR;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PlugINPRO.UI.Windows.AR
{
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
    }

    

    
}
