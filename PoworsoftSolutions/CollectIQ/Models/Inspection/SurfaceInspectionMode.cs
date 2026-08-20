namespace CollectIQ.Models.Inspection
{
    /// <summary>
    /// Surface inspection capture strategies. ExternalLight is the primary
    /// CollectIQ photometric-stereo workflow. SinglePhoto is a conventional
    /// front-image pre-screen. TiltSweep keeps phone/light fixed while the card
    /// is tilted through many views to expose moving reflections and relief.
    /// </summary>
    public enum SurfaceInspectionMode
    {
        ExternalLight = 0,
        SinglePhoto = 1,
        TiltSweep = 2
    }
}
