namespace CollectIQ.Models.Inspection
{
    /// <summary>
    /// Contains the generated images and preliminary measurements from a
    /// directional surface inspection.
    /// </summary>
    public sealed class SurfaceInspectionResult
    {
        public string DiffuseImagePath { get; set; } = string.Empty;
        public string ReliefImagePath { get; set; } = string.Empty;
        public string SpecularDefectImagePath { get; set; } = string.Empty;
        public string HeatmapImagePath { get; set; } = string.Empty;
        public string DefectMaskImagePath { get; set; } = string.Empty;
        public string DefectOverlayImagePath { get; set; } = string.Empty;
        public AlignmentDiagnostics Diagnostics { get; set; } = new();
        public InspectionCardSurfaceProfile SurfaceProfile { get; set; } = InspectionCardSurfaceProfile.Normal;
        public double AnomalyScore { get; set; }
        public double CaptureConsistencyScore { get; set; }
        public string Summary { get; set; } = string.Empty;
    }
}
