/*
 * FILE         : ProfileViewModel.cs
 * PROJECT      : CollectIQ (Mobile Application)
 * PROGRAMMER   : Darryl Poworoznyk
 * FIRST VERSION: 2026-01-18
 * DESCRIPTION  :
 *   View model for the user profile / reputation page.
 *   Later this can pull from a real UserProfile table or
 *   remote API when CollectIQ goes multi-user.
 */

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CollectIQ.ViewModels
{
    public class ProfileViewModel : INotifyPropertyChanged
    {
                    // Basic identity
        public string DisplayName { get; set; } = "Darryl P.";
        public string Handle { get; set; } = "@PoworsoftCards";
        public string AvatarImagePath { get; set; } = "default_avatar.png";
        public bool IsVerified { get; set; } = true;
        public string Location { get; set; } = "Ontario, Canada";
        public DateTime MemberSince { get; set; } = new DateTime(2024, 10, 1);

        public string MemberSinceLabel =>
            $"Member since {MemberSince:MMM yyyy}";

        public string MembershipLabel => "Trusted Member";

        // Reputation
        public double Rating { get; set; } = 4.9;
        public int RatingCount { get; set; } = 27;
        public string StarString
        {
            get
            {
                int fullStars = (int)Math.Round(Rating);
                return new string('★', fullStars) +
                       new string('☆', Math.Max(0, 5 - fullStars));
            }
        }

        public string RatingCountLabel => $"{RatingCount} reviews";
        public int TradesCompleted { get; set; } = 54;
        public int DisputesCount { get; set; } = 0;

        // Collection / volume
        public int TotalCardsOwned { get; set; } = 312;
        public decimal CollectionEstimatedValue { get; set; } = 7845.50m;
        public decimal TotalVolumeSold { get; set; } = 3240.00m;

        // Activity
        public int CardsBought { get; set; } = 130;
        public int CardsSold { get; set; } = 84;

        // Preferences
        public string FavouriteSportsLabel { get; set; } =
            "Favourite sports: Football, Basketball, Pokémon";

        public string FavouriteGradersLabel { get; set; } =
            "Preferred grading: PSA, BGS, SGC, Raw";

        public string OpenToTradesLabel { get; set; } = "Open to trades";
        public string ContactPreferenceLabel { get; set; } =
            "Contact via in-app messaging";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
