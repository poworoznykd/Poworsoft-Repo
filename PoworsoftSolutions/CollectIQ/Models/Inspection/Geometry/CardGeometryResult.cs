namespace CollectIQ.Models.Inspection.Geometry
{
    public sealed class CardGeometryResult
    {
        public bool Success { get; set; }
        public CardPoint[] Corners { get; set; } = Array.Empty<CardPoint>();
        public double Confidence { get; set; }
        public int SourceWidth { get; set; }
        public int SourceHeight { get; set; }
    }
}
