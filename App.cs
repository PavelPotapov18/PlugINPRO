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
        public static FamilySymbol SelectedPipeSupportFamily { get; set; }

        // ============================================
        // ДОБАВЛЕННЫЕ ПОЛЯ ДЛЯ ХРАНЕНИЯ РАЗМЕРОВ
        // ============================================

        public static double LastDuctWidth { get; set; }
        public static double LastDuctHeight { get; set; }
        public static double LastDuctDiameter { get; set; }
        public static bool LastDuctWasRound { get; set; }
        public static string LastDuctInfo { get; set; }

        public Result OnStartup(UIControlledApplication application)
        {

            // Пытаемся создать вкладку
            try
            {
                application.CreateRibbonTab("INPRO");
            }
            catch
            {
                // вкладка уже существует
            }

            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // ============================================
            // ПАНЕЛЬ "О плагине"
            // ============================================
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
                LongDescription = "Работает для наборов \"Общие уровни и сетки\" и наборов для связанных файлов"
            };
            commonPanel.AddItem(worksetsButtonData);

            PushButtonData ThreeDByWorksetsButtonData = new PushButtonData(
                "ThreeDByWorksetsButton",
                "3D по\nраб.наборам",
                assemblyPath,
                "PlugINPRO.Commands.Common.ThreeDByWorksetsCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.Common.cube32.png"),
                Image = LoadImage("PlugINPRO.Resources.Common.cube16.png"),
                ToolTip = "Создание 3D видов по рабочим наборам"
            };
            commonPanel.AddItem(ThreeDByWorksetsButtonData);

            PushButtonData exportButtonData = new PushButtonData(
                "PdfDwgExportButton",
                "Экспорт\nPDF и DWG",
                assemblyPath,
                "PlugINPRO.Commands.Common.PdfDwgExportCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.Common.export32.png"),
                Image = LoadImage("PlugINPRO.Resources.Common.export16.png"),
                ToolTip = "Экспорт листов и комплектов в PDF и DWG формат"
            };
            commonPanel.AddItem(exportButtonData);

            // ============================================
            // ПАНЕЛЬ "IFC"
            // ============================================
            RibbonPanel genplanPanel = application.CreateRibbonPanel("INPRO", "IFC");

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

            PulldownButtonData IfcPullData = new PulldownButtonData("IFCTools", "IFC")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.IFC.ifc32.png"),
                ToolTip = "Работа по переносу файлов IFC"
            };

            PulldownButton IfcPullButton = genplanPanel.AddItem(IfcPullData) as PulldownButton;
            IfcPullButton.AddPushButton(ifcButtonData);
            IfcPullButton.AddPushButton(resetButtonData);
            IfcPullButton.AddPushButton(paintButtonData);

            // ============================================
            // ПАНЕЛЬ BIM
            // ============================================
            RibbonPanel bimPanel = application.CreateRibbonPanel("INPRO", "BIM");

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

            PushButtonData doorEditorButtonData = new PushButtonData(
                "DoorEditor",
                "Редактор\nдверей",
                assemblyPath,
                "PlugINPRO.Commands.AR.DoorEditorCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.dooredit32.png"),
                Image = LoadImage("PlugINPRO.Resources.AR.dooredit16.png"),
                ToolTip = "Редактирование параметров выбранной двери с предпросмотром"
            };
            arPanel.AddItem(doorEditorButtonData);

            // КОНТЕЙНЕР "ВОМ"
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
                LongDescription = "Заполняет параметр \"ADSK_Количество\" у элементов"
            };

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
                LongDescription = "Заполняет параметры \"INP_ВОМ_Категория\" и \"INP_ВОМ_Единица измерения\""
            };

            PulldownButtonData VomPullData = new PulldownButtonData("VOMTools", "ВОМ")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.vom32.png"),
                ToolTip = "Команды по созданию ведомости объемов материалов"
            };

            PulldownButton VomPullButton = arPanel.AddItem(VomPullData) as PulldownButton;
            VomPullButton.AddPushButton(vomParamsButtonData);
            VomPullButton.AddPushButton(vomСountButtonData);

            // КОНТЕЙНЕР "ЭЭ"
            PushButtonData orientationButtonData = new PushButtonData(
                "Orientation",
                "Ориентация по сторонам света",
                assemblyPath,
                "PlugINPRO.Commands.AR.OrientationCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.compass32.png"),
                Image = LoadImage("PlugINPRO.Resources.AR.compass16.png"),
                ToolTip = "Заполняет параметр \"INP_Ориентация по сторонам света\""
            };

            PushButtonData DoorWinButtonData = new PushButtonData(
                "DoorsWindows",
                "Проверка дверей и окон",
                assemblyPath,
                "PlugINPRO.Commands.AR.DoorsWindowsPrepCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.dooredit32.png"),
                Image = LoadImage("PlugINPRO.Resources.AR.dooredit16.png"),
                ToolTip = "Проверка готовности дверей и окон к расчету энергоэффективности"
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
                ToolTip = "Экспорт данных в CSV"
            };

            PulldownButtonData energyPullData = new PulldownButtonData("EnergyTools", "ЭЭ")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.vom32.png"),
                ToolTip = "Команды по подготовке к расчету энергоэффективности"
            };

            PulldownButton energyPullButton = arPanel.AddItem(energyPullData) as PulldownButton;
            energyPullButton.AddPushButton(orientationButtonData);
            energyPullButton.AddPushButton(DoorWinButtonData);
            energyPullButton.AddPushButton(ExportToCsvButtonData);

            // КОНТЕЙНЕР "ОТДЕЛКА"
            PushButtonData roomWallFaceButtonData = new PushButtonData(
                "RoomWallFinish",
                "Отделка стен по помещениям",
                assemblyPath,
                "PlugINPRO.Commands.AR.CreateRoomFinishWallsCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.WallFinish32.png"),
                Image = LoadImage("PlugINPRO.Resources.AR.WallFinish16.png"),
                ToolTip = "Отделка стен по помещениям"
            };

            PushButtonData roomFloorButtonData = new PushButtonData(
                "RoomFloorFinish",
                "Отделка пола по помещениям",
                assemblyPath,
                "PlugINPRO.Commands.AR.CreateRoomFinishFloorsCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.FloorFinish32.png"),
                Image = LoadImage("PlugINPRO.Resources.AR.FloorFinish16.png"),
                ToolTip = "Отделка пола по помещениям"
            };

            PushButtonData roomCeilingButtonData = new PushButtonData(
                "RoomCeilingFinish",
                "Отделка потолков по помещениям",
                assemblyPath,
                "PlugINPRO.Commands.AR.CreateRoomFinishCeilingCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.FloorFinish32.png"),
                Image = LoadImage("PlugINPRO.Resources.AR.FloorFinish16.png"),
                ToolTip = "Созданием потолков по помещениям"
            };

            PulldownButtonData finishingPullData = new PulldownButtonData("FinishTools", "Отделка")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.AR.Finish32.png"),
                ToolTip = "Команды по созданию отделки помещений"
            };

            PulldownButton finishingPullButton = arPanel.AddItem(finishingPullData) as PulldownButton;
            finishingPullButton.AddPushButton(roomWallFaceButtonData);
            finishingPullButton.AddPushButton(roomFloorButtonData);
            finishingPullButton.AddPushButton(roomCeilingButtonData);


            // ============================================
            // 🆕 ПАНЕЛЬ ОВ (Отопление и Вентиляция) - КОНТЕЙНЕР
            // ============================================
            RibbonPanel ovPanel = application.CreateRibbonPanel("INPRO", "ОВ");

            PulldownButtonData ductSupportPullData = new PulldownButtonData("DuctSupports", "Опоры\nвоздуховодов")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.OV.DuctFitting32.png"),
                ToolTip = "Инструменты по расстановке опор воздуховодов"
            };

            PulldownButton ductSupportsPulldownButton = ovPanel.AddItem(ductSupportPullData) as PulldownButton;

            ductSupportsPulldownButton.AddPushButton(new PushButtonData(
                "DuctSupportsByLength",
                "По всей длине",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.Commands.OV.DuctSupportsByLengthCommand")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.OV.DuctFitting32.png"),
                Image = LoadImage("PlugINPRO.Resources.OV.DuctFitting16.png"),
                ToolTip = "Расстановка опор по всей длине воздуховода"
            });

            ductSupportsPulldownButton.AddPushButton(new PushButtonData(
                "DuctSupportCopy",
                "Копировать опору",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.Commands.OV.DuctSupportCopyCommand")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.OV.DuctFitting32.png"),
                Image = LoadImage("PlugINPRO.Resources.OV.DuctFitting16.png"),
                ToolTip = "Копирование опоры вдоль трассы воздуховода"
            });

            ductSupportsPulldownButton.AddPushButton(new PushButtonData(
                "DuctSupportPlace",
                "Поставить опору",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.Commands.OV.DuctSupportPlaceCommand")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.OV.DuctFitting32.png"),
                Image = LoadImage("PlugINPRO.Resources.OV.DuctFitting16.png"),
                ToolTip = "Установка опоры на воздуховод с заданным отступом"
            });

            // ============================================
            // 🆕 ПАНЕЛЬ ЭОМ (Электромонтаж) - КОНТЕЙНЕР
            // ============================================
            RibbonPanel eomPanel = application.CreateRibbonPanel("INPRO", "ЭОМ");

            PulldownButtonData cableTraySupportPullData = new PulldownButtonData("CableTraySupports", "Опоры\nлотков")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.ES.CableTrayFitting32.png"),
                ToolTip = "Инструменты по расстановке опор кабельных лотков"
            };

            PulldownButton cableTraySupportsPulldownButton = eomPanel.AddItem(cableTraySupportPullData) as PulldownButton;

            cableTraySupportsPulldownButton.AddPushButton(new PushButtonData(
                "CableTraySupportsByLength",
                "По всей длине",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.Commands.ES.CableTraySupportsByLengthCommand")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.ES.CableTrayFitting32.png"),
                Image = LoadImage("PlugINPRO.Resources.ES.CableTrayFitting16.png"),
                ToolTip = "Расстановка опор по всей длине лотка"
            });

            cableTraySupportsPulldownButton.AddPushButton(new PushButtonData(
                "CableTraySupportCopy",
                "Копировать опору",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.Commands.ES.CableTraySupportCopyCommand")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.ES.CableTrayFitting32.png"),
                Image = LoadImage("PlugINPRO.Resources.ES.CableTrayFitting16.png"),
                ToolTip = "Копирование опоры вдоль трассы лотка"
            });

            cableTraySupportsPulldownButton.AddPushButton(new PushButtonData(
                "CableTraySupportPlace",
                "Поставить опору",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.Commands.ES.CableTraySupportPlaceCommand")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.ES.CableTrayFitting32.png"),
                Image = LoadImage("PlugINPRO.Resources.ES.CableTrayFitting16.png"),
                ToolTip = "Установка опоры на лоток с заданным отступом"
            });

            // ============================================
            // ПАНЕЛЬ ВК (Водопровод и канализация)
            // ============================================
            RibbonPanel vkPanel = application.CreateRibbonPanel("INPRO", "ВК");

            // Кнопки
            PushButtonData pipeSupportsByLengthButtonData = new PushButtonData(
                "PipeSupportsByPipeLength",
                "Расстновка опор по всей длине трубопровода",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.Commands.VK.PipeSupportsByLengthCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.VK.PipeFitting32.png"),
                Image = LoadImage("PlugINPRO.Resources.VK.PipeFitting16.png"),
                ToolTip = "Расстановка опор по всей траектории трубопровода"
            };

            PushButtonData pipeSupportCopyButtonData = new PushButtonData(
                "PipeSupportCopy",
                "Копировать\nотносительно\nопоры",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.Commands.VK.PipeSupportCopyCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.VK.PipeFitting32.png"),
                Image = LoadImage("PlugINPRO.Resources.VK.PipeFitting16.png"),
                ToolTip = "Копирование опоры вдоль трассы трубы с заданным смещением"
            };

            PushButtonData pipeSupportPlaceButtonData = new PushButtonData(
                "PipeSupportPlace",
                "Поставить\nопору",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.Commands.VK.PipeSupportPlaceCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.VK.PipeFitting32.png"),
                Image = LoadImage("PlugINPRO.Resources.VK.PipeFitting16.png"),
                ToolTip = "Установка опоры на трубопровод с заданным отступом"
            };

            PushButtonData pipeSupportAlignZButtonData = new PushButtonData(
                "PipeSupportAlignZ",
                "Выравнивание\nпо Z",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.Commands.VK.PipeSupportAlignZCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.VK.PipeFitting32.png"),
                Image = LoadImage("PlugINPRO.Resources.VK.PipeFitting16.png"),
                ToolTip = "Выравнивание опор по оси трубы"
            };

            // Контейнер ВК
            PulldownButtonData pipeSupportPullData = new PulldownButtonData("PipeSupports", "Опоры\nтрубопроводов")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.VK.PipeFitting32.png"),
                ToolTip = "Инструменты по расстановке опор трубопроводов"
            };

            PulldownButton pipeSupportsPulldownButton = vkPanel.AddItem(pipeSupportPullData) as PulldownButton;

            pipeSupportsPulldownButton.AddPushButton(pipeSupportsByLengthButtonData);
            pipeSupportsPulldownButton.AddPushButton(pipeSupportCopyButtonData);
            pipeSupportsPulldownButton.AddPushButton(pipeSupportPlaceButtonData);
            pipeSupportsPulldownButton.AddPushButton(pipeSupportAlignZButtonData);

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
                LargeImage = LoadImage("PlugINPRO.Resources.VK.pipecolor32.png"),
                Image = LoadImage("PlugINPRO.Resources.VK.pipecolor16.png"),
                ToolTip = "Расчет расхода краски для системы ВК"
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
                LargeImage = LoadImage("PlugINPRO.Resources.VK.WaterCalc32.png"),
                Image = LoadImage("PlugINPRO.Resources.VK.WaterCalc16.png"),
                ToolTip = "Гидравлический расчёт выбранной системы"
            };

            PushButtonData hydraulicAddParamsButtonData = new PushButtonData(
                "HydraulicAddParams",
                "Добавить\nпараметры",
                assemblyPath,
                "PlugINPRO.Commands.VK.HydraulicAddParamsCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.VK.WaterCalc32.png"),
                Image = LoadImage("PlugINPRO.Resources.VK.WaterCalc16.png"),
                ToolTip = "Добавить параметры для гидравлического расчёта"
            };

            PulldownButtonData hydraulicPullData = new PulldownButtonData("HydraulicTools", "Гидравлический\nрасчёт")
            {
                LargeImage = LoadImage("PlugINPRO.Resources.VK.WaterCalc32.png"),
                ToolTip = "Инструменты гидравлического расчёта"
            };

            PulldownButton hydraulicPullButton = vkPanel.AddItem(hydraulicPullData) as PulldownButton;
            hydraulicPullButton.AddPushButton(hydraulicCalculateButtonData);
            hydraulicPullButton.AddPushButton(hydraulicAddParamsButtonData);

            // ============================================
            // ПАНЕЛЬ ТС (Тепловые сети)
            // ============================================
            RibbonPanel tsPanel = application.CreateRibbonPanel("INPRO", "ТС");

            PushButtonData dimensionButtonData = new PushButtonData(
                "AxonometricDimension",
                "Размеры на\nаксонометрии",
                assemblyPath,
                "PlugINPRO.Commands.TS.AxonometricDimensionCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.TS.dimensions32.png"),
                Image = LoadImage("PlugINPRO.Resources.TS.dimensions16.png"),
                ToolTip = "Расстановка размеров на аксонометрических видах"
            };
            tsPanel.AddItem(dimensionButtonData);

            PushButtonData settingsButtonData = new PushButtonData(
                "DimensionSettings",
                "Настройки\nразмеров",
                assemblyPath,
                "PlugINPRO.Commands.TS.DimensionSettingsCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.TS.settings32.png"),
                Image = LoadImage("PlugINPRO.Resources.TS.settings16.png"),
                ToolTip = "Настройка параметров простановки размеров"
            };
            tsPanel.AddItem(settingsButtonData);

            PushButtonData dimensionToGridsButtonData = new PushButtonData(
                "DimensionToGrids",
                "Пересечение\nосей",
                assemblyPath,
                "PlugINPRO.Commands.TS.DimensionToGridsCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.Common.grids32.png"),
                Image = LoadImage("PlugINPRO.Resources.Commongrids16.png"),
                ToolTip = "Размер от трубы/фитинга до пересечения осей"
            };
            tsPanel.AddItem(dimensionToGridsButtonData);

            PushButtonData pipeSlopeTriangleButtonData = new PushButtonData(
                "PipeSlopeTriangle",
                "Треугольник\nуклона",
                assemblyPath,
                "PlugINPRO.Commands.VK.PipeSlopeTriangleCommand"
            )
            {
                LargeImage = LoadImage("PlugINPRO.Resources.TS.slope32.png"),
                Image = LoadImage("PlugINPRO.Resources.TS.slope16.png"),
                ToolTip = "Построение треугольника уклона трубы"
            };
            tsPanel.AddItem(pipeSlopeTriangleButtonData);

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