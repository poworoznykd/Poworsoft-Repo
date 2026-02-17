namespace CollectIQ.Models.SportsCardsPro
{
    /// <summary>
    /// Represents a grade/condition tile on the item page (e.g., Ungraded, Grade 9, PSA 10),
    /// including display price, delta, volume text, and buy/sell recommendations.
    /// This data is not guaranteed to be available via the Prices API.
    /// </summary>
    public class SportCardsProGradePanel
    {
        public string? Label { get; set; }

        public decimal? DisplayPrice { get; set; }
        public decimal? PriceChange { get; set; }

        public string? VolumeText { get; set; }

        public decimal? BuyPrice { get; set; }
        public decimal? SellPrice { get; set; }

        // Optional raw pennies if you prefer storing API-like values
        public long? DisplayPricePennies { get; set; }
        public long? BuyPricePennies { get; set; }
        public long? SellPricePennies { get; set; }
    }
}
