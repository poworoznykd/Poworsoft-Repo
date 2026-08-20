namespace CollectIQ.Models.Inspection
{
    /// <summary>
    /// Contains the generated images and preliminary measurements from a
    /// directional surface inspection.
    /// </summary>
    public sealed class SurfaceInspectionResult
    {
        public SurfaceInspectionMode Mode { get; set; } = SurfaceInspectionMode.ExternalLight;
        public string ModeName { get; set; } = "External Light";
        public string DiffuseImagePath { get; set; } = string.Empty;
        public string AlbedoImagePath { get; set; } = string.Empty;
        public string ReliefImagePath { get; set; } = string.Empty;
        public string NormalMapImagePath { get; set; } = string.Empty;
        public string CurvatureImagePath { get; set; } = string.Empty;
        public string ReferenceResidualImagePath { get; set; } = string.Empty;
        public string HeatmapImagePath { get; set; } = string.Empty;
        public string DefectOverlayImagePath { get; set; } = string.Empty;
        public string DefectMaskImagePath { get; set; } = string.Empty;
        public string SecondaryEvidenceImagePath { get; set; } = string.Empty;
        public int DefectRegionCount { get; set; }
        public AlignmentDiagnostics Diagnostics { get; set; } = new();
        public double AnomalyScore { get; set; }
        public double CaptureConsistencyScore { get; set; }
        public string Summary { get; set; } = string.Empty;
    }
}
