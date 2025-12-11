/*
* FILE            : HighlightPlayerPage.xaml.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2025-12-12
* UPDATED         : 2025-12-13
* DESCRIPTION     :
*     CollectIQ-styled highlight control room for a card.
*
*     This page DOES NOT embed the YouTube player (to avoid 152-4 errors).
*     Instead, it:
*         - Shows a neon pseudo-player with a YouTube thumbnail.
*         - Displays key card details (name, subtitle, grade, est. value).
*         - Displays highlight clip info (position + description).
*         - Lets the user move between clips using:
*               * "‹" and "›" nav buttons overlaid on the player
*               * A horizontal strip of glowing clip chips
*         - Provides:
*               * "Play"  -> open current clip via Launcher (YouTube app/browser)
*               * "Copy"  -> copy link to clipboard
*               * "Share" -> invoke platform share sheet
*
*     Result: feels like an in-app highlight hub, while actual playback
*     happens in the dedicated YouTube environment (most reliable).
*/

using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CollectIQ.Models;
using CollectIQ.Models.Domain.Entities;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;

namespace CollectIQ.Views
{
    /// <summary>
    /// CollectIQ-styled highlight control room for a YouTube-based reel.
    /// </summary>
    public partial class HighlightPlayerPage : ContentPage
    {
        private readonly Card card;
        private readonly HighlightReel highlightReel;

        private int currentIndex;

        /*
        * FUNCTION     : HighlightPlayerPage
        * DESCRIPTION  :
        *     Constructs the highlight page from a Card and HighlightReel.
        *     The reel may contain multiple clips; the user can move between
        *     them via nav arrows or the clip strip.
        * PARAMETERS   :
        *     cardParam  - card that owns the highlight reel (for header).
        *     reelParam  - highlight reel found for the player/card.
        *     startIndex - optional starting clip index within the reel.
        * RETURNS      :
        *     none
        */
        public HighlightPlayerPage(
            Card cardParam,
            HighlightReel reelParam,
            int startIndex = 0)
        {
            InitializeComponent();

            card = cardParam ?? throw new ArgumentNullException(nameof(cardParam));
            highlightReel = reelParam ?? new HighlightReel();

            if (highlightReel.Clips == null)
            {
                highlightReel.Clips = new System.Collections.Generic.List<HighlightClip>();
            }

            // Only keep clips that actually have a URL.
            highlightReel.Clips =
                highlightReel.Clips
                    .Where(c => c != null && !string.IsNullOrWhiteSpace(c.VideoUrl))
                    .ToList();

            if (highlightReel.Clips.Count == 0)
            {
                DisplayAlert(
                    "Highlights",
                    "No playable highlight clips are available for this card.",
                    "OK").ContinueWith(_ => Navigation.PopAsync());

                return;
            }

            // Clamp starting index into valid range.
            if (startIndex < 0)
            {
                startIndex = 0;
            }

            if (startIndex >= highlightReel.Clips.Count)
            {
                startIndex = highlightReel.Clips.Count - 1;
            }

            currentIndex = startIndex;

            InitializeCardHeader();
            BuildClipStrip();
            UpdateCurrentClipUi();
        }

        /*
        * FUNCTION     : InitializeCardHeader
        * DESCRIPTION  :
        *     Initializes the card header/banner: card name, subtitle,
        *     grade, and estimated value.
        * PARAMETERS   :
        *     none
        * RETURNS      :
        *     none
        */
        private void InitializeCardHeader()
        {
            // Card name (player or character).
            CardNameLabel.Text = card.Name ?? "Unknown Card";

            // Subtitle similar to CardPage:
            // e.g. "2020 - BUF - Prizm Silver - #36"
            string yearPart = string.IsNullOrWhiteSpace(card.Year.ToString()) ? string.Empty : card.Year.ToString().Trim();
            string teamPart = string.IsNullOrWhiteSpace(card.Team) ? string.Empty : card.Team.Trim();
            string setPart = string.IsNullOrWhiteSpace(card.Set) ? string.Empty : card.Set.Trim();
            string numPart = string.IsNullOrWhiteSpace(card.Number) ? string.Empty : card.Number.Trim();

            string[] subtitlePieces = new[]
            {
                yearPart,
                teamPart,
                setPart,
                string.IsNullOrWhiteSpace(numPart) ? string.Empty : "#" + numPart
            };

            string subtitle =
                string.Join(" - ", subtitlePieces.Where(p => !string.IsNullOrWhiteSpace(p)));

            CardSubtitleLabel.Text = string.IsNullOrWhiteSpace(subtitle)
                ? "Card details not fully specified."
                : subtitle;

            // Grade information.
            if (!string.IsNullOrWhiteSpace(card.GradeCompany) && card.Grade.HasValue)
            {
                GradeLabel.Text =
                    $"{card.GradeCompany.Trim()} {card.Grade.Value.ToString("0.0", CultureInfo.InvariantCulture)}";
            }
            else if (card.Grade.HasValue)
            {
                GradeLabel.Text =
                    card.Grade.Value.ToString("0.0", CultureInfo.InvariantCulture);
            }
            else
            {
                GradeLabel.Text = "Ungraded";
            }

            // Estimated value.
            if (card.EstimatedValue.HasValue)
            {
                EstimatedValueLabel.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "${0:0.00} USD",
                    card.EstimatedValue.Value);
            }
            else
            {
                EstimatedValueLabel.Text = "N/A";
            }
        }

        /*
        * FUNCTION     : BuildClipStrip
        * DESCRIPTION  :
        *     Dynamically builds the horizontal strip of clip chips at the
        *     bottom. Each chip represents a clip and can be tapped to
        *     jump directly to that clip.
        * PARAMETERS   :
        *     none
        * RETURNS      :
        *     none
        */
        private void BuildClipStrip()
        {
            ClipStripLayout.Children.Clear();

            if (highlightReel.Clips.Count <= 1)
            {
                ClipStripLayout.IsVisible = false;
                return;
            }

            ClipStripLayout.IsVisible = true;

            for (int i = 0; i < highlightReel.Clips.Count; i++)
            {
                int index = i;

                Button chipButton = new Button
                {
                    Text = (i + 1).ToString(CultureInfo.InvariantCulture),
                    FontSize = 12,
                    Padding = new Thickness(10, 4),
                    CornerRadius = 14,
                    HorizontalOptions = LayoutOptions.Start
                };

                // Try to apply AuroraNeonButton style if available.
                if (Application.Current?.Resources != null &&
                    Application.Current.Resources.TryGetValue("AuroraNeonButton", out object styleObj) &&
                    styleObj is Style neonStyle)
                {
                    chipButton.Style = neonStyle;
                }

                chipButton.Clicked += (sender, args) =>
                {
                    currentIndex = index;
                    UpdateCurrentClipUi();
                };

                ClipStripLayout.Children.Add(chipButton);
            }

            HighlightActiveChip();
        }

        /*
        * FUNCTION     : HighlightActiveChip
        * DESCRIPTION  :
        *     Visually emphasizes the chip that corresponds to the current
        *     clip index.
        * PARAMETERS   :
        *     none
        * RETURNS      :
        *     none
        */
        private void HighlightActiveChip()
        {
            for (int i = 0; i < ClipStripLayout.Children.Count; i++)
            {
                if (ClipStripLayout.Children[i] is Button chip)
                {
                    if (i == currentIndex)
                    {
                        chip.Opacity = 1.0;
                        chip.FontAttributes = FontAttributes.Bold;
                    }
                    else
                    {
                        chip.Opacity = 0.6;
                        chip.FontAttributes = FontAttributes.None;
                    }
                }
            }
        }

        /*
        * FUNCTION     : UpdateCurrentClipUi
        * DESCRIPTION  :
        *     Updates the pseudo-player surface and side panel to reflect
        *     the clip at the current index.
        * PARAMETERS   :
        *     none
        * RETURNS      :
        *     none
        */
        private void UpdateCurrentClipUi()
        {
            if (highlightReel.Clips.Count == 0)
            {
                return;
            }

            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            if (currentIndex >= highlightReel.Clips.Count)
            {
                currentIndex = highlightReel.Clips.Count - 1;
            }

            HighlightClip clip = highlightReel.Clips[currentIndex];
            string videoUrl = clip.VideoUrl ?? string.Empty;

            ClipPositionLabel.Text =
                $"Clip {currentIndex + 1} of {highlightReel.Clips.Count}";

            if (!string.IsNullOrWhiteSpace(clip.Description))
            {
                ClipDescriptionLabel.Text = clip.Description;
            }
            else
            {
                ClipDescriptionLabel.Text = "Highlight clip";
            }

            // Try to load a YouTube thumbnail.
            string videoId = ExtractYouTubeVideoId(videoUrl);
            if (!string.IsNullOrWhiteSpace(videoId))
            {
                string thumbUrl = $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg";

                try
                {
                    ThumbnailImage.Source = ImageSource.FromUri(new Uri(thumbUrl));
                }
                catch
                {
                    ThumbnailImage.Source = null;
                }
            }
            else
            {
                ThumbnailImage.Source = null;
            }

            HighlightActiveChip();
        }

        /*
        * FUNCTION     : OnBackClicked
        * DESCRIPTION  :
        *     Pops this page from the navigation stack and returns to the
        *     card detail view.
        * PARAMETERS   :
        *     sender - the back button.
        *     e      - event args.
        * RETURNS      :
        *     none
        */
        private async void OnBackClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        /*
        * FUNCTION     : OnPlaySurfaceTapped
        * DESCRIPTION  :
        *     Allows tapping anywhere on the pseudo-player surface to
        *     trigger playback (same as Play button).
        * PARAMETERS   :
        *     sender - the tap recognizer.
        *     e      - event args.
        * RETURNS      :
        *     none
        */
        private void OnPlaySurfaceTapped(object sender, TappedEventArgs e)
        {
            OnPlayClicked(this, EventArgs.Empty);
        }

        /*
        * FUNCTION     : OnPrevClipClicked
        * DESCRIPTION  :
        *     Moves to the previous highlight clip (if any).
        * PARAMETERS   :
        *     sender - the button.
        *     e      - event args.
        * RETURNS      :
        *     none
        */
        private void OnPrevClipClicked(object sender, EventArgs e)
        {
            if (highlightReel.Clips.Count <= 1)
            {
                return;
            }

            if (currentIndex > 0)
            {
                currentIndex--;
                UpdateCurrentClipUi();
            }
        }

        /*
        * FUNCTION     : OnNextClipClicked
        * DESCRIPTION  :
        *     Moves to the next highlight clip (if any).
        * PARAMETERS   :
        *     sender - the button.
        *     e      - event args.
        * RETURNS      :
        *     none
        */
        private void OnNextClipClicked(object sender, EventArgs e)
        {
            if (highlightReel.Clips.Count <= 1)
            {
                return;
            }

            if (currentIndex < highlightReel.Clips.Count - 1)
            {
                currentIndex++;
                UpdateCurrentClipUi();
            }
        }

        /*
        * FUNCTION     : OnPlayClicked
        * DESCRIPTION  :
        *     Opens the current highlight clip using Launcher so the
        *     native YouTube app or browser handles playback.
        * PARAMETERS   :
        *     sender - the Play button.
        *     e      - event args.
        * RETURNS      :
        *     none
        */
        private async void OnPlayClicked(object sender, EventArgs e)
        {
            if (highlightReel.Clips.Count == 0)
            {
                return;
            }

            string videoUrl = highlightReel.Clips[currentIndex].VideoUrl;

            if (string.IsNullOrWhiteSpace(videoUrl))
            {
                await DisplayAlert(
                    "Playback Error",
                    "No video URL is available for this highlight.",
                    "OK");
                return;
            }

            try
            {
                await Launcher.OpenAsync(videoUrl);
            }
            catch (Exception ex)
            {
                await DisplayAlert(
                    "Playback Error",
                    $"Unable to open the highlight video: {ex.Message}",
                    "OK");
            }
        }

        /*
        * FUNCTION     : OnCopyLinkClicked
        * DESCRIPTION  :
        *     Copies the current clip's URL to the clipboard.
        * PARAMETERS   :
        *     sender - the Copy button.
        *     e      - event args.
        * RETURNS      :
        *     none
        */
        private async void OnCopyLinkClicked(object sender, EventArgs e)
        {
            if (highlightReel.Clips.Count == 0)
            {
                return;
            }

            string videoUrl = highlightReel.Clips[currentIndex].VideoUrl;

            if (string.IsNullOrWhiteSpace(videoUrl))
            {
                await DisplayAlert(
                    "Copy Link",
                    "There is no valid URL for this highlight.",
                    "OK");
                return;
            }

            try
            {
                await Clipboard.SetTextAsync(videoUrl);
                await DisplayAlert(
                    "Copy Link",
                    "Highlight link copied to clipboard.",
                    "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert(
                    "Copy Error",
                    $"Unable to copy link: {ex.Message}",
                    "OK");
            }
        }

        /*
        * FUNCTION     : OnShareClicked
        * DESCRIPTION  :
        *     Invokes the platform share sheet for the current highlight
        *     clip URL.
        * PARAMETERS   :
        *     sender - the Share button.
        *     e      - event args.
        * RETURNS      :
        *     none
        */
        private async void OnShareClicked(object sender, EventArgs e)
        {
            if (highlightReel.Clips.Count == 0)
            {
                return;
            }

            string videoUrl = highlightReel.Clips[currentIndex].VideoUrl;

            if (string.IsNullOrWhiteSpace(videoUrl))
            {
                await DisplayAlert(
                    "Share Highlight",
                    "There is no valid URL for this highlight.",
                    "OK");
                return;
            }

            try
            {
                await Share.RequestAsync(new ShareTextRequest
                {
                    Text = videoUrl,
                    Title = "Share CollectIQ Highlight"
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert(
                    "Share Error",
                    $"Unable to share this highlight: {ex.Message}",
                    "OK");
            }
        }

        /*
        * FUNCTION     : ExtractYouTubeVideoId
        * DESCRIPTION  :
        *     Attempts to extract a YouTube video ID from typical URL
        *     formats:
        *         - watch?v=VIDEO_ID
        *         - youtu.be/VIDEO_ID
        *         - embed/VIDEO_ID
        * PARAMETERS   :
        *     url - raw YouTube URL.
        * RETURNS      :
        *     string - extracted video ID or String.Empty if not found.
        */
        private static string ExtractYouTubeVideoId(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            // watch?v=VIDEO_ID
            Match match = Regex.Match(
                url,
                @"[?&]v=([^&]+)",
                RegexOptions.IgnoreCase);

            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }

            // youtu.be/VIDEO_ID
            match = Regex.Match(
                url,
                @"youtu\.be/([^?&]+)",
                RegexOptions.IgnoreCase);

            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }

            // embed/VIDEO_ID
            match = Regex.Match(
                url,
                @"embed/([^?&]+)",
                RegexOptions.IgnoreCase);

            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }

            return string.Empty;
        }
    }
}
