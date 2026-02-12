using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace SportsCardsProWpfBasic
{
    public partial class MainWindow : Window
    {
        // Put your token here
        private const string ApiToken = "0bb25e9f33af5c0eff9000eda762b5d15410ccf3";

        private readonly SportsCardsProClient _api = new SportsCardsProClient(ApiToken);

        private readonly ObservableCollection<SearchItem> _results = new ObservableCollection<SearchItem>();

        public MainWindow()
        {
            InitializeComponent();
            ResultsListBox.ItemsSource = _results;
            QueryTextBox.Text = "tom brady rookie 2000 bowman #236";
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await RunSearchAsync();
        }

        private async void QueryTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await RunSearchAsync();
        }

        private async Task RunSearchAsync()
        {
            string q = (QueryTextBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q))
                return;

            StatusText.Text = "Searching...";
            SearchButton.IsEnabled = false;
            _results.Clear();

            ClearDetails();

            try
            {
                var items = await _api.SearchProductsAsync(q);

                foreach (var it in items)
                    _results.Add(it);

                StatusText.Text = $"Results: {_results.Count}";
                if (_results.Count > 0)
                    ResultsListBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Search failed.";
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SearchButton.IsEnabled = true;
            }
        }

        private async void ResultsListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ResultsListBox.SelectedItem is not SearchItem item)
                return;

            StatusText.Text = "Loading details...";
            try
            {
                var detailJson = await _api.GetProductDetailsJsonAsync(item.Id);

                item.DetailsJson = detailJson;

                // show all fields
                AllFieldsTextBox.Text = PrettyPrintJson(detailJson);

                // basic title
                SelectedTitle.Text = item.ProductName;
                SelectedSubTitle.Text = $"{item.ConsoleName}   |   {item.Genre}   |   id={item.Id}";

                // prices summary (best-effort; keys vary)
                SelectedPrices.Text = BuildPriceSummary(detailJson);

                // try image from HTML page og:image
                var imgUrl = await _api.TryGetOgImageUrlAsync(item.Id, item.ConsoleName, item.ProductName);
                SelectedImageUrl.Text = string.IsNullOrWhiteSpace(imgUrl) ? "Image: (none found)" : $"Image: {imgUrl}";

                if (!string.IsNullOrWhiteSpace(imgUrl))
                {
                    BitmapImage? bmp = await ImageLoader.LoadBitmapFromUrlAsync(imgUrl);
                    LargeImage.Source = bmp;
                    item.ThumbnailImage = bmp; // reuse for list
                    ResultsListBox.Items.Refresh();
                }
                else
                {
                    LargeImage.Source = null;
                }

                StatusText.Text = "Ready.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Failed loading details.";
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearDetails()
        {
            SelectedTitle.Text = "Select a card";
            SelectedSubTitle.Text = "";
            SelectedPrices.Text = "";
            SelectedImageUrl.Text = "";
            AllFieldsTextBox.Text = "";
            LargeImage.Source = null;
        }

        private static string PrettyPrintJson(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }

        private static string BuildPriceSummary(string detailJson)
        {
            using var doc = JsonDocument.Parse(detailJson);
            var root = doc.RootElement;

            string GetMoney(string key)
            {
                if (!root.TryGetProperty(key, out var el))
                    return "(n/a)";

                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int pennies))
                    return $"${pennies / 100.0:0.00}";

                if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out int p2))
                    return $"${p2 / 100.0:0.00}";

                return el.ToString() ?? "(n/a)";
            }

            var sb = new StringBuilder();
            sb.AppendLine("Prices (from API):");
            sb.AppendLine($"  Ungraded (loose-price): {GetMoney("loose-price")}");
            sb.AppendLine($"  Graded 8/8.5 (new-price): {GetMoney("new-price")}");
            sb.AppendLine($"  Graded 7/7.5 (cib-price): {GetMoney("cib-price")}");
            sb.AppendLine($"  PSA 10 (manual-only-price): {GetMoney("manual-only-price")}");
            sb.AppendLine($"  BGS 10 (bgs-10-price): {GetMoney("bgs-10-price")}");
            sb.AppendLine($"  CGC 10 (condition-17-price): {GetMoney("condition-17-price")}");
            sb.AppendLine($"  SGC 10 (condition-18-price): {GetMoney("condition-18-price")}");
            sb.AppendLine($"  Sales volume: {(root.TryGetProperty("sales-volume", out var sv) ? sv.ToString() : "(n/a)")}");
            return sb.ToString();
        }
    }
}