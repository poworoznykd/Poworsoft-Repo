using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CollectIQ.Models
{
    public class CollectionFilter
    {
		private decimal cardValue;

		public decimal CardValue
		{
			get { return cardValue; }
			set { cardValue = value; }
		}

		public SportLeague League { get; set; }

        public string FilterString { get; set; }


    }

	public enum SportLeague
	{
		NBA,
		NFL,
		MLB,
		NHL,
		MMA,
        Soccer,
        WNBA,
        CFL,
        Other
    }
}
