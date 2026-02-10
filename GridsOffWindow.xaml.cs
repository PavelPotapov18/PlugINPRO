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

namespace PlugINPRO
{
    /// <summary>
    /// Interaction logic for GridsOffWindow.xaml
    /// </summary>
    public partial class GridsOffWindow : Window
    {
        public string Suffix { get; private set; }
        public GridsOffWindow()
        {
            InitializeComponent();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SuffixBox.Text))
            {
                MessageBox.Show(
                    "Суффикс не может быть пустым",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Suffix = SuffixBox.Text.Trim();
            DialogResult = true;
            Close();
        }
    }
}
