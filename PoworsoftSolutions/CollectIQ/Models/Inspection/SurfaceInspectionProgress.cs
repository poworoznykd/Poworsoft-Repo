namespace CollectIQ.Models.Inspection
{
    /// <summary>
    /// User-visible progress emitted by the five-image photometric inspection.
    /// Image paths are optional and are populated as soon as each intermediate
    /// diagnostic becomes available.
    /// </summary>
    public sealed class SurfaceInspectionProgress
    {
        public int Percent { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string FrameKey { get; set; } = string.Empty;
        public string SourceImagePath { get; set; } = string.Empty;
        public string OverlayImagePath { get; set; } = string.Empty;
        public string RegisteredImagePath { get; set; } = string.Empty;
        public string CombinedImagePath { get; set; } = string.Empty;
        public string DerivedImagePath { get; set; } = string.Empty;
    }
}
