namespace CollectIQ.Models.SportsCardsPro
{
    /// <summary>
    /// Represents the sold listing bucket counts shown in tabs (e.g., Ungraded Sold Listings (30)).
    /// This data is typically page/UI-driven and not guaranteed to be available via the Prices API.
    /// </summary>
    public class SportCardsProSoldListingBucket
    {
        public string? Label { get; set; }
        public int Count { get; set; }
    }
}
