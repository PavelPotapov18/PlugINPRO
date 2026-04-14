using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Windows.Interop;
using PlugINPRO.UI.Windows.PluginInfo;

namespace PlugINPRO.Commands.PluginInfo
{
    [Transaction(TransactionMode.Manual)]
    internal class InfoCommand : IExternalCommand
    {
        public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
        {
            InfoWindow window = new InfoWindow();

            // Получаем HWND окна Revit
            IntPtr revitHandle = commandData.Application.MainWindowHandle;

            // Назначаем владельца
            WindowInteropHelper helper = new WindowInteropHelper(window);
            helper.Owner = revitHandle;
            
            window.ShowDialog();
            return Result.Succeeded;
        }
    }
}
