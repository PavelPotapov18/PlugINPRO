using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media.Imaging;


namespace RevitHelloWorld
{
    public class App : IExternalApplication
    {

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
                "PlugINPRO.InfoCommand"
                );
            logoButtonData.LargeImage = LoadImage("PlugINPRO.Resources.info32.png");
            logoButtonData.ToolTip = "PlugINPRO v1.0";
            logoButtonData.LongDescription = "О плагине";

            infoPanel.AddItem(logoButtonData);


            // Создаём панель "Общие инструменты" на вкладке
            RibbonPanel commonPanel = application.CreateRibbonPanel("INPRO", "Общие инструменты");
            
            // Кнопка "Проставление ревизий"
            PushButtonData revButtonData = new PushButtonData(
                "RevisionButton",
                "Ревизии\nи ИЗМы",
                assemblyPath,
                "PlugINPRO.RevIZMCommand"
            );
            revButtonData.LargeImage = LoadImage("PlugINPRO.Resources.R00_32.png");
            revButtonData.Image = LoadImage("PlugINPRO.Resources.R00_16.png");
            revButtonData.ToolTip = "Проставление ревизий и ИЗМов в штампе";

            commonPanel.AddItem(revButtonData);
            

            //Создаем панель ГЕНПЛАН
            RibbonPanel genplanPanel = application.CreateRibbonPanel("INPRO", "Генплан");

            // Кнопка "IFC по координатам"
            PushButtonData ifcButtonData = new PushButtonData(
                "IFCByCoordinates",
                "IFC по\nкоординатам",
                assemblyPath,
                "PlugINPRO.IFCByCoordinatesCommand"
            );
            ifcButtonData.LargeImage = LoadImage("PlugINPRO.Resources.ifc32.png");
            ifcButtonData.Image = LoadImage("PlugINPRO.Resources.ifc16.png");
            ifcButtonData.ToolTip = "Переместить выбранный IFC на введенные координаты";
            

            // Кнопка "Сбросить"
            PushButtonData resetButtonData = new PushButtonData(
                "ResetCoordinates",
                "Сбросить",
                assemblyPath,
                "PlugINPRO.ResetCoordinatesCommand"
            );
            resetButtonData.LargeImage = LoadImage("PlugINPRO.Resources.ifc32.png");
            resetButtonData.Image = LoadImage("PlugINPRO.Resources.ifc16.png");
            resetButtonData.ToolTip = "Установить все координаты в 0";

            
            //Кнопка - контейнер "IFC"(открывает список команд по работе с IFC)
            PulldownButtonData IfcPullData = new PulldownButtonData("IFCTools", "IFC");
            IfcPullData.LargeImage = LoadImage("PlugINPRO.Resources.ifc32.png");
            IfcPullData.ToolTip = "Работа по переносу фалов IFC";

            PulldownButton IfcPullButton = genplanPanel.AddItem(IfcPullData) as PulldownButton;

            IfcPullButton.AddPushButton(ifcButtonData);
            IfcPullButton.AddPushButton(resetButtonData);
            



            //Создаем панель BIM
            RibbonPanel bimPanel = application.CreateRibbonPanel("INPRO", "BIM");

            // Кнопка "IFC to RVT"
            PushButtonData IfcRvtButtonData = new PushButtonData(
                "IFСtoRVT",
                "IFC в RVT",
                assemblyPath,
                "PlugINPRO.IFCtoRVTCommand"
            );
            IfcRvtButtonData.LargeImage = LoadImage("PlugINPRO.Resources.ifc32.png");
            IfcRvtButtonData.Image = LoadImage("PlugINPRO.Resources.ifc16.png");
            IfcRvtButtonData.ToolTip = "Перенос IFC файлов в формат RVT с изменением свойств";
            bimPanel.AddItem(IfcRvtButtonData);

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