using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Text;

namespace WpfApp1.Commands.Model
{
    public class FloorData
    {
        public Floor FloorElement { get; set; }
        public Transform LinkTransform { get; set; } // Transform của file link chứa sàn này
        public string SourceName { get; set; }        // Tên file nguồn (dùng để debug nếu cần)

        public void cal()
        {
            // Placeholder for any calculations or methods related to FloorData
        }
    }

}
