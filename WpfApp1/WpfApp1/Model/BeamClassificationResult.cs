using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.Revit.DB;

namespace WpfApp1.Model
{
    public class BeamClassificationResult
    {
        public ElementId BeamId { get; init; } = ElementId.InvalidElementId;
        public string BeamName { get; init; } = string.Empty;
        public int HostedRebarCount { get; set; }
        public int UpdatedCount { get; set; }
        public int SkippedCount { get; set; }
        public List<string> Messages { get; } = [];
    }
}
