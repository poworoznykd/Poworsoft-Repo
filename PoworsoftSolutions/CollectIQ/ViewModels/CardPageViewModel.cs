using CollectIQ.Domain.Entities;
using CollectIQ.Interfaces;
using CollectIQ.Models;
using CollectIQ.Models.Domain.Entities;
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
 *     Builds a YouTube search query for the current card, using:
 *         - Player or card name
 *         - Sport (e.g., football, hockey, basketball, Pokémon)
 *         - Team and year (when available)
 *     The goal is to bias results toward professional highlight reels,
 *     not random unrelated content.
 * PARAMETERS   :
 *     none
 * RETURNS      :
 *     string - the search query to send to HighlightService.
 */
        public string BuildHighlightSearchQuery()
        {
            Card card = selectedCard;
            if (card == null)
            {
                return string.Empty;
            }

            // Prefer player full name if available; otherwise fall back to card.Name.
            string playerName = string.Empty;

            if (card.Player != null &&
                !string.IsNullOrWhiteSpace(card.Player.FullName))
            {
                playerName = card.Player.FullName.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(card.Name))
            {
                playerName = card.Name.Trim();
            }

            string sport = card.Sport?.Trim() ?? string.Empty;
            string team = card.Team?.Trim() ?? string.Empty;
            string year = card.Year.ToString()?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(playerName))
            {
                // If we do not even know the player/character name, there is no
                // sensible highlight search to perform.
                return string.Empty;
            }

            StringBuilder queryBuilder = new StringBuilder();
            queryBuilder.Append(playerName);

            // Sport-specific tuning.
            if (!string.IsNullOrWhiteSpace(sport))
            {
                string lowerSport = sport.ToLowerInvariant();

                if (lowerSport.Contains("football"))
                {
                    // e.g. "Riley Leonard football highlights"
                    queryBuilder.Append(" football highlights");
                }
                else if (lowerSport.Contains("hockey"))
                {
                    queryBuilder.Append(" hockey highlights");
                }
                else if (lowerSport.Contains("basketball"))
                {
                    queryBuilder.Append(" basketball highlights");
                }
                else if (lowerSport.Contains("baseball"))
                {
                    queryBuilder.Append(" baseball highlights");
                }
                else if (lowerSport.Contains("soccer"))
                {
                    queryBuilder.Append(" soccer highlights");
                }
                else if (lowerSport.Contains("pokemon"))
                {
                    // Pokémon: we care more about TCG-oriented videos.
                    queryBuilder.Append(" pokemon tcg card highlights");
                }
                else
                {
                    queryBuilder.Append(" highlights");
                }
            }
            else
            {
                // No sport set: still bias toward highlight reels but keep it generic.
                queryBuilder.Append(" highlights");
            }

            if (!string.IsNullOrWhiteSpace(team))
            {
                queryBuilder.Append(' ');
                queryBuilder.Append(team);
            }

            if (!string.IsNullOrWhiteSpace(year))
            {
                queryBuilder.Append(' ');
                queryBuilder.Append(year);
            }

            return queryBuilder.ToString().Trim();
        }



    }
}
