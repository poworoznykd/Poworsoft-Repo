namespace CollectIQ.Models.Inspection.Registration
{
    public sealed class CardRegistrationResult
    {
        public Dictionary<string, RegisteredCardFrame> Frames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string EdgeOverlayPath { get; set; } = string.Empty;
        public string AlignmentAveragePath { get; set; } = string.Empty;
        public double OverallQuality { get; set; }
    }
}
