//
//  FILE            : WebAuthenticatorCallbackActivity.cs
//  PROJECT         : CollectIQ (Mobile Application)
//  PROGRAMMER      : Darryl Poworoznyk
//  FIRST VERSION   : 2026-03-04
//  DESCRIPTION     :
//      Android callback Activity for MAUI WebAuthenticator.
//      This enables the app to receive OAuth redirect URIs like:
//
//          collectiq://auth?code=...&state=...
//
//      The MAUI base class handles routing the callback back to the
//      awaiting WebAuthenticator.AuthenticateAsync() call.
//
using Android.App;
using Android.Content;
using Android.Content.PM;

namespace CollectIQ.Platforms.Android
{
    /// <summary>
    /// Receives OAuth redirect callbacks on Android for MAUI WebAuthenticator.
    /// </summary>
    [Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataScheme = "collectiq",
        DataHost = "auth")]
    public sealed class WebAuthCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
    {
        // No additional code required.
        // The MAUI base class reads the redirect URI and forwards the result.
    }
}