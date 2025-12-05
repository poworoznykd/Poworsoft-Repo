/*
* FILE            : HighlightReel.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2025-12-09
* DESCRIPTION     :
*     Represents a collection of video highlights for a player.
*/

namespace CollectIQ.Models.Domain.Entities
{
    /// <summary>
    /// Represents a list of video clips associated with a player's highlight reel.
    /// </summary>
    public sealed class HighlightReel
    {
        public List<HighlightClip> Clips { get; set; } = new List<HighlightClip>();
    }
}
