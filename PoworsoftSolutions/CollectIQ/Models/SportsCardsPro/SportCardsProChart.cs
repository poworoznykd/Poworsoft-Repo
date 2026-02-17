using System.Collections.Generic;

namespace CollectIQ.Models.SportsCardsPro
{
    /// <summary>
    /// Container for chart series labels and points.
    /// Chart history is not provided by the Prices API (per the docs you shared).
    /// </summary>
    public class SportCardsProChart
    {
        public List<string> SeriesLabels { get; set; } = new List<string>();
        public List<SportCardsProChartPoint> Points { get; set; } = new List<SportCardsProChartPoint>();
    }
}
