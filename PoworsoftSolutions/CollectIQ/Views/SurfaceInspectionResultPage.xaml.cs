using CollectIQ.Models.Inspection;
using System.Linq;

namespace CollectIQ.Views
{
    public partial class SurfaceInspectionResultPage : ContentPage
    {
        public SurfaceInspectionResultPage(SurfaceInspectionResult result)
        {
            InitializeComponent();

            ModeNameLabel.Text = result.ModeName;
            SummaryLabel.Text = result.Summary;
            AnomalyScoreLabel.Text = $"{result.AnomalyScore:0}/100";
            ConsistencyLabel.Text = $"{result.CaptureConsistencyScore:0}%";

            ConfigureForMode(result.Mode);

            SetImageIfPresent(HeatmapImage, result.HeatmapImagePath);
            SetImageIfPresent(DefectOverlayImage, result.DefectOverlayImagePath);
            SetImageIfPresent(ReliefImage, result.ReliefImagePath);
            SetImageIfPresent(DiffuseImage,
                string.IsNullOrWhiteSpace(result.AlbedoImagePath)
                    ? result.DiffuseImagePath
                    : result.AlbedoImagePath);
            SetImageIfPresent(NormalMapImage, result.NormalMapImagePath);
            SetImageIfPresent(CurvatureImage, result.CurvatureImagePath);
            SetImageIfPresent(ReferenceResidualImage, result.ReferenceResidualImagePath);

            if (result.Mode == SurfaceInspectionMode.ExternalLight)
            {
                if (File.Exists(result.Diagnostics.EdgeOverlayPath))
                    EdgeOverlayImage.Source = ImageSource.FromFile(result.Diagnostics.EdgeOverlayPath);

                if (File.Exists(result.Diagnostics.AlignmentAveragePath))
                    AlignmentAverageImage.Source = ImageSource.FromFile(result.Diagnostics.AlignmentAveragePath);

                BindFrame(result.Diagnostics.Frames, SurfaceLightDirection.Top,
                    TopStatsLabel, TopDetectionImage, TopRegisteredImage, TopEdgeImage);
                BindFrame(result.Diagnostics.Frames, SurfaceLightDirection.Right,
                    RightStatsLabel, RightDetectionImage, RightRegisteredImage, RightEdgeImage);
                BindFrame(result.Diagnostics.Frames, SurfaceLightDirection.Bottom,
                    BottomStatsLabel, BottomDetectionImage, BottomRegisteredImage, BottomEdgeImage);
                BindFrame(result.Diagnostics.Frames, SurfaceLightDirection.Left,
                    LeftStatsLabel, LeftDetectionImage, LeftRegisteredImage, LeftEdgeImage);
            }
        }

        private void ConfigureForMode(SurfaceInspectionMode mode)
        {
            bool external = mode == SurfaceInspectionMode.ExternalLight;
            bool single = mode == SurfaceInspectionMode.SinglePhoto;
            bool tilt = mode == SurfaceInspectionMode.TiltSweep;

            AlignmentDiagnosticsSection.IsVisible = external;
            NormalMapSection.IsVisible = external;
            CurvatureSection.IsVisible = external;
            ReferenceResidualSection.IsVisible = external;

            if (single)
            {
                ConsistencyTitleLabel.Text = "Card detection quality";
                ReliefTitleLabel.Text = "Multi-scale surface detail";
                DiffuseTitleLabel.Text = "Normalized card image";
                DiffuseDescriptionLabel.Text = "Perspective-normalized source used by the single-photo pre-screen.";
            }
            else if (tilt)
            {
                ConsistencyTitleLabel.Text = "Usable-view quality";
                ReliefTitleLabel.Text = "View-dependent surface response";
                DiffuseTitleLabel.Text = "Robust tilt-sweep appearance";
                DiffuseDescriptionLabel.Text = "Median appearance across the usable normalized tilt views.";
            }
            else
            {
                ConsistencyTitleLabel.Text = "Registration quality";
                ReliefTitleLabel.Text = "Directional relief";
                DiffuseTitleLabel.Text = "Robust estimated albedo";
                DiffuseDescriptionLabel.Text = "Middle-two directional estimate suppresses one-frame glare and shadow.";
            }

            // All three modes intentionally show these core outputs.
            ReliefSection.IsVisible = true;
            DiffuseSection.IsVisible = true;
        }

        private static void SetImageIfPresent(Image image, string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                image.Source = ImageSource.FromFile(path);
        }

        private static void BindFrame(
            IReadOnlyCollection<InspectionDebugFrame> frames,
            SurfaceLightDirection direction,
            Label statsLabel,
            Image detectionImage,
            Image registeredImage,
            Image edgeImage)
        {
            InspectionDebugFrame? frame = frames.FirstOrDefault(item => item.Direction == direction);

            if (frame == null)
            {
                statsLabel.Text = "No diagnostics available";
                return;
            }

            statsLabel.Text =
                $"Detection {frame.DetectionConfidence:0}% • Align {frame.AlignmentConfidence:0}%\n" +
                $"Rotate {frame.RotationDegrees:+0.0;-0.0;0.0}° • Scale {frame.Scale:0.000} • Shift {frame.OffsetX},{frame.OffsetY}px";

            if (File.Exists(frame.DetectionOverlayPath))
                detectionImage.Source = ImageSource.FromFile(frame.DetectionOverlayPath);

            if (File.Exists(frame.RegisteredImagePath))
                registeredImage.Source = ImageSource.FromFile(frame.RegisteredImagePath);

            if (File.Exists(frame.EdgeImagePath))
                edgeImage.Source = ImageSource.FromFile(frame.EdgeImagePath);
        }

        private async void OnDoneClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}
