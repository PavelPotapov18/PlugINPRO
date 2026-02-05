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

        // Статическая переменная для хранения выбранного семейства
        public static FamilySymbol SelectedSupportFamily { get; set; }

        // ============================================
        // ДОБАВЛЕННЫЕ ПОЛЯ ДЛЯ ХРАНЕНИЯ РАЗМЕРОВ ВОЗДУХОВОДА
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

            // НОВЫЙ КОД ДЛЯ ПАНЕЛИ ОВ - НАЧАЛО
            // ============================================
            // ДОБАВЛЯЕМ ПАНЕЛЬ "ОВ" (Отопление и Вентиляция)
            // ============================================
            RibbonPanel ovPanel = application.CreateRibbonPanel("INPRO", "ОВ");

            // Единственная кнопка "Опоры воздуховодов" - все в одном окне
            PushButtonData ductSupportsButtonData = new PushButtonData(
                "DuctSupports",
                "Опоры\nвоздуховодов",
                Assembly.GetExecutingAssembly().Location,
                "PlugINPRO.DuctSupportsCommand"
            );
            ductSupportsButtonData.ToolTip = "Расстановка опор по траектории воздуховода\nВключает выбор крепления и шага";
            ovPanel.AddItem(ductSupportsButtonData);
            // НОВЫЙ КОД ДЛЯ ПАНЕЛИ ОВ - КОНЕЦ

            return Result.Succeeded;

        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}