using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls.Ribbon;
using System.Windows.Media.Imaging;
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
            try
            {
                application.CreateRibbonTab("PREFAB");
                string assemblyPath = @"C:\Users\tran_huyenmy\AppData\Roaming\Autodesk\Revit\Addins\2026\PREFAB.dll";
                var panel = application.CreateRibbonPanel("PREFAB", "For Modeling");
                PushButtonData item0 = new PushButtonData("1", "Model", assemblyPath, "PREFAB.Commands.ForModeling");
                PushButtonData item1 = new PushButtonData("2", "Check", assemblyPath, "PREFAB.Commands.ForChecking");
                PushButtonData item2 = new PushButtonData("3", "Dim", assemblyPath, "PREFAB.Commands.ForDimension");
                item0.LargeImage = new BitmapImage(new Uri(@"E:\\CODE\\REVIT_TOOLS\\WpfApp1\\WpfApp1\\Resource\\Image\house.ico"));
                item1.LargeImage = new BitmapImage(new Uri(@"E:\\CODE\\REVIT_TOOLS\\WpfApp1\\WpfApp1\\Resource\\Image\glasses.ico"));
                item2.LargeImage = new BitmapImage(new Uri(@"E:\\CODE\\REVIT_TOOLS\\WpfApp1\\WpfApp1\\Resource\\Image\pencil.ico"));
                item0.Text = "Beam";
                item1.Text = "Dim";
                item2.Text = "Check";
                panel.AddItem(item0);
                panel.AddItem(item1);
                panel.AddItem(item2);
                RibbonButtonCreate(application);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error!",ex.ToString());
            }
            return Result.Succeeded;
        }
        public void RibbonButtonCreate(UIControlledApplication application)
        {

        }

    }

}
