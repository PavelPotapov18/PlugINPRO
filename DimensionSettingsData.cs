using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml.Serialization;
using Autodesk.Revit.DB;

namespace PlugINPRO
{
    public class DimensionSettingsData
    {
        public DimensionMode Mode { get; set; }
        public string DimensionTypeId { get; set; }
        public double OffsetDistance { get; set; }

        public DimensionSettingsData()
        {
            Mode = DimensionMode.ByPipeLength;
            DimensionTypeId = "";
            OffsetDistance = 0.3;
        }

        private static string SettingsPath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = Path.Combine(appData, "PlugINPRO");
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                return Path.Combine(folder, "DimensionSettings.xml");
            }
        }

        public void Save()
        {
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(DimensionSettingsData));
                using (StreamWriter writer = new StreamWriter(SettingsPath))
                {
                    serializer.Serialize(writer, this);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения настроек: {ex.Message}");
            }
        }

        public static DimensionSettingsData Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(DimensionSettingsData));
                    using (StreamReader reader = new StreamReader(SettingsPath))
                    {
                        return (DimensionSettingsData)serializer.Deserialize(reader);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки настроек: {ex.Message}");
            }

            return new DimensionSettingsData();
        }
    }

    public enum DimensionMode
    {
        ByPipeLength,           // Режим 1: Размеры по трубам/воздуховодам
        ByFittingCenter,        // Режим 2: Размеры от центра фитинга до центра фитинга
        BySupportsToPipe,       // Режим 3: Размеры между опорами до труб/воздуховодов
        BySupportsToFittings    // Режим 4: Размеры между опорами до фитингов
    }
}