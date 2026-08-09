using CollectIQ.Models.Inspection;
using System.Linq;

namespace CollectIQ.Views
{
    public partial class SurfaceInspectionResultPage : ContentPage
    {
        public SurfaceInspectionResultPage(SurfaceInspectionResult result)
        {
            InitializeComponent();

            SummaryLabel.Text = result.Summary;
            AnomalyScoreLabel.Text = $"{result.AnomalyScore:0}/100";
            ConsistencyLabel.Text = $"{result.CaptureConsistencyScore:0}%";
            HeatmapImage.Source = ImageSource.FromFile(result.HeatmapImagePath);
            ReliefImage.Source = ImageSource.FromFile(result.ReliefImagePath);
            DiffuseImage.Source = ImageSource.FromFile(result.DiffuseImagePath);

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

        private async void OnDoneClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}
