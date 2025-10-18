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
using Microsoft.Maui.Media;
using System;
using System.Formats.Tar;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace CollectIQ.Views
{
    /// <summary>
    /// Page for creating or editing a single card with photo and eBay assist.
    /// </summary>
    public partial class AddCardPage : ContentPage
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
        /// <param name="database">Database service instance.</param>
        /// <param name="ebayService">Marketplace/eBay service instance.</param>
        /// <param name="imageStorage">Image storage service.</param>
        /// <param name="existing">Optional existing card (Edit mode when provided).</param>
        public AddCardPage(
            IDatabase database,
            IEbayService ebayService,
            IImageStorage imageStorage,
            Card? existing = null)
        {
            InitializeComponent();

            _database = database;
            _ebayService = ebayService;
            _imageStorage = imageStorage;

            _card = existing ?? new Card();
            Bind();
        }

        /// <summary>
        /// Applies the current card values to input fields and preview image.
        /// </summary>
        private void Bind()
        {
            PlayerEntry.Text = _card.Player;
            YearEntry.Text = _card.Year;
            SetEntry.Text = _card.Set;
            NumberEntry.Text = _card.Number;
            TeamEntry.Text = _card.Team;
            GradeCoEntry.Text = _card.GradeCompany;
            GradeEntry.Text = _card.Grade;
            PriceEntry.Text = _card.PurchasePrice == 0m ? string.Empty : _card.PurchasePrice.ToString();

            if (!string.IsNullOrWhiteSpace(_card.PhotoPath))
            {
                PreviewImage.Source = _card.PhotoPath;
            }
        }

        /// <summary>
        /// Captures a photo with the device camera and saves it locally.
        /// </summary>
        /// <param name="sender">Event source.</param>
        /// <param name="e">Event args.</param>
        private async void OnTakePhoto(object sender, EventArgs e)
        {
            try
            {
                FileResult? photo = await MediaPicker.CapturePhotoAsync(new MediaPickerOptions
                {
                    Title = "Card Photo"
                });

                if (photo == null)
                {
                    return;
                }

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
        /// Picks a photo from the device gallery and saves it locally.
        /// </summary>
        /// <param name="sender">Event source.</param>
        /// <param name="e">Event args.</param>
        private async void OnPickPhoto(object sender, EventArgs e)
        {
            try
            {
                FileResult? photo = await MediaPicker.PickPhotoAsync();
                if (photo == null)
                {
                    return;
                }

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
        /// Finds the best match on eBay for the entered description and previews the image.
        /// </summary>
        /// <param name="sender">Event source.</param>
        /// <param name="e">Event args.</param>
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
        /// Downloads the candidate eBay image (if any) and stores it locally as the card photo.
        /// </summary>
        /// <param name="sender">Event source.</param>
        /// <param name="e">Event args.</param>
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
        /// Saves the current card (insert or update) to the database and returns to the previous page.
        /// </summary>
        /// <param name="sender">Event source.</param>
        /// <param name="e">Event args.</param>
        private async void OnSave(object sender, EventArgs e)
        {
            _card.Player = PlayerEntry.Text?.Trim() ?? string.Empty;
            _card.Year = YearEntry.Text?.Trim() ?? string.Empty;
            _card.Set = SetEntry.Text?.Trim() ?? string.Empty;
            _card.Number = NumberEntry.Text?.Trim() ?? string.Empty;
            _card.Team = TeamEntry.Text?.Trim() ?? string.Empty;
            _card.GradeCompany = GradeCoEntry.Text?.Trim() ?? string.Empty;
            _card.Grade = GradeEntry.Text?.Trim() ?? string.Empty;

            _ = decimal.TryParse(PriceEntry.Text, out decimal price);
            _card.PurchasePrice = price;

            await _database.UpsertAsync(_card);
            await DisplayAlert("Saved", "Card saved.", "OK");
            await Navigation.PopAsync();
        }

        /// <summary>
        /// Deletes the current card (if persisted) and returns to the previous page.
        /// </summary>
        /// <param name="sender">Event source.</param>
        /// <param name="e">Event args.</param>
        private async void OnDelete(object sender, EventArgs e)
        {
            if (_card.Id == 0)
            {
                await Navigation.PopAsync();
                return;
            }

            bool confirm = await DisplayAlert("Delete", "Remove this card?", "Yes", "No");
            if (!confirm)
            {
                return;
            }

            await _database.DeleteAsync(_card);
            await Navigation.PopAsync();
        }
    }
}
