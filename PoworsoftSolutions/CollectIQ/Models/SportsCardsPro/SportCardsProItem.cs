using System;

namespace CollectIQ.Models.SportsCardsPro
{
    // This is the object your Insights page binds to.
    // It contains the raw API snapshot plus a couple convenience fields.
    public class SportCardsProItem
    {
        public string ItemPageUrl { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;

        public SportCardsProPricesSnapshot CardSnapShot { get; set; } = new SportCardsProPricesSnapshot();

        public DateTime RetrievedUtc { get; set; } = DateTime.UtcNow;
    }

}