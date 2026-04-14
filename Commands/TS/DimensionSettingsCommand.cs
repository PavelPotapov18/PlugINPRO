using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PlugINPRO.UI.Windows.TS;
using System;

namespace PlugINPRO.Commands.TS
{
    [Transaction(TransactionMode.ReadOnly)]
    public class DimensionSettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;

                DimensionSettings settingsWindow = new DimensionSettings(uiApp);
                settingsWindow.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}