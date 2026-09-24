using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PREFAB
{
    public class ExternalApplication : IExternalApplication
    {
        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
        public Result OnStartup(UIControlledApplication application)
        {
            string tab = "PREFAB";
            try
            {
                application.CreateRibbonTab(tab);
                application.CreateRibbonPanel(tab, "For Dimension");
                application.CreateRibbonPanel(tab, "For Model");
                application.CreateRibbonPanel(tab, "For Checking");
            }
            catch (Exception)
            {
                TaskDialog.Show("Error!", "Duplicate ribbon tab!");
            }
            return Result.Succeeded;
        }
        
    }
}
