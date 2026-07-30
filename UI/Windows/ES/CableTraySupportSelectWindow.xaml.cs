using Autodesk.Revit.DB;
using PlugINPRO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PlugINPRO.UI.Windows.ES
{
    public partial class CableTraySupportSelectWindow : Window
    {
        public FamilySymbol SelectedFamily { get; private set; }

        private Document _document;
        private List<FamilySymbol> _availableFamilies;

        public CableTraySupportSelectWindow(Document document)
        {
            InitializeComponent();
            _document = document;
            Loaded += Window_Loaded;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadAvailableFamilies();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadAvailableFamilies()
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(_document)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_CableTrayFitting)
                    .WhereElementIsElementType();

                _availableFamilies = collector.Cast<FamilySymbol>().ToList();

                _availableFamilies = _availableFamilies
                    .Where(fs =>
                        fs.Family.Name.IndexOf("крепление", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fs.Name.IndexOf("крепление", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fs.Family.Name.IndexOf("опора", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fs.Name.IndexOf("опора", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fs.Family.Name.IndexOf("support", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fs.Name.IndexOf("support", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                if (_availableFamilies.Count > 0)
                {
                    var familyItems = _availableFamilies.Select(fs => new
                    {
                        FamilySymbol = fs,
                        DisplayName = GetFamilyDisplayName(fs)
                    }).ToList();

                    cmbFamilies.ItemsSource = familyItems;
                    cmbFamilies.SelectedIndex = 0;

                    if (App.SelectedCableTraySupportFamily != null && App.SelectedCableTraySupportFamily.IsValidObject)
                    {
                        for (int i = 0; i < _availableFamilies.Count; i++)
                        {
                            if (_availableFamilies[i].Id == App.SelectedCableTraySupportFamily.Id)
                            {
                                cmbFamilies.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    tbInfo.Text = "❌ В проекте не найдены семейства опор.\n" +
                                 "Загрузите семейства с названием\n" +
                                 "'Опора' или 'Support' в категории CABLE TRAY FITTINGS";
                    btnOk.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки семейств: {ex.Message}",
                    "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                _availableFamilies = new List<FamilySymbol>();
            }
        }

        private string GetFamilyDisplayName(FamilySymbol symbol)
        {
            if (symbol == null) return "Неизвестно";

            string name = symbol.Family.Name;
            if (!string.IsNullOrEmpty(symbol.Name) && symbol.Name != symbol.Family.Name)
            {
                name += $" : {symbol.Name}";
            }

            if (name.Length > 60)
                name = name.Substring(0, 57) + "...";

            return name;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (cmbFamilies.SelectedItem == null)
            {
                MessageBox.Show("Выберите опору из списка!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            dynamic selectedItem = cmbFamilies.SelectedItem;
            SelectedFamily = selectedItem.FamilySymbol;

            if (SelectedFamily == null || !SelectedFamily.IsValidObject)
            {
                MessageBox.Show("Выбранная опора не доступна!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            App.SelectedCableTraySupportFamily = SelectedFamily;

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}