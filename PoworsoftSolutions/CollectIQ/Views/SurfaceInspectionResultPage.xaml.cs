using CollectIQ.Models.Inspection;
using System.Linq;

namespace CollectIQ.Views
{
    public partial class SurfaceInspectionResultPage : ContentPage
    {
        private readonly SurfaceInspectionResult result;
        private string heatmapPath = string.Empty;
        private string reliefPath = string.Empty;
        private string specularPath = string.Empty;
        private string diffusePath = string.Empty;
        private string defectOverlayPath = string.Empty;

        private enum PrimaryInspectionView
        {
            Heatmap,
            Relief,
            Specular,
            Diffuse,
            DefectOverlay
        }

        public SurfaceInspectionResultPage(SurfaceInspectionResult result)
        {
            InitializeComponent();
            this.result = result;

            SummaryLabel.Text = result.Summary;
            AnomalyScoreLabel.Text = $"{result.AnomalyScore:0}/100";
            ConsistencyLabel.Text = $"{result.CaptureConsistencyScore:0}%";
            heatmapPath = result.HeatmapImagePath;
            reliefPath = result.ReliefImagePath;
            specularPath = result.SpecularDefectImagePath;
            diffusePath = result.DiffuseImagePath;
            defectOverlayPath = result.DefectOverlayImagePath;

            HeatmapImage.Source = ImageSource.FromFile(result.HeatmapImagePath);
            ReliefImage.Source = ImageSource.FromFile(result.ReliefImagePath);
            if (!string.IsNullOrWhiteSpace(result.SpecularDefectImagePath) && File.Exists(result.SpecularDefectImagePath))
            {
                SpecularDefectImage.Source = ImageSource.FromFile(result.SpecularDefectImagePath);
            }
            DiffuseImage.Source = ImageSource.FromFile(result.DiffuseImagePath);
            if (!string.IsNullOrWhiteSpace(result.DefectOverlayImagePath) && File.Exists(result.DefectOverlayImagePath))
            {
                DefectOverlayImage.Source = ImageSource.FromFile(result.DefectOverlayImagePath);
            }

            SurfaceProfileLabel.Text = result.SurfaceProfile == InspectionCardSurfaceProfile.FoilChrome
                ? "Profile: Foil / Chrome"
                : "Profile: Normal";

            SetPrimaryView(!string.IsNullOrWhiteSpace(defectOverlayPath) && File.Exists(defectOverlayPath)
                ? PrimaryInspectionView.DefectOverlay
                : PrimaryInspectionView.Heatmap);

            if (File.Exists(result.Diagnostics.EdgeOverlayPath))
            {
                EdgeOverlayImage.Source = ImageSource.FromFile(result.Diagnostics.EdgeOverlayPath);
            }

            if (File.Exists(result.Diagnostics.AlignmentAveragePath))
            {
                AlignmentAverageImage.Source = ImageSource.FromFile(result.Diagnostics.AlignmentAveragePath);
            }

            BindFrame(result.Diagnostics.Frames, SurfaceLightDirection.Top,
                TopStatsLabel, TopDetectionImage, TopRegisteredImage, TopEdgeImage);
            BindFrame(result.Diagnostics.Frames, SurfaceLightDirection.Right,
                RightStatsLabel, RightDetectionImage, RightRegisteredImage, RightEdgeImage);
            BindFrame(result.Diagnostics.Frames, SurfaceLightDirection.Bottom,
                BottomStatsLabel, BottomDetectionImage, BottomRegisteredImage, BottomEdgeImage);
            BindFrame(result.Diagnostics.Frames, SurfaceLightDirection.Left,
                LeftStatsLabel, LeftDetectionImage, LeftRegisteredImage, LeftEdgeImage);
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
            {
                detectionImage.Source = ImageSource.FromFile(frame.DetectionOverlayPath);
            }

            if (File.Exists(frame.RegisteredImagePath))
            {
                registeredImage.Source = ImageSource.FromFile(frame.RegisteredImagePath);
            }

            if (File.Exists(frame.EdgeImagePath))
            {
                edgeImage.Source = ImageSource.FromFile(frame.EdgeImagePath);
            }
        }


        private void OnHeatmapToggleClicked(object sender, EventArgs e)
        {
            SetPrimaryView(!string.IsNullOrWhiteSpace(defectOverlayPath) && File.Exists(defectOverlayPath)
                ? PrimaryInspectionView.DefectOverlay
                : PrimaryInspectionView.Heatmap);
        }

        private void OnReliefToggleClicked(object sender, EventArgs e)
        {
            SetPrimaryView(PrimaryInspectionView.Relief);
        }

        private void OnSpecularToggleClicked(object sender, EventArgs e)
        {
            SetPrimaryView(PrimaryInspectionView.Specular);
        }

        private void OnDiffuseToggleClicked(object sender, EventArgs e)
        {
            SetPrimaryView(PrimaryInspectionView.Diffuse);
        }

        private void OnDefectOverlayToggleClicked(object sender, EventArgs e)
        {
            SetPrimaryView(PrimaryInspectionView.DefectOverlay);
        }

        private void SetPrimaryView(PrimaryInspectionView view)
        {
            string imagePath;
            string title;
            string description;

            switch (view)
            {
                case PrimaryInspectionView.Relief:
                    imagePath = reliefPath;
                    title = "Directional relief";
                    description = "High-pass directional lighting map. Good for reading shape changes, waviness and general surface disturbance.";
                    break;
                case PrimaryInspectionView.Specular:
                    imagePath = specularPath;
                    title = "Specular-enhanced defect view";
                    description = result.SurfaceProfile == InspectionCardSurfaceProfile.FoilChrome
                        ? "Foil/Chrome tuning is active. Strong off-axis highlight changes are emphasized while more reflective texture noise is suppressed."
                        : "Strongest off-axis highlight changes are emphasized to make dents, dimples and surface waviness easier to see.";
                    break;
                case PrimaryInspectionView.Diffuse:
                    imagePath = diffusePath;
                    title = "Glare-reduced average";
                    description = "Average of the four directional captures after normalization. Useful as a calmer reference image.";
                    break;
                case PrimaryInspectionView.DefectOverlay:
                    imagePath = defectOverlayPath;
                    title = "Detected flaws overlay";
                    description = "Potential defects are overlaid on the neutral reference card. Yellow is moderate evidence; red is the strongest combined directional evidence.";
                    break;
                default:
                    imagePath = heatmapPath;
                    title = "Potential surface anomalies";
                    description = "Full-card anomaly heatmap after perspective normalization and alignment.";
                    break;
            }

            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                PrimaryViewImage.Source = ImageSource.FromFile(imagePath);
            }

            PrimaryViewTitleLabel.Text = title;
            PrimaryViewDescriptionLabel.Text = description;

            ApplyToggleState(HeatmapToggleButton, view == PrimaryInspectionView.Heatmap);
            ApplyToggleState(ReliefToggleButton, view == PrimaryInspectionView.Relief);
            ApplyToggleState(SpecularToggleButton, view == PrimaryInspectionView.Specular);
            ApplyToggleState(DiffuseToggleButton, view == PrimaryInspectionView.Diffuse);
            ApplyToggleState(DefectOverlayToggleButton, view == PrimaryInspectionView.DefectOverlay);
        }

        private static void ApplyToggleState(Button button, bool isActive)
        {
            button.BackgroundColor = isActive
                ? Color.FromArgb("#1D6A2A")
                : Color.FromArgb("#253047");

            button.TextColor = isActive
                ? Color.FromArgb("#F0FFED")
                : Color.FromArgb("#E2E8F0");
        }

        private async void OnDoneClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}
