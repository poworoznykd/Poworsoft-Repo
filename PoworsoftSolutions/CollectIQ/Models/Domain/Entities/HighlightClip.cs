/*
* FILE            : HighlightClip.cs
* PROJECT         : CollectIQ (Mobile Application)
* PROGRAMMER      : Darryl Poworoznyk
* FIRST VERSION   : 2025-12-09
* DESCRIPTION     :
*     Represents a single video highlight clip.
*/

namespace CollectIQ.Models.Domain.Entities
{
    /// <summary>
    /// Represents a single highlight clip for a player's highlight reel.
    /// </summary>
    public sealed class HighlightClip
    {
        public string VideoUrl { get; set; } = string.Empty;      // YouTube, TikTok, local
        public string Description { get; set; } = string.Empty;
        public TimeSpan? StartTimestamp { get; set; }
    }
}
