using System.Diagnostics;
using CollectIQ.Interfaces;
using CollectIQ.Services.Inspection;
using CollectIQ.Services.Inspection.Geometry;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace CollectIQ.Views
{
    public partial class InspectCornersPage : ContentPage
    {
        private readonly CardBoundaryInspectionService inspectionService;
        private bool cameraReady;
        private bool captureInProgress;
        private bool showingResult;
        private CancellationTokenSource? cameraCts;

        public InspectCornersPage()
        {
            InitializeComponent();
            ICardGeometryService geometry = new CardGeometryService();
            inspectionService = new CardBoundaryInspectionService(geometry);
        }

        protected override async void OnAppearing() { base.OnAppearing(); if(!showingResult) await RestartCameraAsync(); }
        protected override void OnDisappearing() { ReleaseCamera(); base.OnDisappearing(); }

        private void ReleaseCamera()
        {
            cameraCts?.Cancel(); cameraCts?.Dispose(); cameraCts=null; cameraReady=false;
            try { InspectionCameraView?.StopCameraPreview(); } catch { }
        }

        private async Task RestartCameraAsync()
        {
            ReleaseCamera(); ShowCamera(); CaptureButton.IsEnabled=false; CameraStatusLabel.Text="Starting camera…";
            PermissionStatus status=await Permissions.CheckStatusAsync<Permissions.Camera>();
            if(status!=PermissionStatus.Granted) status=await Permissions.RequestAsync<Permissions.Camera>();
            if(status!=PermissionStatus.Granted) { CameraStatusLabel.Text="Camera permission is required."; return; }
            cameraCts=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            try
            {
                for(int i=0;i<30;i++) { if(InspectionCameraView.Width>20 && InspectionCameraView.Height>20) break; await Task.Delay(80,cameraCts.Token); }
                await InspectionCameraView.StartCameraPreview(cameraCts.Token); await Task.Delay(350,cameraCts.Token);
                cameraReady=true; CaptureButton.IsEnabled=true; CaptureButton.Text="● CAPTURE"; CameraStatusLabel.Text="Keep the entire physical card inside the guide.";
            }
            catch(Exception ex) { CameraStatusLabel.Text=$"Camera could not start: {ex.Message}"; }
        }

        private async void OnCaptureClicked(object sender,EventArgs e)
        {
            if(captureInProgress)return;
            if(showingResult) { showingResult=false; await RestartCameraAsync(); return; }
            if(!cameraReady) { await RestartCameraAsync(); if(!cameraReady)return; }
            captureInProgress=true; CaptureButton.IsEnabled=false;
            try
            {
                using CancellationTokenSource timeout=new(TimeSpan.FromSeconds(10));
                using Stream stream=await InspectionCameraView.CaptureImage(timeout.Token);
                string dir=Path.Combine(FileSystem.AppDataDirectory,"BoundaryInspections","Inputs");Directory.CreateDirectory(dir);
                string path=Path.Combine(dir,"corners_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff")+".jpg");
                await using(FileStream output=new(path,FileMode.Create,FileAccess.Write,FileShare.None)) await stream.CopyToAsync(output);
                ReleaseCamera(); await AnalyzeAsync(path);
            }
            catch(Exception ex) { CameraStatusLabel.Text=$"Capture failed: {ex.Message}"; CaptureButton.IsEnabled=true; }
            finally { captureInProgress=false; }
        }

        private async void OnLoadPhotoClicked(object sender,EventArgs e)
        {
            try
            {
                FileResult? photo=await MediaPicker.Default.PickPhotoAsync(); if(photo==null)return;
                string dir=Path.Combine(FileSystem.AppDataDirectory,"BoundaryInspections","Inputs");Directory.CreateDirectory(dir);
                string ext=Path.GetExtension(photo.FileName);if(string.IsNullOrWhiteSpace(ext))ext=".jpg";
                string path=Path.Combine(dir,"corners_picked_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff")+ext);
                await using Stream input=await photo.OpenReadAsync();await using(FileStream output=new(path,FileMode.Create,FileAccess.Write,FileShare.None))await input.CopyToAsync(output);
                ReleaseCamera();await AnalyzeAsync(path);
            }
            catch(Exception ex) { await DisplayAlert("Inspection",ex.Message,"OK"); }
        }

        private async Task AnalyzeAsync(string path)
        {
            BusyIndicator.IsVisible=true;BusyIndicator.IsRunning=true;SummaryLabel.Text="Finding the physical card with the Centering detector and analyzing the four corners…";
            try
            {
                CardBoundaryInspectionResult r=await inspectionService.AnalyzeAsync(path);
                showingResult=true;ShowResult();CaptureButton.Text="↻ RETAKE";CaptureButton.IsEnabled=true;
                Score1.Text=Format(r.TopLeft); Score2.Text=Format(r.TopRight); Score3.Text=Format(r.BottomLeft); Score4.Text=Format(r.BottomRight); ResultImage.Source=ImageSource.FromFile(r.NormalizedImagePath);
                AnalysisImage.Source=ImageSource.FromFile(r.CornerOverlayPath);
                TopLeftCloseupImage.Source=ImageSource.FromFile(r.TopLeftCloseupPath);
                TopRightCloseupImage.Source=ImageSource.FromFile(r.TopRightCloseupPath);
                BottomLeftCloseupImage.Source=ImageSource.FromFile(r.BottomLeftCloseupPath);
                BottomRightCloseupImage.Source=ImageSource.FromFile(r.BottomRightCloseupPath);
                TopLeftExplanationLabel.Text=r.TopLeftExplanation;
                TopRightExplanationLabel.Text=r.TopRightExplanation;
                BottomLeftExplanationLabel.Text=r.BottomLeftExplanation;
                BottomRightExplanationLabel.Text=r.BottomRightExplanation;
                SummaryLabel.Text=$"Physical card detected at {r.DetectionConfidence:0}% confidence. The full card remains visible above; use the magnified closeups to inspect each highlighted candidate.";
            }
            catch(Exception ex) { SummaryLabel.Text=ex.Message;showingResult=false;ShowCamera();await RestartCameraAsync(); }
            finally { BusyIndicator.IsVisible=false;BusyIndicator.IsRunning=false; }
        }

        private static string Format(RegionScore s)=>$"{s.DamageScore:0}/100 • {s.Label}";
        private void ShowCamera() { InspectionCameraView.IsVisible=true;CameraGuide.IsVisible=true;CameraStatusLabel.IsVisible=true;ResultImage.IsVisible=false;AnalysisImage.Source=null; }
        private void ShowResult() { InspectionCameraView.IsVisible=false;CameraGuide.IsVisible=false;CameraStatusLabel.IsVisible=false;ResultImage.IsVisible=true; }
    }
}
