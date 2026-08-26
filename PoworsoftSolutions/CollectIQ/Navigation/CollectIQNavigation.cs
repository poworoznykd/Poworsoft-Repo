using CollectIQ.Views;
using Microsoft.Maui.Controls;

namespace CollectIQ.Navigation
{
    /// <summary>
    /// Centralizes navigation between Collect and Inspect so global Shell routes
    /// are never mixed with stale inspection stacks.
    /// </summary>
    public static class CollectIQNavigation
    {
        private static readonly SemaphoreSlim NavigationGate = new(1, 1);

        public static async Task GoToCollectAsync(string rootRoute = "DashboardPage")
        {
            if (Shell.Current == null)
                return;

            await NavigationGate.WaitAsync();
            try
            {
                await Shell.Current.GoToAsync($"//{rootRoute}", false);
            }
            finally
            {
                NavigationGate.Release();
            }
        }

        public static async Task GoToInspectAsync(string? inspectionRoute = null)
        {
            if (Shell.Current == null)
                return;

            await NavigationGate.WaitAsync();
            try
            {
                // Inspect pages are registered global routes rather than Shell roots.
                // Always begin from the known Dashboard root so repeated mode changes
                // cannot leave an old inspection page buried in another tab's stack.
                await Shell.Current.GoToAsync("//DashboardPage", false);
                await Shell.Current.GoToAsync(nameof(InspectHubPage), false);

                if (!string.IsNullOrWhiteSpace(inspectionRoute) &&
                    !string.Equals(inspectionRoute, nameof(InspectHubPage), StringComparison.Ordinal))
                {
                    await Shell.Current.GoToAsync(inspectionRoute, false);
                }
            }
            finally
            {
                NavigationGate.Release();
            }
        }
    }
}
