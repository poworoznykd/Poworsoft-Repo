//
//  FILE            : ProfileViewModel.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2025-11-23
//  UPDATED         : 2026-01-02
//  DESCRIPTION     :
//      ViewModel for the CollectIQ user profile.
//      Stores public-facing identity and reputation stats that can be
//      displayed in the dashboard summary card and the full Profile page.
//      Includes INotifyPropertyChanged so UI updates immediately when the
//      avatar photo or stats change.
//

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CollectIQ.ViewModels
{
    /// <summary>
    /// ViewModel for displaying and editing the user's CollectIQ profile.
    /// </summary>
    public class ProfileViewModel : INotifyPropertyChanged
    {
        private string avatarPath;
        private string displayName;
        private string handle;
        private string location;
        private string memberSince;
        private bool isVerified;

        private double rating;
        private int ratingCount;

        private int tradesCompleted;
        private int salesCompleted;
        private int purchasesCompleted;

        private int collectionCount;
        private decimal collectionValue;

        private string avgResponseTime;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProfileViewModel"/> class.
        /// </summary>
        public ProfileViewModel()
        {
            avatarPath = string.Empty;
            displayName = "Darryl Poworoznyk";
            handle = "@collectiq_member";
            location = "Ontario, Canada";
            memberSince = "Member since 2025";
            isVerified = false;

            rating = 4.9;
            ratingCount = 127;

            tradesCompleted = 32;
            salesCompleted = 18;
            purchasesCompleted = 25;

            collectionCount = 0;
            collectionValue = 0m;

            avgResponseTime = "2h";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Absolute path to the avatar image stored locally on device.
        /// </summary>
        public string AvatarPath
        {
            get => avatarPath;
            set
            {
                if (avatarPath != value)
                {
                    avatarPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayName
        {
            get => displayName;
            set
            {
                if (displayName != value)
                {
                    displayName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Handle
        {
            get => handle;
            set
            {
                if (handle != value)
                {
                    handle = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Location
        {
            get => location;
            set
            {
                if (location != value)
                {
                    location = value;
                    OnPropertyChanged();
                }
            }
        }

        public string MemberSince
        {
            get => memberSince;
            set
            {
                if (memberSince != value)
                {
                    memberSince = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsVerified
        {
            get => isVerified;
            set
            {
                if (isVerified != value)
                {
                    isVerified = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Rating
        {
            get => rating;
            set
            {
                if (Math.Abs(rating - value) > 0.0001)
                {
                    rating = value;
                    OnPropertyChanged();
                }
            }
        }

        public int RatingCount
        {
            get => ratingCount;
            set
            {
                if (ratingCount != value)
                {
                    ratingCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public int TradesCompleted
        {
            get => tradesCompleted;
            set
            {
                if (tradesCompleted != value)
                {
                    tradesCompleted = value;
                    OnPropertyChanged();
                }
            }
        }

        public int SalesCompleted
        {
            get => salesCompleted;
            set
            {
                if (salesCompleted != value)
                {
                    salesCompleted = value;
                    OnPropertyChanged();
                }
            }
        }

        public int PurchasesCompleted
        {
            get => purchasesCompleted;
            set
            {
                if (purchasesCompleted != value)
                {
                    purchasesCompleted = value;
                    OnPropertyChanged();
                }
            }
        }

        public int CollectionCount
        {
            get => collectionCount;
            set
            {
                if (collectionCount != value)
                {
                    collectionCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public decimal CollectionValue
        {
            get => collectionValue;
            set
            {
                if (collectionValue != value)
                {
                    collectionValue = value;
                    OnPropertyChanged();
                }
            }
        }

        public string AvgResponseTime
        {
            get => avgResponseTime;
            set
            {
                if (avgResponseTime != value)
                {
                    avgResponseTime = value;
                    OnPropertyChanged();
                }
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
