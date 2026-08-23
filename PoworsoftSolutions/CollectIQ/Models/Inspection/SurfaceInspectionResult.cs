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
        public List<TiltViewDiagnostic> TiltViews { get; set; } = new();
        public AlignmentDiagnostics Diagnostics { get; set; } = new();
        public double AnomalyScore { get; set; }
        public double CaptureConsistencyScore { get; set; }
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>
    /// Visual proof that one Tilt Sweep capture was (or was not) rectified
    /// onto the same four canonical card points as every other tilt view.
    /// </summary>
    public sealed class TiltViewDiagnostic
    {
        public int CaptureNumber { get; set; }
        public bool Accepted { get; set; }
        public double GeometryConfidence { get; set; }
        public double AlignmentConfidence { get; set; }
        public bool Rotated180 { get; set; }
        public string AlignedImagePath { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
