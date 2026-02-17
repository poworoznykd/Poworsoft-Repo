using CollectIQ.Interfaces;
using CollectIQ.Models;
using CollectIQ.Services;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CollectIQ.ViewModels
{
    public class CardPageViewModel : BaseViewModel
    {
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
            this.selectedCard = card;
            cardSubTitle = $"{card.Year} - {card.Team} - {card.Set} - #{card.Number}";
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

            string sport = SelectedCard.Sport.ToString()?.Trim() ?? string.Empty;
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
