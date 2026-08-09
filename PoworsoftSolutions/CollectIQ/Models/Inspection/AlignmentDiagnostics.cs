namespace CollectIQ.Models.Inspection
{
    /// <summary>
    /// Shared alignment diagnostics model that can be reused by surface,
    /// centering, edge and corner inspections.
    /// </summary>
    public sealed class AlignmentDiagnostics
    {
        public List<InspectionDebugFrame> Frames { get; set; } = new();
        public string EdgeOverlayPath { get; set; } = string.Empty;
        public string AlignmentAveragePath { get; set; } = string.Empty;
    }
}
