using CollectIQ.Models.Inspection;

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
        }

        private async void OnDoneClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}
