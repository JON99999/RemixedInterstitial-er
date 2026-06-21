using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace InterstitialerMaui
{
    public partial class MainPage : ContentPage
    {
        private LocalBackendServer? _server;
        private int _serverPort = 3000;

        public MainPage()
        {
            InitializeComponent();
            StartBackendAndLoadWebView();
        }

        private async void StartBackendAndLoadWebView()
        {
            try
            {
                // Resolve a free port
                _serverPort = GetFreePort(3000);
                Debug.WriteLine($"Starting local C# server on port: {_serverPort}");

                // Start local server in parallel background thread
                string distFolder = App.GetDistFolderPath();
                _server = new LocalBackendServer(_serverPort, distFolder, this);
                _server.Start();

                // Point the WebView to our local listener URL
                AppWebView.Source = $"http://127.0.0.1:{_serverPort}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Encountered error in MAUI startup pipeline: {ex.Message}");
            }
        }

        private int GetFreePort(int startingPort)
        {
            int port = startingPort;
            while (port < startingPort + 100)
            {
                try
                {
                    using var tcpListener = new TcpListener(IPAddress.Loopback, port);
                    tcpListener.Start();
                    tcpListener.Stop();
                    return port;
                }
                catch (SocketException)
                {
                    port++;
                }
            }
            return port;
        }

        public async Task<string?> PickDirectoryAsync()
        {
            return await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
#if WINDOWS
                    var picker = new Windows.Storage.Pickers.FolderPicker();
                    if (App.Current != null && App.Current.Windows.Count > 0)
                    {
                        var handler = App.Current.Windows[0].Handler;
                        if (handler != null && handler.PlatformView != null)
                        {
                            var window = (Microsoft.UI.Xaml.Window)handler.PlatformView;
                            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                        }
                    }
                    picker.FileTypeFilter.Add("*");
                    var folder = await picker.PickSingleFolderAsync();
                    return folder?.Path;
#elif MACCATALYST
                    // OS native apple script folder selector prompt 
                    using var proc = new System.Diagnostics.Process {
                        StartInfo = new System.Diagnostics.ProcessStartInfo {
                            FileName = "osascript",
                            Arguments = "-e \"POSIX path of (choose folder with prompt \\\"Select Folder\\\")\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };
                    proc.Start();
                    string output = await proc.StandardOutput.ReadToEndAsync();
                    await proc.WaitForExitAsync();
                    return output.Trim();
#else
                    await Task.CompletedTask;
                    return null;
#endif
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Native Picker failed to execute: {ex.Message}");
                    return null;
                }
            });
        }
    }
}
