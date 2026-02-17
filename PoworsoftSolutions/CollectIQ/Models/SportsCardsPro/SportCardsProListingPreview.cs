namespace CollectIQ.Models.SportsCardsPro
{
    /// <summary>
    /// Represents a horizontal listing preview (e.g., an eBay carousel item) containing title, price and image.
    /// This data is typically page/UI-driven and not guaranteed to be available via the Prices API.
    /// </summary>
    public class SportCardsProListingPreview
    {
        public string? Title { get; set; }
        public decimal? Price { get; set; }
        public string? ShippingText { get; set; }
        public string? Url { get; set; }
        public string? ImageUrl { get; set; }
        public string? Source { get; set; }
    }
}
