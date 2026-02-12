using System.Windows.Media.Imaging;

namespace SportsCardsProWpfBasic
{
    public class SearchItem
    {
        public string Id { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string ConsoleName { get; set; } = "";
        public string Genre { get; set; } = "";

        // this is the KEY FIX: bind Image.Source to an ImageSource, not a string
        public BitmapImage? ThumbnailImage { get; set; }

        public string DetailsJson { get; set; } = "";
    }

}