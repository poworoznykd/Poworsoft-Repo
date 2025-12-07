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


    }
}
