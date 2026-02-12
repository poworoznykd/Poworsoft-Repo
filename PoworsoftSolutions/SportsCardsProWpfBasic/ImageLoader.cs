using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace SportsCardsProWpfBasic
{
    public static class ImageLoader
    {
        private static readonly HttpClient _http = new HttpClient(new HttpClientHandler
        {
            UseCookies = false,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip |
        System.Net.DecompressionMethods.Deflate |
        System.Net.DecompressionMethods.Brotli
        });

        public static async Task<BitmapImage?> LoadBitmapFromUrlAsync(string url)
        {
            try
            {
                byte[] bytes = await _http.GetByteArrayAsync(url);

                using var ms = new MemoryStream(bytes);

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze(); // CRITICAL: allows using across threads safely

                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }


}