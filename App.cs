using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Reflection;

namespace RevitHelloWorld
{
    public class App : IExternalApplication
    {
        public static TextBox txtX;
        public static TextBox txtY;
        public static TextBox txtZ;

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

            // Создаём панель "Общие инструменты" на вкладке
            RibbonPanel panel = application.CreateRibbonPanel("INPRO", "Общие инструменты");

            // Путь к текущей DLL
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // Кнопка "Проставление ревизий"
            PushButtonData buttonData = new PushButtonData(
                "RevisionButton",
                "Проставление ревизий",
                assemblyPath,
                "PlugINPRO.RevIZMCommand"
            );

            PushButton button = panel.AddItem(buttonData) as PushButton;
            button.ToolTip = "Проставление ревизий и ИЗМов в штампе";

            //Создаем панель ГЕНПЛАН
            RibbonPanel genplanPanel = application.CreateRibbonPanel("INPRO", "ГЕНПЛАН");

            // --- КНОПКИ ---

            // Кнопка "IFC по координатам"
            PushButtonData ifcButtonData = new PushButtonData(
                "IFCByCoordinates",
                "IFC по\nкоординатам",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.IFCByCoordinatesCommand"
            );
            ifcButtonData.ToolTip = "Переместить выбранный IFC на введенные координаты";
            genplanPanel.AddItem(ifcButtonData);

            // Кнопка "Сбросить"
            PushButtonData resetButtonData = new PushButtonData(
                "ResetCoordinates",
                "Сбросить",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.ResetCoordinatesCommand"
            );
            resetButtonData.ToolTip = "Установить все координаты в 0";
            genplanPanel.AddItem(resetButtonData);

            //Создаем панель BIM
            RibbonPanel bimPanel = application.CreateRibbonPanel("INPRO", "BIM");

            // Кнопка "IFC to RVT"
            PushButtonData IfcRvtButtonData = new PushButtonData(
                "IFСtoRVT",
                "IFC в RVT",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.IFCtoRVTCommand"
            );
            IfcRvtButtonData.ToolTip = "Перенос IFC файлов в формат RVT с изменением свойств";
            bimPanel.AddItem(IfcRvtButtonData);

            // ============================================
            // ПАНЕЛЬ ОВ (Отопление и Вентиляция)
            // ============================================
            RibbonPanel ovPanel = application.CreateRibbonPanel("INPRO", "ОВ");

            // Кнопка "Опоры воздуховодов"
            PushButtonData ductSupportsButtonData = new PushButtonData(
                "DuctSupports",
                "Опоры\nвоздуховодов",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.DuctSupportsCommand"
            );
            ductSupportsButtonData.ToolTip = "Расстановка опор по траектории воздуховода\nВключает выбор крепления и шага";
            ovPanel.AddItem(ductSupportsButtonData);
            // ============================================

            // ============================================
            // НОВАЯ ПАНЕЛЬ ЭОМ (Электромонтаж)
            // ============================================
            RibbonPanel eomPanel = application.CreateRibbonPanel("INPRO", "ЭОМ");

            // Кнопка "Опоры лотков"
            PushButtonData cableTraySupportsButtonData = new PushButtonData(
                "CableTraySupports",
                "Опоры\nлотков",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.CableTraySupportsCommand"
            );
            cableTraySupportsButtonData.ToolTip = "Расстановка опор по траектории кабельного лотка\nВключает выбор крепления и шага";
            eomPanel.AddItem(cableTraySupportsButtonData);
            // ============================================

            // ============================================
            // НОВАЯ ПАНЕЛЬ ВК (Водопровод и канализация)
            // ============================================
            RibbonPanel vkPanel = application.CreateRibbonPanel("INPRO", "ВК");

            // Кнопка "Опоры трубопроводов"
            PushButtonData pipeSupportsButtonData = new PushButtonData(
                "PipeSupports",
                "Опоры\nтрубопроводов",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.PipeSupportsCommand"
            );
            pipeSupportsButtonData.ToolTip = "Расстановка опор по траектории трубопровода\nВключает выбор крепления и шага";
            vkPanel.AddItem(pipeSupportsButtonData);
            // ============================================

            return Result.Succeeded;

        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}