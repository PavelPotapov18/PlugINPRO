using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PlugINPRO.UI.Windows.Common
{
    public partial class WorksetsCheckWindow : Window
    {
        public Workset SelectedWorkset { get; private set; }

        public WorksetsCheckWindow(Document doc)
        {
            InitializeComponent();

            var worksets = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .Where(w =>
                    w.Name == "Shared Levels and Grids" ||
                    w.Name == "Общие уровни и сетки" ||
                    w.Name == "Внешние ссылки" ||
                    w.Name.Contains("#Связь"))
                .ToList();

            WorksetComboBox.ItemsSource = worksets;
            WorksetComboBox.DisplayMemberPath = "Name";
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            SelectedWorkset = WorksetComboBox.SelectedItem as Workset;

            if (SelectedWorkset == null)
            {
                MessageBox.Show("Выберите рабочий набор.");
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
