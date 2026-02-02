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

        public Result OnStartup(UIControlledApplication application)
        {
            string tabName = "INPRO";
            string panelName = "Общие инструменты";

            // Пытаемся создать вкладку (если уже есть — Revit кинет исключение)
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
                // вкладка уже существует — это нормально
            }

            // Создаём панель на вкладке
            RibbonPanel panel = application.CreateRibbonPanel(tabName, panelName);

            // Путь к текущей DLL
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // Кнопка
            PushButtonData buttonData = new PushButtonData(
                "RevisionButton",
                "Проставление ревизий",
                assemblyPath,
                "PlugINPRO.RevIZMSetting"
            );

            PushButton button = panel.AddItem(buttonData) as PushButton;
            button.ToolTip = "Проставление ревизий и ИЗМов в штампе";

            // 3. Создаем панель ГЕНПЛАН
            CreateGenplanPanel(application);

            return Result.Succeeded;

        }

        private void CreateGenplanPanel(UIControlledApplication app)
        {
            // Создаем панель ГЕНПЛАН
            RibbonPanel genplanPanel = app.CreateRibbonPanel("INPRO", "ГЕНПЛАН");

            // --- ТЕКСТОВЫЕ ПОЛЯ ДЛЯ КООРДИНАТ ---

            // Поле для X
            TextBoxData txtXData = new TextBoxData("txtX");
            txtXData.Name = "X (мм)";
            txtXData.ToolTip = "Координата X в миллиметрах";
            txtX = genplanPanel.AddItem(txtXData) as TextBox;
            if (txtX != null)
            {
                txtX.Value = "0"; // Это строка, все правильно
                txtX.ToolTip = "Координата X в мм";
                txtX.Width = 80;
            }
            
            // Поле для Y
            TextBoxData txtYData = new TextBoxData("txtY");
            txtYData.Name = "Y (мм)";
            txtYData.ToolTip = "Координата Y в миллиметрах";
            txtY = genplanPanel.AddItem(txtYData) as TextBox;
            if (txtY != null)
            {
                txtY.Value = "0";
                txtY.ToolTip = "Координата Y в мм";
                txtY.Width = 80;
            }

            // Поле для Z
            TextBoxData txtZData = new TextBoxData("txtZ");
            txtZData.Name = "Z (мм)";
            txtZData.ToolTip = "Координата Z в миллиметрах";
            txtZ = genplanPanel.AddItem(txtZData) as TextBox;
            if (txtZ != null)
            {
                txtZ.Value = "0";
                txtZ.ToolTip = "Координата Z в мм";
                txtZ.Width = 80;
            }

            // Разделитель
            genplanPanel.AddSeparator();

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
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
