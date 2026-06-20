using System;
using System.IO;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace InterstitialerMaui
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new MainPage());
            string appMode = "Admin";
            try
            {
                string distFolder = GetDistFolderPath();
                string configPath = Path.Combine(distFolder, "app-config.json");
                if (File.Exists(configPath))
                {
                    var raw = File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("mode", out var modeProp))
                    {
                        appMode = modeProp.GetString() ?? "Admin";
                    }
                }
            }
            catch
            {
                // Fallback to default
            }

            window.Title = appMode == "Player" ? "Interstitial-er Player - MAUI" : "Interstitial-er Admin - MAUI";

            if (appMode == "Player")
            {
                // Lock Player to a thin vertical standard strip of 250px on the far left (Exact Parity)
                window.Width = 250;
                window.MinimumWidth = 250;
                window.MaximumWidth = 250;
                window.X = 0;
                window.Y = 0;

                try
                {
                    var devDisplay = DeviceDisplay.Current.MainDisplayInfo;
                    if (devDisplay.Height > 0)
                    {
                        window.Height = devDisplay.Height / devDisplay.Density;
                    }
                    else
                    {
                        window.Height = 800;
                    }
                }
                catch
                {
                    window.Height = 800;
                }
            }
            else
            {
                window.Width = 800;
                window.Height = 800;
                window.MinimumWidth = 400;
                window.MinimumHeight = 400;
                
                // Center the window on display if possible
                try
                {
                    var devDisplay = DeviceDisplay.Current.MainDisplayInfo;
                    if (devDisplay.Width > 0 && devDisplay.Height > 0)
                    {
                        double screenWidth = devDisplay.Width / devDisplay.Density;
                        double screenHeight = devDisplay.Height / devDisplay.Density;
                        window.X = (screenWidth - 800) / 2;
                        window.Y = (screenHeight - 800) / 2;
                    }
                }
                catch
                {
                }
            }

            return window;
        }

        public static string GetDistFolderPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // Direct relative
            var path1 = Path.Combine(baseDir, "dist");
            if (Directory.Exists(path1)) return path1;

            // Catalyst/iOS bundle checks 
            var path2 = Path.Combine(baseDir, "..", "Resources", "dist");
            if (Directory.Exists(path2)) return Path.GetFullPath(path2);

            var path3 = Path.Combine(baseDir, "Contents", "Resources", "dist");
            if (Directory.Exists(path3)) return Path.GetFullPath(path3);

            // Backwards traverse to root for easy IDE debugging
            var current = baseDir;
            for (int i = 0; i < 5; i++)
            {
                var test = Path.Combine(current, "dist");
                if (Directory.Exists(test)) return test;
                
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || parent == current) break;
                current = parent;
            }

            return baseDir;
        }
    }
}
