using Autodesk.Revit.UI;
using System.Reflection;

namespace RebarTag.Application
{
    /// <summary>Creates the Rebar Tag ribbon command when Revit starts.</summary>
    public class RebarTagApplication : IExternalApplication
    {
        private const string TabName = "BIM Tools";
        private const string PanelName = "Reinforcement";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch
            {
                // The tab may already be created by another add-in during startup.
            }

            RibbonPanel panel = application.CreateRibbonPanel(TabName, PanelName);
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            PushButtonData button = new PushButtonData(
                "AutoRebarTag",
                "Auto\nRebar Tag",
                assemblyPath,
                "RebarTag.Cmd.RebarTagCheckerCmd")
            {
                ToolTip = "Select a rebar host, then click where its rebar tags should be placed."
            };

            panel.AddItem(button);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
    }
}
