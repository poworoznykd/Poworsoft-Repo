//
//  FILE            : AddCardPage.xaml.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : <Your Name>
//  FIRST VERSION   : 2025-10-18
//  DESCRIPTION     :
//      Code-behind for the Add/Edit Card screen. Supports camera capture,
//      gallery picking, eBay-assisted search, and saving an attached image
//      locally for the card record.
//
using CollectIQ.Interfaces;
using CollectIQ.Models;
using CollectIQ.Services;
using Microsoft.Maui.Media;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    /// <summary>
    /// Page for creating or editing a single card with photo and eBay assist.
    /// </summary>
    public partial class CardPage : ContentPage
    {
        private readonly IDatabase _database;
        private readonly IEbayService _ebayService;
        private readonly IImageStorage _imageStorage;

        private Card _card;
        private string? _candidateEbayImageUrl;
        private EbayListing? _lastEbayListing;

        /// <summary>
        /// Initializes a new instance of the <see cref="AddCardPage"/> class.
        /// </summary>
        public CardPage(Card? existing = null)
        {
            InitializeComponent();

            _database = App.Database; 
            _ebayService = new EbayService(); 
            _imageStorage = new ImageStorage();
            _card = existing ?? new Card();
            Bind();
        }

        /// <summary>
        /// Binds current card values to UI controls.
        /// </summary>
        private void Bind()
        {
            PlayerEntry.Text = _card.Player;
            YearEntry.Text = _card.Year.ToString();
            SetEntry.Text = _card.Set;
            NumberEntry.Text = _card.Number;
            TeamEntry.Text = _card.Team;
            GradeCoEntry.Text = _card.GradeCompany;
            GradeEntry.Text = _card.Grade?.ToString() ?? string.Empty;
            PriceEntry.Text = _card.PurchasePrice?.ToString() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(_card.PhotoPath))
            {
                PreviewImage.Source = _card.PhotoPath;
            }
        }

        /// <summary>
        /// Captures a photo using the device camera.
        /// </summary>
        private async void OnTakePhoto(object sender, EventArgs e)
        {
            try
            {
                FileResult? photo = await MediaPicker.CapturePhotoAsync(new MediaPickerOptions
                {
                    Title = "Card Photo"
                });

                if (photo == null)
                    return;

                using Stream stream = await photo.OpenReadAsync();
                string path = await _imageStorage.SaveAsync(stream, ".jpg");

                _card.PhotoPath = path;
                PreviewImage.Source = path;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Camera Error", ex.Message, "OK");
            }
        }

        /// <summary>
        /// Allows the user to pick a photo from the gallery.
        /// </summary>
        private async void OnPickPhoto(object sender, EventArgs e)
        {
            try
            {
                FileResult? photo = await MediaPicker.PickPhotoAsync();
                if (photo == null)
                    return;

                using Stream stream = await photo.OpenReadAsync();
                string path = await _imageStorage.SaveAsync(stream, ".jpg");

                _card.PhotoPath = path;
                PreviewImage.Source = path;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Gallery Error", ex.Message, "OK");
            }
        }

        /// <summary>
        /// Searches eBay for listings matching the query entered by the user.
        /// </summary>
        private async void OnFindEbay(object sender, EventArgs e)
        {
            string query = EbayQueryEntry.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                await DisplayAlert("Search", "Enter a description to search.", "OK");
                return;
            }

            try
            {
                _lastEbayListing = await _ebayService.GetBestMatchAsync(query);
                if (_lastEbayListing == null)
                {
                    EbayResultLabel.Text = "No result found.";
                    _candidateEbayImageUrl = null;
                    return;
                }

                _candidateEbayImageUrl = _lastEbayListing.ImageUrl;

                string priceText = _lastEbayListing.Price.HasValue
                    ? $"{_lastEbayListing.Price.Value:#,0.00} {_lastEbayListing.Currency}"
                    : "N/A";

                EbayResultLabel.Text = $"Found: {_lastEbayListing.Title}\nPrice: {priceText}";

                if (!string.IsNullOrWhiteSpace(_candidateEbayImageUrl))
                {
                    PreviewImage.Source = ImageSource.FromUri(new Uri(_candidateEbayImageUrl));
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("eBay Error", ex.Message, "OK");
            }
        }

        /// <summary>
        /// Uses the image from the last found eBay listing as the card image.
        /// </summary>
        private async void OnUseEbayImage(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_candidateEbayImageUrl))
            {
                await DisplayAlert("Image", "No eBay image selected. Run 'Find on eBay' first.", "OK");
                return;
            }

            try
            {
                using HttpClient http = new HttpClient();
                using Stream stream = await http.GetStreamAsync(_candidateEbayImageUrl);
                string path = await _imageStorage.SaveAsync(stream, ".jpg");

                _card.PhotoPath = path;
                PreviewImage.Source = path;

                if (_lastEbayListing?.Price is decimal p && string.IsNullOrWhiteSpace(PriceEntry.Text))
                {
                    PriceEntry.Text = p.ToString();
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Download Error", ex.Message, "OK");
            }
        }

        /// <summary>
        /// Saves or updates the current card to the database.
        /// </summary>
        private async void OnSave(object sender, EventArgs e)
        {
            _card.Player = PlayerEntry.Text?.Trim() ?? string.Empty;
            _card.Set = SetEntry.Text?.Trim() ?? string.Empty;
            _card.Number = NumberEntry.Text?.Trim() ?? string.Empty;
            _card.Team = TeamEntry.Text?.Trim() ?? string.Empty;
            _card.GradeCompany = GradeCoEntry.Text?.Trim() ?? string.Empty;

            if (int.TryParse(YearEntry.Text, out int year))
                _card.Year = year;

            if (double.TryParse(GradeEntry.Text, out double grade))
                _card.Grade = grade;

            if (decimal.TryParse(PriceEntry.Text, out decimal price))
                _card.PurchasePrice = price;

            await _database.UpsertAsync(_card);
            await DisplayAlert("Saved", "Card saved.", "OK");
            await Navigation.PopAsync();
        }

        /// <summary>
        /// Deletes the current card and returns to the previous page.
        /// </summary>
        private async void OnDelete(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_card.Id))
            {
                await Navigation.PopAsync();
                return;
            }

            bool confirm = await DisplayAlert("Delete", "Remove this card?", "Yes", "No");
            if (!confirm)
                return;

            await _database.DeleteAsync<Card>(_card.Id);
            await Navigation.PopAsync();
        }
    }
}
