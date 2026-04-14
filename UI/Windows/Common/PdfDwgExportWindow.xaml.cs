using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Forms = System.Windows.Forms;

namespace PlugINPRO.UI.Windows.Common
{
    public partial class PdfDwgExportWindow : Window
    {
        private Document _doc;

        public ExportSettings ResultSettings { get; private set; }

        private bool _isLoaded = false;

        public PdfDwgExportWindow(Document doc)
        {
            InitializeComponent();
            
            _doc = doc;

            LoadPackageValues();
            LoadStatusValues();
            LoadDwgPredefinedSetups();

            _isLoaded = true;
            UpdatePreview();
        }

        

        private void LoadPackageValues()
        {
            var values = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Select(v => v.LookupParameter("ADSK_Штамп Раздел проекта")?.AsString())
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            PackageCombo.ItemsSource = values;

            if (values.Any())
                PackageCombo.SelectedIndex = 0;
        }

        private void LoadStatusValues()
        {
            var values = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Select(v => v.LookupParameter("Project status_text")?.AsString())
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            StatusCombo.ItemsSource = values;

            if (values.Any())
                StatusCombo.SelectedIndex = 0;
        }

        private void LoadDwgPredefinedSetups()
        {
            IList<string> setups =
                BaseExportOptions.GetPredefinedSetupNames(_doc);

            if (setups != null && setups.Count > 0)
            {
                DwgSetupCombo.ItemsSource = setups;
                DwgSetupCombo.SelectedIndex = 0;
            }
            else
            {
                DwgSetupCombo.ItemsSource =
                    new List<string> { "<Нет доступных настроек>" };

                DwgSetupCombo.SelectedIndex = 0;
                DwgSetupCombo.IsEnabled = false;
            }
        }

        // =========================
        // LIVE PREVIEW
        // =========================

        private void AnyValueChanged(object sender, EventArgs e)
        {
            if (!_isLoaded)
                return;
            UpdatePreview();
        }

        private void AlternativeCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
                return;
            AlternativePanel.IsEnabled = AlternativeCheck.IsChecked == true;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_doc == null)
                return;

            string proj = _doc.ProjectInformation.Number ?? "1001";

            string building =
                _doc.ProjectInformation
                    .LookupParameter("ADSK_Номер здания")?
                    .AsString() ?? "01";

            string package = PackageCombo.SelectedItem?.ToString() ?? "АР";

            string sep1 = Sep1Box.Text ?? "";
            string sep2 = Sep2Box.Text ?? "";
            string sep3 = Sep3Box.Text ?? "";

            // ===== Имя комплекта =====
            string packageName =
                proj + sep1 +
                building + sep2 +
                package;

            if (AddOprSuffixCheck.IsChecked == true)
                packageName += ".ОПР";

            // ===== Демонстрационные данные листа =====
            string afry = "A100";
            string rev = "02";
            string izm = "1";

            string baseSheetName =
                packageName + sep3 +
                afry +
                "_R" + rev +
                "_Izm" + izm;

            string altSheetName =
                (AlternativePrefixBox.Text ?? "") +
                " Лист 1_Izm" + izm;

            PreviewText.Text =
                AlternativeCheck.IsChecked == true
                ? altSheetName
                : baseSheetName;
        }

        // =========================
        // КНОПКИ
        // =========================

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ResultSettings = new ExportSettings
            {
                DirectoryPath = DirectoryBox.Text,
                PackageName = PackageCombo.SelectedItem?.ToString(),
                ProjectStatus = StatusCombo.SelectedItem?.ToString(),

                Separator1 = Sep1Box.Text,
                Separator2 = Sep2Box.Text,
                Separator3 = Sep3Box.Text,

                AddOprSuffix = AddOprSuffixCheck.IsChecked == true,

                UseAlternativeName = AlternativeCheck.IsChecked == true,
                AlternativePrefix = AlternativePrefixBox.Text,

                DwgPredefinedSetup = DwgSetupCombo.SelectedItem?.ToString()
            };

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Forms.FolderBrowserDialog();

            if (dialog.ShowDialog() ==
                Forms.DialogResult.OK)
            {
                DirectoryBox.Text = dialog.SelectedPath;
            }
        }
    }

    // =========================
    // КЛАСС НАСТРОЕК
    // =========================

    public class ExportSettings
    {
        public string DirectoryPath { get; set; }
        public string PackageName { get; set; }
        public string ProjectStatus { get; set; }

        public string Separator1 { get; set; }
        public string Separator2 { get; set; }
        public string Separator3 { get; set; }

        public bool AddOprSuffix { get; set; }

        public bool UseAlternativeName { get; set; }
        public string AlternativePrefix { get; set; }

        public string DwgPredefinedSetup { get; set; }
    }
}