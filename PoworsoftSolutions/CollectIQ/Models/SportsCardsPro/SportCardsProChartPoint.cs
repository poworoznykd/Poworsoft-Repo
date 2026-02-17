using System;
using System.Collections.Generic;

namespace CollectIQ.Models.SportsCardsPro
{
    /// <summary>
    /// Represents a single time point in the chart.
    /// Chart history is not provided by the Prices API (per the docs you shared).
    /// </summary>
    public class SportCardsProChartPoint
    {
        public DateTime Date { get; set; }

        /// <summary>
        /// Price series by label (e.g., "Ungraded", "7", "8", "9", "BGS 9.5", "PSA 10").
        /// </summary>
        public Dictionary<string, decimal> PricesBySeries { get; set; } = new Dictionary<string, decimal>();

        public int? Volume { get; set; }
    }
}
