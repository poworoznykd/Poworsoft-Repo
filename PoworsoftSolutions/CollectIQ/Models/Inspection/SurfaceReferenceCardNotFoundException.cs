namespace CollectIQ.Models.Inspection
{
    /// <summary>
    /// Raised when the neutral Surface reference cannot be locked automatically.
    /// PreviewImagePath points to the already-auto-oriented bounded preview that
    /// should be used by the manual four-corner recovery UI.
    /// </summary>
    public sealed class SurfaceReferenceCardNotFoundException : InvalidOperationException
    {
        public SurfaceReferenceCardNotFoundException(string message, string previewImagePath, Exception? innerException = null)
            : base(message, innerException)
        {
            PreviewImagePath = previewImagePath;
        }

        public string PreviewImagePath { get; }
    }
}
