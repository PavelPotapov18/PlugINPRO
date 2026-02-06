using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Reflection;
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

            // Создаём панель "Общие инструменты" на вкладке
            RibbonPanel commonPanel = application.CreateRibbonPanel("INPRO", "Общие инструменты");

            // Путь к текущей DLL
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // Кнопка "Проставление ревизий"
            PushButtonData revButtonData = new PushButtonData(
                "RevisionButton",
                "Ревизии\nи ИЗМы",
                assemblyPath,
                "PlugINPRO.RevIZMCommand"
            );
            revButtonData.LargeImage = LoadImage("PlugINPRO.Resources.R00_48.png");
            revButtonData.Image = LoadImage("PlugINPRO.Resources.R00_32.png");
            revButtonData.ToolTip = "Проставление ревизий и ИЗМов в штампе";

            PushButton revButton = commonPanel.AddItem(revButtonData) as PushButton;
            

            //Создаем панель ГЕНПЛАН
            RibbonPanel genplanPanel = application.CreateRibbonPanel("INPRO", "Генплан");

            // Кнопка "IFC по координатам"
            PushButtonData ifcButtonData = new PushButtonData(
                "IFCByCoordinates",
                "IFC по\nкоординатам",
                assemblyPath,
                "PlugINPRO.IFCByCoordinatesCommand"
            );
            ifcButtonData.LargeImage = LoadImage("PlugINPRO.Resources.ifc64.png");
            ifcButtonData.Image = LoadImage("PlugINPRO.Resources.ifc32.png");
            ifcButtonData.ToolTip = "Переместить выбранный IFC на введенные координаты";
            

            // Кнопка "Сбросить"
            PushButtonData resetButtonData = new PushButtonData(
                "ResetCoordinates",
                "Сбросить",
                assemblyPath,
                "PlugINPRO.ResetCoordinatesCommand"
            );
            resetButtonData.LargeImage = LoadImage("PlugINPRO.Resources.ifc64.png");
            resetButtonData.Image = LoadImage("PlugINPRO.Resources.ifc32.png");
            resetButtonData.ToolTip = "Установить все координаты в 0";

            
            //Кнопка - контейнер "IFC"(открывает список команд по работе с IFC)
            PulldownButtonData IfcPullData = new PulldownButtonData("IFCTools", "IFC");
            IfcPullData.LargeImage = LoadImage("PlugINPRO.Resources.ifc48.png");
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
            IfcRvtButtonData.LargeImage = LoadImage("PlugINPRO.Resources.ifc48.png");
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