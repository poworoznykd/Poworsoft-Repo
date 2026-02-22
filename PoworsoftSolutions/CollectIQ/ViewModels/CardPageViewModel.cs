//
//  FILE            : CardPageViewModel.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  DESCRIPTION     :
//      ViewModel for CardPage.
//      - Exposes the selected Card.
//      - Builds a clean subtitle for the page header.
//      - Builds a YouTube search query for a highlight reel.
//

using CollectIQ.Models;
using System;
using System.Collections.Generic;

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
                if (cardSubTitle == value)
                {
                    return;
                }

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
                if (selectedCard == value)
                {
                    return;
                }

                selectedCard = value;
                OnPropertyChanged();
            }
        }

        public CardPageViewModel(Card card)
        {
            SelectedCard = card;
            CardSubTitle = BuildSubtitle(card);
        }

        // ----------------------------------------------------------
        //  PRIVATE HELPERS
        // ----------------------------------------------------------

        private static string BuildSubtitle(Card card)
        {
            if (card == null)
            {
                return string.Empty;
            }

            // Team is a composed object (TeamJson backing field), so always use Team.Name.
            string year = card.Year?.ToString() ?? string.Empty;
            string team = card.Team?.Name ?? string.Empty;
            string set = card.Set ?? string.Empty;
            string number = card.Number ?? string.Empty;

            string raw = $"{year}  {team}  {set}  #{number}";
            return raw.Replace("  ", " ").Trim();
        }

        // ----------------------------------------------------------
        //  PUBLIC METHODS
        // ----------------------------------------------------------

        /*
        * FUNCTION     : BuildHighlightSearchQuery
        * DESCRIPTION  :
        *     Builds a YouTube search query for this card's highlight reel.
        *     - Sports cards: bias toward game highlights.
        *     - Pokémon: bias toward TCG / character content.
        * RETURNS      :
        *     string - Query string suitable for YouTube search.
        */
        public string BuildHighlightSearchQuery()
        {
            if (SelectedCard == null)
            {
                return string.Empty;
            }

            string playerName = SelectedCard.Player?.FullName?.Trim() ?? string.Empty;

            // Fallback: the listing/title usually contains the player name.
            if (string.IsNullOrWhiteSpace(playerName))
            {
                playerName = (SelectedCard.Title ?? string.Empty).Trim();
            }

            string sport = SelectedCard.Sport.ToString()?.Trim() ?? string.Empty;
            string team = SelectedCard.Team?.Name?.Trim() ?? string.Empty;
            string year = SelectedCard.Year?.ToString()?.Trim() ?? string.Empty;

            bool isPokemon =
                sport.Contains("pokemon", StringComparison.OrdinalIgnoreCase) ||
                playerName.Contains("pokemon", StringComparison.OrdinalIgnoreCase);

            List<string> parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(playerName))
            {
                parts.Add(playerName);
            }

            if (!string.IsNullOrWhiteSpace(year))
            {
                parts.Add(year);
            }

            if (!string.IsNullOrWhiteSpace(team) && !isPokemon)
            {
                parts.Add(team);
            }

            if (!string.IsNullOrWhiteSpace(sport))
            {
                parts.Add(sport);
            }

            string baseQuery = string.Join(" ", parts).Trim();

            if (isPokemon)
            {
                if (!baseQuery.Contains("pokemon", StringComparison.OrdinalIgnoreCase))
                {
                    baseQuery += " Pokémon";
                }

                baseQuery += " TCG highlights";
            }
            else
            {
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
