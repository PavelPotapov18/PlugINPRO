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


            // ============================================
            // ПАНЕЛЬ "ОБЩИЕ ИНСТРУМЕНТЫ"
            // ============================================

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

            // ============================================
            // ПАНЕЛЬ "IFC"
            // ============================================
            RibbonPanel genplanPanel = application.CreateRibbonPanel("INPRO", "IFC");

            // ============================================
            // КОНТЕЙНЕР "IFC"
            // ============================================
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

            // ============================================
            // ПАНЕЛЬ BIM
            // ============================================
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


            // ============================================
            // ПАНЕЛЬ АР (Архитектура)
            // ============================================

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
            // КОНТЕЙНЕР "ВОМ"
            // ============================================

            // Кнопка "Подсчет ВОМ"
            PushButtonData vomСountButtonData = new PushButtonData(
                "VOMCount",
                "Подсчет ВОМ",
                assemblyPath,
                "PlugINPRO.Commands.AR.MaterialsCountCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.vom32.png"),
                Image = LoadImage("PlugINPRO.Resources.AR.vom16.png"),
                ToolTip = "Заполнение данных для ведомости объемов материалов",
                LongDescription = "Заполняет параметр \"ADSK_Количество\" у элементов (стен, полов и пр.) в зависимости от указанных для элементов единиц измерения"
            };
            
            // Кнопка "Параметры ВОМ"
            PushButtonData vomParamsButtonData = new PushButtonData(
                "VOMParams",
                "Параметры ВОМ",
                assemblyPath,
                "PlugINPRO.Commands.AR.MaterialsParamsCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.vom32.png"),
                Image = LoadImage("PlugINPRO.Resources.AR.vom16.png"),
                ToolTip = "Заполнение данных для ведомости объемов материалов",
                LongDescription = "Заполняет параметры \"INP_ВОМ_Категория\" и \"INP_ВОМ_Единица измерения\" для типоразмеров элементов (стен, полов и пр.)"
            };

            //Кнопка - контейнер "ВОМ"(открывает список команд по созданию ведомости объемов материалов)
            PulldownButtonData VomPullData = new PulldownButtonData("VOMTools", "ВОМ")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.vom32.png"),
                ToolTip = "Команды по созданию ведомости объемов материалов"
            };

            PulldownButton VomPullButton = arPanel.AddItem(VomPullData) as PulldownButton;

            VomPullButton.AddPushButton(vomParamsButtonData);
            VomPullButton.AddPushButton(vomСountButtonData);


            // ============================================
            // КОНТЕЙНЕР "ЭЭ"
            // ============================================

            // Кнопка "Ориентация по сторонам света"
            PushButtonData orientationButtonData = new PushButtonData(
                "Orientation",
                "Ориентация по сторонам света",
                assemblyPath,
                "PlugINPRO.Commands.AR.OrientationCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.compass32.png"),
                Image = LoadImage("PlugINPRO.Resources.AR.compass16.png"),
                ToolTip = "Заполняет параметр \"INP_Ориентация по сторонам света\" ",
                LongDescription = "Заполняет параметр \"INP_Ориентация по сторонам света\" у элементов (стен, дверей и пр.) с учетом ориентации элементов относительно истинного севера"
            };

            // Кнопка "Двери и окна по помещениям"
            PushButtonData DoorWinButtonData = new PushButtonData(
                "DoorsWindows",
                "Проверка дверей и окон",
                assemblyPath,
                "PlugINPRO.Commands.AR.DoorsWindowsPrepCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.dooredit32.png"),
                Image = LoadImage("PlugINPRO.Resources.AR.dooredit16.png"),
                ToolTip = "Проверка готовности дверей и окон к расчету энергоэффективности",
                LongDescription = "Проверка корректности заполнения требуемых параметров у дверей и окон"
            };

            PushButtonData ExportToCsvButtonData = new PushButtonData(
                "ExportToCsv",
                "Экспорт в CSV",
                assemblyPath,
                "PlugINPRO.Commands.AR.ExportToCsvCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.vom32.png"),
                Image = LoadImage("PlugINPRO.Resources.AR.vom16.png"),
                ToolTip = "Экспорт данных в CSV",
                LongDescription = "Экспорт данных по помещениям и эоементам (стены, двери, окна) в табличном формате CSV"
            };

            //Кнопка - контейнер "ЭЭ"(открывает список команд по заполнению параметров для расчета энергоэффективности)
            PulldownButtonData energyPullData = new PulldownButtonData("EnergyTools", "ЭЭ")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.vom32.png"),
                ToolTip = "Команды по подготовке к расчету энергоэффективности"
            };

            PulldownButton energyPullButton = arPanel.AddItem(energyPullData) as PulldownButton;

            energyPullButton.AddPushButton(orientationButtonData);
            energyPullButton.AddPushButton(DoorWinButtonData);
            energyPullButton.AddPushButton(ExportToCsvButtonData);


            // ============================================
            // КОНТЕЙНЕР "ОТДЕЛКА"
            // ============================================

            // Кнопка "Отделка стен по помещениям"
            PushButtonData roomWallFaceButtonData = new PushButtonData(
                "RoomWallFinish",
                "Отделка стен по помещениям",
                assemblyPath,
                "PlugINPRO.Commands.AR.CreateRoomFinishWallsCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.WallFinish32.png"),
                Image = LoadImage("PlugINPRO.Resources.AR.WallFinish16.png"),
                ToolTip = "Отделка стен по помещениям",
                LongDescription = "Создание отделки стен по каждому помещению в зависимости от стены-основы"
            };

            // Кнопка "Отделка пола по помещениям"
            PushButtonData roomFloorButtonData = new PushButtonData(
                "RoomFloorFinish",
                "Отделка пола по помещениям",
                assemblyPath,
                "PlugINPRO.Commands.AR.CreateRoomFinishFloorsCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.FloorFinish32.png"),
                Image = LoadImage("PlugINPRO.Resources.AR.FloorFinish16.png"),
                ToolTip = "Отделка стен по помещениям",
                LongDescription = "Создание отделки пола по выбранному помещению"
            };

            //Кнопка - контейнер "Отделка"(открывает список команд по заполнению параметров для создания отделки помещений)
            PulldownButtonData finishingPullData = new PulldownButtonData("FinishTools", "Отделка")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.Finish32.png"),
                ToolTip = "Команды по созданию отделки помещений"
            };

            PulldownButton finishingPullButton = arPanel.AddItem(finishingPullData) as PulldownButton;

            finishingPullButton.AddPushButton(roomWallFaceButtonData);
            finishingPullButton.AddPushButton(roomFloorButtonData);




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

            // 1. Сначала создаем кнопки
            PushButtonData pipeSupportsByLengthButtonData = new PushButtonData(
                "PipeSupportsByPipeLength",
                "Расстновка опор по всей длине трубопровода",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.Commands.VK.PipeSupportsByLengthCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.VK.PipeFitting32.png"),
                Image = LoadImage("PlugINPRO.Resources.VK.PipeFitting16.png"),
                ToolTip = "Расстановка опор по всей траектории трубопровода\nВключает выбор крепления и шага"
            };

            // НОВАЯ КНОПКА "Копировать относительно опоры"
            PushButtonData pipeSupportCopyButtonData = new PushButtonData(
                "PipeSupportCopy",
                "Копировать\nотносительно\nопоры",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.Commands.VK.PipeSupportCopyCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.VK.PipeFitting32.png"),
                Image = LoadImage("PlugINPRO.Resources.VK.PipeFitting16.png"),
                ToolTip = "Копирование опоры вдоль трассы трубы с заданным смещением",
                LongDescription = "Выберите существующую опору, укажите смещение (+ или -) для копирования вдоль трассы трубы"
            };

            // 2. Создаем кнопку-контейнер
            PulldownButtonData pipeSupportPullData = new PulldownButtonData("PipeSupports", "Опоры трубопроводов")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.VK.PipeFitting32.png"),
                ToolTip = "Инструменты по расстановке опор трубопроводов",
                LongDescription = "Различные опции расстановки опор (по всей длине или конкретное количество)"
            };

            PulldownButton pipeSupportsPulldownButton = vkPanel.AddItem(pipeSupportPullData) as PulldownButton;

            // 3. Добавляем кнопки в контейнер
            pipeSupportsPulldownButton.AddPushButton(pipeSupportsByLengthButtonData);
            pipeSupportsPulldownButton.AddPushButton(pipeSupportCopyButtonData); // ← добавляем новую кнопку

            // ============================================
            // НОВАЯ КНОПКА "Краска" на панели ВК
            // ============================================

            PushButtonData paintVKButtonData = new PushButtonData(
                "PaintCalculator",
                "Краска",
                assemblyPath,
                "PlugINPRO.Commands.VK.PaintCalculatorCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.VK.Paint32.png"),
                Image = LoadImage("PlugINPRO.Resources.VK.Paint16.png"),
                ToolTip = "Расчет расхода краски для системы ВК",
                LongDescription = "Выбирает систему трубопроводов и рассчитывает расход краски"
            };

            vkPanel.AddItem(paintVKButtonData);

            // ============================================
            // НОВАЯ КНОПКА-КОНТЕЙНЕР "Гидравлический расчёт" на панели ВК
            // ============================================

            PushButtonData hydraulicCalculateButtonData = new PushButtonData(
                "HydraulicCalculate",
                "Рассчитать\nсистему",
                assemblyPath,
                "PlugINPRO.Commands.VK.HydraulicCalculateCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.VK.Hydraulic32.png"),
                Image = LoadImage("PlugINPRO.Resources.VK.Hydraulic16.png"),
                ToolTip = "Гидравлический расчёт выбранной системы",
                LongDescription = "Выполняет гидравлический расчёт трубопроводной системы"
            };

            PushButtonData hydraulicAddParamsButtonData = new PushButtonData(
                "HydraulicAddParams",
                "Добавить\nпараметры",
                assemblyPath,
                "PlugINPRO.Commands.VK.HydraulicAddParamsCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.VK.HydraulicParams32.png"),
                Image = LoadImage("PlugINPRO.Resources.VK.HydraulicParams16.png"),
                ToolTip = "Добавить параметры для гидравлического расчёта",
                LongDescription = "Добавляет в проект общие параметры, необходимые для выполнения гидравлического расчёта"
            };

            PulldownButtonData hydraulicPullData = new PulldownButtonData("HydraulicTools", "Гидравлический\nрасчёт")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.VK.Hydraulic32.png"),
                ToolTip = "Инструменты гидравлического расчёта",
                LongDescription = "Выполнение гидравлического расчёта и добавление необходимых параметров"
            };

            PulldownButton hydraulicPullButton = vkPanel.AddItem(hydraulicPullData) as PulldownButton;
            hydraulicPullButton.AddPushButton(hydraulicCalculateButtonData);
            hydraulicPullButton.AddPushButton(hydraulicAddParamsButtonData);

            // ============================================

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
            // НОВАЯ КНОПКА "Пересечение осей"
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