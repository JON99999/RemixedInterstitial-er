using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Threading;

namespace InterstitialerMaui.WinUI;

/// <summary>
/// Program class containing the suitable Main entry point for WinUI.
/// </summary>
public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Explicitly set WebView2 User Data Folder (UDF) to a safe, user-writable LocalAppData path.
        // On unpackaged apps (WindowsPackageType=None), WebView2 defaults to the EXE folder.
        // If the EXE runs from a non-writable/ProgramFiles folder or a restricted directory, WebView2
        // initialization fails silently, rendering a blank screen.
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string uDataFolder = System.IO.Path.Combine(localAppData, "Interstitial-er", "WebView2");
            System.IO.Directory.CreateDirectory(uDataFolder);
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", uDataFolder);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set WEBVIEW2_USER_DATA_FOLDER: {ex.Message}");
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
