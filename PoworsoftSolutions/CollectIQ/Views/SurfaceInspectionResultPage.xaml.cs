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

            ConfigureForResult(result);

            if (result.Mode == SurfaceInspectionMode.TiltSweep)
                BindTiltViews(result.TiltViews);

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

                BindFrame(result.Diagnostics.Frames, SurfaceLightDirection.Top, TopStatsLabel, TopDetectionImage, TopRegisteredImage, TopEdgeImage);
                BindFrame(result.Diagnostics.Frames, SurfaceLightDirection.Right, RightStatsLabel, RightDetectionImage, RightRegisteredImage, RightEdgeImage);
                BindFrame(result.Diagnostics.Frames, SurfaceLightDirection.Bottom, BottomStatsLabel, BottomDetectionImage, BottomRegisteredImage, BottomEdgeImage);
                BindFrame(result.Diagnostics.Frames, SurfaceLightDirection.Left, LeftStatsLabel, LeftDetectionImage, LeftRegisteredImage, LeftEdgeImage);
            }
        }

        private void ConfigureForResult(SurfaceInspectionResult result)
        {
            bool external = result.Mode == SurfaceInspectionMode.ExternalLight;
            bool single = result.Mode == SurfaceInspectionMode.SinglePhoto;
            bool tilt = result.Mode == SurfaceInspectionMode.TiltSweep;
            bool tiltHasCombinedOutputs = tilt &&
                                          (!string.IsNullOrWhiteSpace(result.HeatmapImagePath) ||
                                           !string.IsNullOrWhiteSpace(result.DefectOverlayImagePath) ||
                                           !string.IsNullOrWhiteSpace(result.DiffuseImagePath));

            AlignmentDiagnosticsSection.IsVisible = external;
            NormalMapSection.IsVisible = external;
            CurvatureSection.IsVisible = external;
            ReferenceResidualSection.IsVisible = external;
            TiltAlignmentSection.IsVisible = tilt;

            DefectTitleLabel.IsVisible = !tilt || tiltHasCombinedOutputs;
            DefectDescriptionLabel.IsVisible = !tilt || tiltHasCombinedOutputs;
            DefectOverlayBorder.IsVisible = !tilt || tiltHasCombinedOutputs;
            HeatmapTitleLabel.IsVisible = !tilt || tiltHasCombinedOutputs;
            HeatmapDescriptionLabel.IsVisible = !tilt || tiltHasCombinedOutputs;
            HeatmapBorder.IsVisible = !tilt || tiltHasCombinedOutputs;

            if (single)
            {
                ConsistencyTitleLabel.Text = "Card detection quality";
                ReliefTitleLabel.Text = "Multi-scale surface detail";
                DiffuseTitleLabel.Text = "Normalized card image";
                DiffuseDescriptionLabel.Text = "Perspective-normalized source used by the single-photo pre-screen.";
                ReliefSection.IsVisible = true;
                DiffuseSection.IsVisible = true;
            }
            else if (tilt)
            {
                ConsistencyTitleLabel.Text = "Alignment quality";
                DefectTitleLabel.Text = "Tilt-sweep defect candidates";
                DefectDescriptionLabel.Text = "Yellow boxes mark the strongest view-dependent candidate defects found across the aligned tilt sweep.";
                HeatmapTitleLabel.Text = "Tilt-sweep variability heatmap";
                HeatmapDescriptionLabel.Text = "Stable artwork should stay quiet after alignment. Regions that react unusually as the card tilts become hot.";
                DiffuseTitleLabel.Text = "Aligned average view";
                DiffuseDescriptionLabel.Text = "Average of the accepted tilt views after mapping them to the same flat card geometry.";
                ReliefSection.IsVisible = false;
                DiffuseSection.IsVisible = tiltHasCombinedOutputs;
            }
            else
            {
                ConsistencyTitleLabel.Text = "Registration quality";
                ReliefTitleLabel.Text = "Directional relief";
                DiffuseTitleLabel.Text = "Robust estimated albedo";
                DiffuseDescriptionLabel.Text = "Middle-two directional estimate suppresses one-frame glare and shadow.";
                ReliefSection.IsVisible = true;
                DiffuseSection.IsVisible = true;
            }
        }

        private void BindTiltViews(IReadOnlyList<TiltViewDiagnostic> views)
        {
            TiltViewsContainer.Children.Clear();
            foreach (TiltViewDiagnostic view in views.OrderBy(item => item.CaptureNumber))
            {
                var title = new Label
                {
                    Text = view.Accepted ? $"VIEW {view.CaptureNumber:00} — ALIGNED" : $"VIEW {view.CaptureNumber:00} — REJECTED",
                    TextColor = view.Accepted ? Color.FromArgb("#86FF70") : Color.FromArgb("#FF8A8A"),
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 12
                };

                var status = new Label
                {
                    Text = view.Accepted
                        ? $"Geometry {view.GeometryConfidence:0}% • Align {view.AlignmentConfidence:0}%" + (view.Rotated180 ? " • 180° ambiguity corrected" : string.Empty)
                        : view.Status,
                    TextColor = Color.FromArgb("#9FB3C8"),
                    FontSize = 10
                };

                var stack = new VerticalStackLayout { Spacing = 5 };
                stack.Children.Add(title);
                stack.Children.Add(status);

                if (view.Accepted && !string.IsNullOrWhiteSpace(view.AlignedImagePath) && File.Exists(view.AlignedImagePath))
                {
                    stack.Children.Add(new Image
                    {
                        Source = ImageSource.FromFile(view.AlignedImagePath),
                        Aspect = Aspect.AspectFit,
                        HeightRequest = 420
                    });
                }

                TiltViewsContainer.Children.Add(new Border
                {
                    Stroke = view.Accepted ? Color.FromArgb("#18A7C8") : Color.FromArgb("#7F1D1D"),
                    StrokeThickness = 1,
                    Padding = 8,
                    Content = stack
                });
            }
        }

        private static void SetImageIfPresent(Image image, string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                image.Source = ImageSource.FromFile(path);
        }

        private static void BindFrame(IReadOnlyCollection<InspectionDebugFrame> frames, SurfaceLightDirection direction, Label statsLabel, Image detectionImage, Image registeredImage, Image edgeImage)
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

            if (File.Exists(frame.DetectionOverlayPath)) detectionImage.Source = ImageSource.FromFile(frame.DetectionOverlayPath);
            if (File.Exists(frame.RegisteredImagePath)) registeredImage.Source = ImageSource.FromFile(frame.RegisteredImagePath);
            if (File.Exists(frame.EdgeImagePath)) edgeImage.Source = ImageSource.FromFile(frame.EdgeImagePath);
        }

        private async void OnDoneClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}
