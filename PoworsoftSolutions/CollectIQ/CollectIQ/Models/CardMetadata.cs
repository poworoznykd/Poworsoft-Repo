// FILE: CardMetadata.cs
// PROJECT: CollectIQ
// DESCRIPTION:
// Represents structured information parsed from OCR text.

namespace CollectIQ.Models
{
    public class CardMetadata
    {
        public string? Year { get; set; }
        public string? Brand { get; set; }
        public string? Series { get; set; }
        public string? Player { get; set; }
        public string? Team { get; set; }
        public string? Sport { get; set; }
        public string? CardNumber { get; set; }
        public string? RawText { get; set; }

        public override string ToString()
        {
            // Build compact query text
            var parts = new List<string?> { Year, Brand, Series, Player, CardNumber };
            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))).Trim();
        }
    }
}
