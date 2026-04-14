using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PlugINPRO
{
    public partial class DimensionSettings : Window
    {
        private DimensionSettingsData settings;
        private UIApplication uiApp;

        public DimensionSettings(UIApplication uiApp)
        {
            InitializeComponent();
            this.uiApp = uiApp;

            // Загружаем настройки
            settings = DimensionSettingsData.Load();

            // Загружаем типы размеров из проекта
            LoadDimensionTypes();

            // Устанавливаем выбранный режим
            SetSelectedMode(settings.Mode);

            // Устанавливаем значение смещения (переводим из метров в миллиметры)
            OffsetTextBox.Text = (settings.OffsetDistance * 1000).ToString("F0");
        }

        private void LoadDimensionTypes()
        {
            try
            {
                Document doc = uiApp.ActiveUIDocument.Document;

                // Собираем только ЛИНЕЙНЫЕ типы размеров
                var dimensionTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .Where(dt => dt.StyleType == DimensionStyleType.Linear)
                    .OrderBy(dt => dt.Name)
                    .ToList();

                DimensionTypeComboBox.Items.Clear();

                foreach (DimensionType dt in dimensionTypes)
                {
                    DimensionTypeComboBox.Items.Add(dt);

                    if (settings.DimensionTypeId != null &&
                        dt.Id.ToString() == settings.DimensionTypeId)
                    {
                        DimensionTypeComboBox.SelectedItem = dt;
                    }
                }

                if (DimensionTypeComboBox.SelectedItem == null &&
                    DimensionTypeComboBox.Items.Count > 0)
                {
                    DimensionTypeComboBox.SelectedIndex = 0;
                }

                if (DimensionTypeComboBox.Items.Count == 0)
                {
                    DimensionTypeComboBox.IsEnabled = false;
                    DimensionTypeComboBox.Items.Add("Нет линейных типов размеров");
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", $"Не удалось загрузить типы размеров: {ex.Message}");
            }
        }

        private void SetSelectedMode(DimensionMode mode)
        {
            foreach (ComboBoxItem item in ModeComboBox.Items)
            {
                if (item.Tag.ToString() == mode.ToString())
                {
                    ModeComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Здесь можно добавить логику при смене режима
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Сохраняем режим
                if (ModeComboBox.SelectedItem is ComboBoxItem selectedMode)
                {
                    if (Enum.TryParse(selectedMode.Tag.ToString(), out DimensionMode mode))
                    {
                        settings.Mode = mode;
                    }
                }

                // Сохраняем тип размера
                if (DimensionTypeComboBox.SelectedItem is DimensionType selectedType)
                {
                    settings.DimensionTypeId = selectedType.Id.ToString();
                }

                // Сохраняем смещение (переводим из миллиметров в метры)
                if (double.TryParse(OffsetTextBox.Text, out double offsetMm))
                {
                    if (offsetMm <= 0)
                    {
                        TaskDialog.Show("Ошибка", "Смещение должно быть больше 0");
                        return;
                    }
                    settings.OffsetDistance = offsetMm / 1000.0; // переводим в метры
                }
                else
                {
                    TaskDialog.Show("Ошибка", "Введите корректное значение смещения");
                    return;
                }

                settings.Save();

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", $"Не удалось сохранить настройки: {ex.Message}");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}