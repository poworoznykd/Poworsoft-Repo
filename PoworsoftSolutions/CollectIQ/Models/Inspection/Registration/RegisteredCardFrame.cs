using CollectIQ.Models.Inspection.Geometry;

namespace CollectIQ.Models.Inspection.Registration
{
    public sealed class RegisteredCardFrame
    {
        public string Key { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string DetectionOverlayPath { get; set; } = string.Empty;
        public string RegisteredImagePath { get; set; } = string.Empty;
        public string EdgeImagePath { get; set; } = string.Empty;
        public CardGeometryResult Geometry { get; set; } = new();
        public float[] Luminance { get; set; } = Array.Empty<float>();
        public double AlignmentConfidence { get; set; }
        public double RotationDegrees { get; set; }
        public double Scale { get; set; } = 1.0;
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
    }
}
