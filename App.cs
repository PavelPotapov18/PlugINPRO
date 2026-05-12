using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace PlugINPRO
{
    public class App : IExternalApplication
    {

        // Статические переменные для хранения выбранных семейств
        public static FamilySymbol SelectedSupportFamily { get; set; }
        public static FamilySymbol SelectedCableTraySupportFamily { get; set; }
        public static FamilySymbol SelectedPipeSupportFamily { get; set; } // НОВОЕ: для труб

        // ============================================
        // ДОБАВЛЕННЫЕ ПОЛЯ ДЛЯ ХРАНЕНИЯ РАЗМЕРОВ
        // ============================================

        // Последние размеры воздуховода (для информации)
        public static double LastDuctWidth { get; set; }
        public static double LastDuctHeight { get; set; }
        public static double LastDuctDiameter { get; set; }
        public static bool LastDuctWasRound { get; set; }

        // Можно также хранить строковую информацию для отображения
        public static string LastDuctInfo { get; set; }

        public Result OnStartup(UIControlledApplication application)
        {

            // Пытаемся создать вкладку (если уже есть — Revit кинет исключение)
            try
            {
                application.CreateRibbonTab("INPRO");
            }
            catch
            {
                // вкладка уже существует — это нормально
            }

            // Путь к текущей DLL
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // Создаём панель "Информация о плагине" на вкладке
            RibbonPanel infoPanel = application.CreateRibbonPanel("INPRO", "О плагине");
            PushButtonData logoButtonData = new PushButtonData(
                "LogoButton",
                "Info",
                assemblyPath,
                "PlugINPRO.Commands.PluginInfo.InfoCommand"
                )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.PluginInfo.info32.png"),
                Image = LoadImage("PlugINPRO.Resources.PluginInfo.info16.png"),
                ToolTip = "PlugINPRO v1.0",
                LongDescription = "О плагине"
            };

            infoPanel.AddItem(logoButtonData);


            // Создаём панель "Общие инструменты" на вкладке
            RibbonPanel commonPanel = application.CreateRibbonPanel("INPRO", "Общие инструменты");

            // Кнопка "Проставление ревизий"
            PushButtonData revButtonData = new PushButtonData(
                "RevisionButton",
                "Ревизии\nи ИЗМы",
                assemblyPath,
                "PlugINPRO.Commands.Common.RevIZMCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.Common.R00_32.png"),
                Image = LoadImage("PlugINPRO.Resources.Common.R00_16.png"),
                ToolTip = "Проставление ревизий и ИЗМов в штампе"
            };

            commonPanel.AddItem(revButtonData);


            // Кнопка "Отключение осей и уровней связанных файлов"
            PushButtonData gridsButtonData = new PushButtonData(
                "GridsButton",
                "Оси/Уровни\nоткл.",
                assemblyPath,
                "PlugINPRO.Commands.Common.GridsOffCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.Common.grids32.png"),
                Image = LoadImage("PlugINPRO.Resources.Common.grids16.png"),
                ToolTip = "Отключение осей и уровней в связанных файлах"
            };

            commonPanel.AddItem(gridsButtonData);

            // Кнопка "Проверка рабочих наборов"
            PushButtonData worksetsButtonData = new PushButtonData(
                "WorksetsButton",
                "Проверка\nраб.наборов",
                assemblyPath,
                "PlugINPRO.Commands.Common.WorksetsCheckCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.Common.Worksets32.png"),
                Image = LoadImage("PlugINPRO.Resources.Common.Worksets16.png"),
                ToolTip = "Проверка рабочих наборов на корректность заполнения по элементам",
                LongDescription = "Работает для наборов \"Общие уровни и сетки\" и наборов для связанных файлов (\"#Связь_АР\", \"#Связь_КР\" и др.)"
            };
            commonPanel.AddItem(worksetsButtonData);

            // Кнопка "3D по рабочим наборам"
            PushButtonData ThreeDByWorksetsButtonData = new PushButtonData(
                "ThreeDByWorksetsButton",
                "3D по\nраб.наборам",
                assemblyPath,
                "PlugINPRO.Commands.Common.ThreeDByWorksetsCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.Common.cube32.png"),
                Image = LoadImage("PlugINPRO.Resources.Common.cube16.png"),
                ToolTip = "Создание 3D видов по рабочим наборам",
                LongDescription = "Создает отдельные 3D виды, содержащие элементы только одного конкретного рабочего набора"
            };
            commonPanel.AddItem(ThreeDByWorksetsButtonData);

            // Кнопка "Экспорт PDF и DWG"
            PushButtonData exportButtonData = new PushButtonData(
                "PdfDwgExportButton",
                "Экспорт\nPDF и DWG",
                assemblyPath,
                "PlugINPRO.Commands.Common.PdfDwgExportCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.Common.export32.png"),
                Image = LoadImage("PlugINPRO.Resources.Common.export16.png"),
                ToolTip = "Экспорт листов и комплектов в PDF и DWG формат",
                LongDescription = "Экспорт листов проекта в PDF и DWG по комплектам и создание сводного PDF файла для выбранного комплекта"
            };
            commonPanel.AddItem(exportButtonData);

            //Создаем панель IFC
            RibbonPanel genplanPanel = application.CreateRibbonPanel("INPRO", "IFC");

            // Кнопка "IFC по координатам"
            PushButtonData ifcButtonData = new PushButtonData(
                "IFCByCoordinates",
                "IFC по\nкоординатам",
                assemblyPath,
                "PlugINPRO.Commands.IFC.IFCByCoordinatesCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.IFC.ifc32.png"),
                Image = LoadImage("PlugINPRO.Resources.IFC.ifc16.png"),
                ToolTip = "Переместить выбранный IFC на введенные координаты"
            };

            // Кнопка "Сбросить"
            PushButtonData resetButtonData = new PushButtonData(
                "ResetCoordinates",
                "Сбросить",
                assemblyPath,
                "PlugINPRO.Commands.IFC.ResetCoordinatesCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.IFC.ifc32.png"),
                Image = LoadImage("PlugINPRO.Resources.IFC.ifc16.png"),
                ToolTip = "Установить все координаты в 0"
            };
            // Кнопка "Покраска IFC элементов"
            PushButtonData paintButtonData = new PushButtonData(
                "IFCPaint",
                "Покраска IFC элементов",
                assemblyPath,
                "PlugINPRO.Commands.IFC.IFCPaintCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.IFC.ifc32.png"),
                Image = LoadImage("PlugINPRO.Resources.IFC.ifc16.png"),
                ToolTip = "Покраска требуемых элементов из IFC связей"
            };

            //Кнопка - контейнер "IFC"(открывает список команд по работе с IFC)
            PulldownButtonData IfcPullData = new PulldownButtonData("IFCTools", "IFC")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.IFC.ifc32.png"),
                ToolTip = "Работа по переносу фалов IFC"
            };

            PulldownButton IfcPullButton = genplanPanel.AddItem(IfcPullData) as PulldownButton;

            IfcPullButton.AddPushButton(ifcButtonData);
            IfcPullButton.AddPushButton(resetButtonData);
            IfcPullButton.AddPushButton(paintButtonData);


            //Создаем панель BIM
            RibbonPanel bimPanel = application.CreateRibbonPanel("INPRO", "BIM");

            // Кнопка "IFC to RVT"
            PushButtonData IfcRvtButtonData = new PushButtonData(
                "IFСtoRVT",
                "IFC в RVT",
                assemblyPath,
                "PlugINPRO.Commands.BIM.IFCtoRVTCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.IFC.ifc32.png"),
                Image = LoadImage("PlugINPRO.Resources.IFC.ifc16.png"),
                ToolTip = "Перенос IFC файлов в формат RVT с изменением свойств"
            };

            bimPanel.AddItem(IfcRvtButtonData);

            // Добавим после существующих панелей или в соответствующем месте
            RibbonPanel arPanel = application.CreateRibbonPanel("INPRO", "АР");

            // Кнопка "Редактор дверей"
            PushButtonData doorEditorButtonData = new PushButtonData(
                "DoorEditor",
                "Редактор\nдверей",
                assemblyPath,
                "PlugINPRO.Commands.AR.DoorEditorCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.dooredit32.png"),
                Image = LoadImage("PlugINPRO.Resources.AR.dooredit16.png"),
                ToolTip = "Редактирование параметров выбранной двери с предпросмотром",
                LongDescription = "Позволяет редактировать параметры двери в реальном времени с визуальным отображением изменений"
            };

            arPanel.AddItem(doorEditorButtonData);

            // ============================================
            // ПАНЕЛЬ ОВ (Отопление и Вентиляция)
            // ============================================
            RibbonPanel ovPanel = application.CreateRibbonPanel("INPRO", "ОВ");

            // Кнопка "Опоры воздуховодов"
            PushButtonData ductSupportsButtonData = new PushButtonData(
                "DuctSupports",
                "Опоры\nвоздуховодов",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.Commands.OV.DuctSupportsCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.OV.DuctFitting32.png"),
                Image = LoadImage("PlugINPRO.Resources.OV.DuctFitting16.png"),
                ToolTip = "Расстановка опор по траектории воздуховода\nВключает выбор крепления и шага"
            };
            ovPanel.AddItem(ductSupportsButtonData);

            // ============================================
            // ПАНЕЛЬ ЭОМ (Электромонтаж)
            // ============================================
            RibbonPanel eomPanel = application.CreateRibbonPanel("INPRO", "ЭОМ");

            // Кнопка "Опоры лотков"
            PushButtonData cableTraySupportsButtonData = new PushButtonData(
                "CableTraySupports",
                "Опоры\nлотков",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.Commands.ES.CableTraySupportsCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.ES.CableTrayFitting32.png"),
                Image = LoadImage("PlugINPRO.Resources.ES.CableTrayFitting16.png"),
                ToolTip = "Расстановка опор по траектории кабельного лотка\nВключает выбор крепления и шага"
            };

            eomPanel.AddItem(cableTraySupportsButtonData);

            // ============================================
            // ПАНЕЛЬ ВК (Водопровод и канализация)
            // ============================================
            RibbonPanel vkPanel = application.CreateRibbonPanel("INPRO", "ВК");

            // Кнопка "Опоры трубопроводов"
            PushButtonData pipeSupportsButtonData = new PushButtonData(
                "PipeSupports",
                "Опоры\nтрубопроводов",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.Commands.VK.PipeSupportsCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.VK.PipeFitting32.png"),
                Image = LoadImage("PlugINPRO.Resources.VK.PipeFitting16.png"),
                ToolTip = "Расстановка опор по траектории трубопровода\nВключает выбор крепления и шага"
            };

            vkPanel.AddItem(pipeSupportsButtonData);

            // ============================================
            // НОВАЯ ПАНЕЛЬ ТС (Тепловые сети)
            // ============================================
            RibbonPanel tsPanel = application.CreateRibbonPanel("INPRO", "ТС");

            // Кнопка "Размеры на аксонометрии"
            PushButtonData dimensionButtonData = new PushButtonData(
                "AxonometricDimension",
                "Размеры на\nаксонометрии",
                assemblyPath,
                "PlugINPRO.Commands.TS.AxonometricDimensionCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.TS.dimensions32.png"),
                Image = LoadImage("PlugINPRO.Resources.TS.dimensions16.png"),
                ToolTip = "Расстановка размеров на аксонометрических видах",
                LongDescription = "Проставляет размеры на выбранных трубах. Режим работы настраивается в окне настроек"
            };

            tsPanel.AddItem(dimensionButtonData);

            // Кнопка "Настройки"
            PushButtonData settingsButtonData = new PushButtonData(
                "DimensionSettings",
                "Настройки\nразмеров",
                assemblyPath,
                "PlugINPRO.Commands.TS.DimensionSettingsCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.TS.settings32.png"),
                Image = LoadImage("PlugINPRO.Resources.TS.settings16.png"),
                ToolTip = "Настройка параметров простановки размеров",
                LongDescription = "Выбор режима простановки размеров и типа размера"
            };

            tsPanel.AddItem(settingsButtonData);

            // ============================================
            // НОВАЯ КНОПКА "Переечение осей"
            // ============================================
            PushButtonData dimensionToGridsButtonData = new PushButtonData(
                "DimensionToGrids",
                "Пересечение\nосей",
                assemblyPath,
                "PlugINPRO.Commands.TS.DimensionToGridsCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.dimensiontogrids32.png"),
                Image = LoadImage("PlugINPRO.Resources.dimensiontogrids16.png"),
                ToolTip = "Размер от трубы/фитинга до пересечения осей",
                LongDescription = "Создает размер от выбранной точки трубы или центра фитинга до точки пересечения двух выбранных осей"
            };

            tsPanel.AddItem(dimensionToGridsButtonData);
            // ============================================

            return Result.Succeeded;

        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private BitmapImage LoadImage(string resourceName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    return null;

                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = stream;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();

                return image;
            }
        }

    }
}