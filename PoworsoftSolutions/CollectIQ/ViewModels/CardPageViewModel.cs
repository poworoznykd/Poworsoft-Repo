using CollectIQ.Interfaces;
using CollectIQ.Models;
using CollectIQ.Models.SportsCardsPro;
using CollectIQ.Services;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CollectIQ.Enums.Enums;

namespace CollectIQ.ViewModels
{
    public class CardPageViewModel : BaseViewModel
    {
        private string headerTitle;
        public string HeaderTitle
        {
            get => headerTitle;
            private set
            {
                if (headerTitle == value) return;
                headerTitle = value;
                OnPropertyChanged();
            }
        }

        private string cardSubTitle;
        public string CardSubTitle
        {
            get { return cardSubTitle; }
            private set
            {
                if (cardSubTitle == value) return;
                cardSubTitle = value;
                OnPropertyChanged();
            }
        }

        private Card selectedCard;
        public Card SelectedCard
        {
            get { return selectedCard; }
            private set
            {
                if (selectedCard == value) return;
                selectedCard = value;
                OnPropertyChanged();
            }
        }

        public CardPageViewModel(Card card)
        {
            this.selectedCard = card ?? new Card();
            RefreshHeader(useStoredTitle: true);
        }

        /// <summary>
        /// Refreshes the large header so edits made below are immediately visible.
        /// The stored marketplace/source title remains available on the card, but
        /// after the user edits card metadata the header becomes a live summary of
        /// the current card fields instead of being frozen to the original match.
        /// </summary>
        public void RefreshHeader(bool useStoredTitle = false)
        {
            Card card = SelectedCard ?? new Card();
            string player = card.Player?.FullName?.Trim() ?? string.Empty;
            string year = card.Year.HasValue && card.Year.Value > 0 ? card.Year.Value.ToString() : string.Empty;
            string set = card.Set?.Trim() ?? string.Empty;
            string number = card.Number?.Trim() ?? string.Empty;
            string team = card.Team?.Name?.Trim() ?? string.Empty;

            if (useStoredTitle && !string.IsNullOrWhiteSpace(card.Title))
            {
                HeaderTitle = card.Title.Trim();
            }
            else
            {
                string[] titleParts = new[] { year, set, player }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
                string rebuilt = string.Join(" ", titleParts);
                if (!string.IsNullOrWhiteSpace(number))
                    rebuilt = $"{rebuilt} #{number}".Trim();
                HeaderTitle = string.IsNullOrWhiteSpace(rebuilt)
                    ? (!string.IsNullOrWhiteSpace(card.Title) ? card.Title : "Card Detail")
                    : rebuilt;
            }

            List<string> subtitleParts = new();
            if (!string.IsNullOrWhiteSpace(team)) subtitleParts.Add(team);
            if (!string.IsNullOrWhiteSpace(set)) subtitleParts.Add(set);
            if (!string.IsNullOrWhiteSpace(number)) subtitleParts.Add($"#{number}");
            CardSubTitle = string.Join(" • ", subtitleParts);
        }

        // ============================================================
        //  SPORT PICKER SUPPORT
        // ============================================================

        public List<CollectingCardCategory> SportOptions { get; } =
            Enum.GetValues(typeof(CollectingCardCategory))
                .Cast<CollectingCardCategory>()
                .ToList();

        public IReadOnlyList<SportsCardsProGradeOption> GradeOptions => SportsCardsProGradeCatalog.All;

        public SportsCardsProGradeOption SelectedGradeOption
        {
            get => SportsCardsProGradeCatalog.FromCard(SelectedCard);
            set
            {
                SportsCardsProGradeOption selected = value ?? SportsCardsProGradeCatalog.Ungraded;
                selected.ApplyToCard(SelectedCard);
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedGradeDisplay));
            }
        }

        public string SelectedGradeDisplay
        {
            get
            {
                Grading grading = SelectedCard?.Grading ?? new Grading();
                if (!grading.Grade.HasValue)
                    return "Ungraded";

                string company = grading.Company?.Trim() ?? string.Empty;
                return string.IsNullOrWhiteSpace(company)
                    ? grading.Grade.Value.ToString("0.##")
                    : $"{company} {grading.Grade.Value:0.##}";
            }
        }

        public CollectingCardCategory SelectedSport
        {
            get
            {
                if (SelectedCard == null)
                {
                    return CollectingCardCategory.Other;
                }

                return SelectedCard.Sport;
            }
            set
            {
                if (SelectedCard == null)
                {
                    return;
                }

                SelectedCard.Sport = value;
                OnPropertyChanged();
            }
        }

        /*
       * FUNCTION     : BuildHighlightSearchQuery
       * DESCRIPTION  :
       *     Builds a YouTube search query for this card's highlight reel.
       *     - For sports cards, we bias toward game highlights.
       *     - For Pokémon, we bias toward TCG / character highlights.
       * PARAMETERS   :
       *     none
       * RETURNS      :
       *     string  - query string suitable for YouTube search.
       */
        public string BuildHighlightSearchQuery()
        {
            if (SelectedCard == null)
            {
                return string.Empty;
            }

            // Prefer Player.FullName, fall back to card.Name
            string playerName = string.Empty;
            if (SelectedCard.Player != null &&
                !string.IsNullOrWhiteSpace(SelectedCard.Player.FullName))
            {
                playerName = SelectedCard.Player.FullName.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(SelectedCard.Player.FullName))
            {
                playerName = SelectedCard.Player.FullName.Trim();
            }

            string sport = SelectedCard.Sport.ToString().Trim();
            string team = SelectedCard.Team.Name?.Trim() ?? string.Empty;
            string year = SelectedCard.Year.ToString()?.Trim() ?? string.Empty;

            bool isPokemon =
                sport.Contains("pokemon", StringComparison.OrdinalIgnoreCase) ||
                playerName.Contains("pokemon", StringComparison.OrdinalIgnoreCase);

            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(playerName))
            {
                parts.Add(playerName);
            }

            if (!string.IsNullOrWhiteSpace(year))
            {
                parts.Add(year);
            }

            if (!string.IsNullOrWhiteSpace(team) &&
                !isPokemon) // teams usually don’t matter for Pokémon
            {
                parts.Add(team);
            }

            if (!string.IsNullOrWhiteSpace(sport))
            {
                parts.Add(sport);
            }

            string baseQuery = string.Join(" ", parts);

            if (isPokemon)
            {
                // Pokémon bias – character + TCG + highlight gameplay
                if (!baseQuery.Contains("pokemon", StringComparison.OrdinalIgnoreCase))
                {
                    baseQuery += " Pokémon";
                }

                baseQuery += " TCG highlights";
            }
            else
            {
                // Sports bias – actual highlight reel, not someone’s vlog
                if (!baseQuery.Contains("highlight", StringComparison.OrdinalIgnoreCase))
                {
                    baseQuery += " highlight reel";
                }

                baseQuery += " full game best plays";
            }

            return baseQuery.Trim();
        }


    }
}
