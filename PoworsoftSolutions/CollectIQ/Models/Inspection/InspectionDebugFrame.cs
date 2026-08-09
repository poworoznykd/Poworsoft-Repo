namespace CollectIQ.Models.Inspection
{
    /// <summary>
    /// Holds per-capture diagnostics that explain how one inspection frame was
    /// detected, normalized and aligned.
    /// </summary>
    public sealed class InspectionDebugFrame
    {
        public SurfaceLightDirection Direction { get; set; }
        public string DetectionOverlayPath { get; set; } = string.Empty;
        public string RegisteredImagePath { get; set; } = string.Empty;
        public string EdgeImagePath { get; set; } = string.Empty;
        public double DetectionConfidence { get; set; }
        public double AlignmentConfidence { get; set; }
        public double RotationDegrees { get; set; }
        public double Scale { get; set; } = 1.0;
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
    }
}
