using System;
using System.Windows.Media.Imaging;

namespace SportsCardsProWpfBasic
{
    public static class ImageHelper
    {
        public static BitmapImage CreateBitmap(string url)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(url, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.EndInit();
            bmp.Freeze(); // safe across threads
            return bmp;
        }
    }
}