using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;

namespace InterstitialerMaui
{
    public class LocalBackendServer
    {
        private readonly HttpListener _listener;
        private readonly int _port;
        private readonly string _distFolder;
        private readonly MainPage _mainPage;
        private bool _isRunning;
        private string? _registeredOAuthToken;

        private readonly string _baseDir;
        private readonly string _dataDir;
        private readonly string _logDir;
        private readonly string _scheduleFileDefault;
        private readonly string _logFileDefault;
        private readonly string _settingsFile;

        private Dictionary<string, string> _currentSettings;

        public LocalBackendServer(int port, string distFolder, MainPage mainPage)
        {
            _port = port;
            _distFolder = distFolder;
            _mainPage = mainPage;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");

            // Setup AppData persistent base locations
            _baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Interstitial-er");
            _dataDir = Path.Combine(_baseDir, "data");
            _logDir = Path.Combine(_baseDir, "Scheduler Logs");
            _scheduleFileDefault = Path.Combine(_dataDir, "schedules.json");
            _logFileDefault = Path.Combine(_logDir, "logs.json");
            _settingsFile = Path.Combine(_dataDir, "settings.json");

            Directory.CreateDirectory(_dataDir);
            Directory.CreateDirectory(_logDir);

            // Default fallback settings
            _currentSettings = new Dictionary<string, string>
            {
                { "mode", "Demo" },
                { "localPathMP3s", "" },
                { "localPathLogs", "" },
                { "localPathSchedules", "" },
                { "driveFolderLogs", "" },
                { "driveFolderMP3s", "" },
                { "driveFolderPreferences", "" }
            };

            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    string raw = File.ReadAllText(_settingsFile, Encoding.UTF8);
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(raw);
                    if (parsed != null)
                    {
                        foreach (var kvp in parsed)
                        {
                            _currentSettings[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load settings file config: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                string raw = JsonSerializer.Serialize(_currentSettings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFile, raw, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to preserve settings: {ex.Message}");
            }
        }

        private string GetScheduleFilePath()
        {
            if (_currentSettings.TryGetValue("mode", out var mode) && mode == "Local" &&
                _currentSettings.TryGetValue("localPathSchedules", out var customPath) && !string.IsNullOrWhiteSpace(customPath))
            {
                Directory.CreateDirectory(customPath);
                return Path.Combine(customPath, "schedules.json");
            }
            return _scheduleFileDefault;
        }

        private string GetLogFilePath()
        {
            if (_currentSettings.TryGetValue("mode", out var mode) && mode == "Local" &&
                _currentSettings.TryGetValue("localPathLogs", out var customPath) && !string.IsNullOrWhiteSpace(customPath))
            {
                Directory.CreateDirectory(customPath);
                return Path.Combine(customPath, "logs.json");
            }
            return _logFileDefault;
        }

        private string GetLogBackupPath()
        {
            if (_currentSettings.TryGetValue("mode", out var mode) && mode == "Local" &&
                _currentSettings.TryGetValue("localPathLogs", out var customPath) && !string.IsNullOrWhiteSpace(customPath))
            {
                return Path.Combine(customPath, "backups", "logs_backup.json");
            }
            return Path.Combine(_logDir, "backups", "logs_backup.json");
        }

        private string GetScheduleBackupPath()
        {
            if (_currentSettings.TryGetValue("mode", out var mode) && mode == "Local" &&
                _currentSettings.TryGetValue("localPathSchedules", out var customPath) && !string.IsNullOrWhiteSpace(customPath))
            {
                return Path.Combine(customPath, "backups", "schedules_backup.json");
            }
            return Path.Combine(_dataDir, "backups", "schedules_backup.json");
        }

        public void Start()
        {
            _isRunning = true;
            _listener.Start();
            Task.Run(ListenLoop);
        }

        public void Stop()
        {
            _isRunning = false;
            _listener.Stop();
        }

        private async Task ListenLoop()
        {
            while (_isRunning)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch (Exception ex)
                {
                    if (!_isRunning) break;
                    Debug.WriteLine($"Error inside C# Server Http Listener pump: {ex.Message}");
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            var req = context.Request;
            var resp = context.Response;
            var path = req.Url?.AbsolutePath ?? "/";
            var method = req.HttpMethod;

            // Simple CORS support (Parity)
            resp.AddHeader("Access-Control-Allow-Origin", "*");
            resp.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS, PUT, DELETE");
            resp.AddHeader("Access-Control-Allow-Headers", "Content-Type, X-Requested-With, Authorization");

            if (method == "OPTIONS")
            {
                resp.StatusCode = (int)HttpStatusCode.OK;
                resp.Close();
                return;
            }

            try
            {
                // Router section
                if (path == "/api/settings" && method == "GET")
                {
                    await WriteJsonResponse(resp, _currentSettings);
                }
                else if (path == "/api/settings" && method == "POST")
                {
                    string body = await ReadBodyAsync(req);
                    using var doc = JsonDocument.Parse(body);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        _currentSettings[prop.Name] = prop.Value.GetString() ?? "";
                    }
                    SaveSettings();
                    await WriteJsonResponse(resp, new { success = true, settings = _currentSettings });
                }
                else if (path == "/api/register-token" && method == "POST")
                {
                    string body = await ReadBodyAsync(req);
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("token", out var tokProp))
                    {
                        _registeredOAuthToken = tokProp.GetString();
                        await WriteJsonResponse(resp, new { success = true });
                    }
                    else
                    {
                        await WriteErrorResponse(resp, HttpStatusCode.BadRequest, "Token is required");
                    }
                }
                else if (path == "/api/check-registered-token" && method == "GET")
                {
                    var tok = _registeredOAuthToken;
                    _registeredOAuthToken = null; // consume
                    await WriteJsonResponse(resp, new { token = tok });
                }
                else if (path == "/api/oauth-callback" && method == "GET")
                {
                    // Serve OAuth loopback HTML exactly as returned in server.ts
                    string html = GetOAuthHTMLContent();
                    await WriteHtmlResponse(resp, html);
                }
                else if (path == "/api/check-local-paths" && method == "POST")
                {
                    string body = await ReadBodyAsync(req);
                    using var doc = JsonDocument.Parse(body);
                    string mp3 = doc.RootElement.TryGetProperty("localPathMP3s", out var m1) ? m1.GetString() ?? "" : "";
                    string logs = doc.RootElement.TryGetProperty("localPathLogs", out var l1) ? l1.GetString() ?? "" : "";
                    string scheds = doc.RootElement.TryGetProperty("localPathSchedules", out var s1) ? s1.GetString() ?? "" : "";

                    bool mp3Exists = string.IsNullOrWhiteSpace(mp3) || Directory.Exists(mp3);
                    bool logsExists = string.IsNullOrWhiteSpace(logs) || Directory.Exists(logs);
                    bool schedExists = string.IsNullOrWhiteSpace(scheds) || Directory.Exists(scheds);

                    await WriteJsonResponse(resp, new {
                        exists = mp3Exists && logsExists && schedExists,
                        mp3Exists = mp3Exists,
                        logsExists = logsExists,
                        schedExists = schedExists
                    });
                }
                else if (path == "/api/create-local-paths" && method == "POST")
                {
                    string body = await ReadBodyAsync(req);
                    using var doc = JsonDocument.Parse(body);
                    string mp3 = doc.RootElement.TryGetProperty("localPathMP3s", out var m1) ? m1.GetString() ?? "" : "";
                    string logs = doc.RootElement.TryGetProperty("localPathLogs", out var l1) ? l1.GetString() ?? "" : "";
                    string sDir = doc.RootElement.TryGetProperty("localPathSchedules", out var s1) ? s1.GetString() ?? "" : "";

                    int createdCount = 0;
                    string[] dirsToCreate = { mp3, logs, sDir };
                    foreach (var d in dirsToCreate)
                    {
                        if (!string.IsNullOrWhiteSpace(d) && !Directory.Exists(d))
                        {
                            Directory.CreateDirectory(d);
                            createdCount++;
                        }
                    }
                    await WriteJsonResponse(resp, new { success = true, createdCount = createdCount });
                }
                else if (path == "/api/browse-folder" && method == "POST")
                {
                    string? selected = await _mainPage.PickDirectoryAsync();
                    if (string.IsNullOrEmpty(selected))
                    {
                        await WriteJsonResponse(resp, new { success = true, cancelled = true });
                    }
                    else
                    {
                        await WriteJsonResponse(resp, new { success = true, path = selected });
                    }
                }
                else if (path == "/api/downloads-path" && method == "GET")
                {
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string downloads = Path.Combine(home, "Downloads");
                    await WriteJsonResponse(resp, new { success = true, path = downloads });
                }
                else if (path == "/api/list-directories" && method == "GET")
                {
                    string target = req.QueryString["path"] ?? "";
                    if (string.IsNullOrWhiteSpace(target))
                    {
                        target = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    }

                    if (!Directory.Exists(target))
                    {
                        await WriteJsonResponse(resp, new { success = false, error = "Path does not exist" });
                        return;
                    }

                    var folders = new List<string>();
                    var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
                    foreach (var d in Directory.EnumerateDirectories(target, "*", options))
                    {
                        folders.Add(Path.GetFileName(d));
                    }
                    folders.Sort();

                    string? parent = Path.GetDirectoryName(target);
                    await WriteJsonResponse(resp, new {
                        success = true,
                        currentPath = target,
                        parentPath = parent != target ? parent : null,
                        folders = folders
                    });
                }
                else if (path == "/api/local-mp3s" && method == "GET")
                {
                    _currentSettings.TryGetValue("localPathMP3s", out var folderPath);
                    if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                    {
                        await WriteJsonResponse(resp, Array.Empty<object>());
                        return;
                    }

                    var list = new List<object>();
                    foreach (var f in Directory.EnumerateFiles(folderPath, "*.mp3"))
                    {
                        var info = new FileInfo(f);
                        double sizeMb = (double)info.Length / (1024 * 1024);
                        list.Add(new {
                            name = Path.GetFileName(f),
                            size = $"{sizeMb:F1} MB",
                            duration = "0:15",
                            path = $"/api/stream-local?file={Uri.EscapeDataString(Path.GetFileName(f))}"
                        });
                    }
                    await WriteJsonResponse(resp, list);
                }
                else if (path == "/api/stream-local" && method == "GET")
                {
                    string fileName = req.QueryString["file"] ?? "";
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        await WriteErrorResponse(resp, HttpStatusCode.BadRequest, "Filename required");
                        return;
                    }

                    _currentSettings.TryGetValue("localPathMP3s", out var folderPath);
                    if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                    {
                        await WriteErrorResponse(resp, HttpStatusCode.NotFound, "Local source folder not defined or offline");
                        return;
                    }

                    string fullPath = Path.Combine(folderPath, Path.GetFileName(fileName));
                    if (File.Exists(fullPath))
                    {
                        resp.ContentType = "audio/mpeg";
                        resp.ContentLength64 = new FileInfo(fullPath).Length;
                        using var fileStream = File.OpenRead(fullPath);
                        await fileStream.CopyToAsync(resp.OutputStream);
                        resp.OutputStream.Close();
                    }
                    else
                    {
                        await WriteErrorResponse(resp, HttpStatusCode.NotFound, "File not found");
                    }
                }
                else if (path == "/api/schedules" && method == "GET")
                {
                    string filePath = GetScheduleFilePath();
                    if (!File.Exists(filePath))
                    {
                        await WriteJsonResponse(resp, Array.Empty<object>());
                        return;
                    }

                    string raw = File.ReadAllText(filePath, Encoding.UTF8);
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("data", out var dataProp))
                    {
                        await WriteJsonResponse(resp, dataProp);
                    }
                    else
                    {
                        await WriteJsonResponse(resp, doc.RootElement);
                    }
                }
                else if (path == "/api/schedules" && method == "POST")
                {
                    string body = await ReadBodyAsync(req);
                    using var doc = JsonDocument.Parse(body);
                    string filePath = GetScheduleFilePath();
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                    int counter = 0;
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            string raw = File.ReadAllText(filePath, Encoding.UTF8);
                            using var parsed = JsonDocument.Parse(raw);
                            if (parsed.RootElement.ValueKind == JsonValueKind.Object && parsed.RootElement.TryGetProperty("ScheduleBackupCounter", out var cProp))
                            {
                                counter = cProp.GetInt32();
                            }
                        }
                        catch {}
                    }

                    counter++;
                    var payload = new { ScheduleBackupCounter = counter, data = doc.RootElement };
                    string payloadStr = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(filePath, payloadStr, Encoding.UTF8);

                    // Backup secondary
                    try
                    {
                        string bk = GetScheduleBackupPath();
                        Directory.CreateDirectory(Path.GetDirectoryName(bk)!);
                        File.WriteAllText(bk, payloadStr, Encoding.UTF8);
                    }
                    catch {}

                    await WriteJsonResponse(resp, new { success = true });
                }
                else if (path == "/api/logs" && method == "GET")
                {
                    string filePath = GetLogFilePath();
                    if (!File.Exists(filePath))
                    {
                        await WriteJsonResponse(resp, Array.Empty<object>());
                        return;
                    }

                    string raw = File.ReadAllText(filePath, Encoding.UTF8);
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("data", out var dataProp))
                    {
                        await WriteJsonResponse(resp, dataProp);
                    }
                    else
                    {
                        await WriteJsonResponse(resp, doc.RootElement);
                    }
                }
                else if (path == "/api/logs" && method == "POST")
                {
                    string body = await ReadBodyAsync(req);
                    using var newLogElement = JsonDocument.Parse(body);

                    string filePath = GetLogFilePath();
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                    var logsList = new List<JsonElement>();
                    int counter = 0;

                    if (File.Exists(filePath))
                    {
                        try
                        {
                            string raw = File.ReadAllText(filePath, Encoding.UTF8);
                            using var parsed = JsonDocument.Parse(raw);
                            if (parsed.RootElement.ValueKind == JsonValueKind.Object)
                            {
                                if (parsed.RootElement.TryGetProperty("LogsBackupCounter", out var cProp))
                                {
                                    counter = cProp.GetInt32();
                                }
                                if (parsed.RootElement.TryGetProperty("data", out var listProp) && listProp.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var child in listProp.Clone().EnumerateArray())
                                    {
                                        logsList.Add(child);
                                    }
                                }
                            }
                            else if (parsed.RootElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var child in parsed.RootElement.Clone().EnumerateArray())
                                {
                                    logsList.Add(child);
                                }
                            }
                        }
                        catch {}
                    }

                    logsList.Add(newLogElement.RootElement.Clone());
                    counter++;

                    var payload = new { LogsBackupCounter = counter, data = logsList };
                    string payloadStr = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(filePath, payloadStr, Encoding.UTF8);

                    // Sync backup
                    try
                    {
                        string bk = GetLogBackupPath();
                        Directory.CreateDirectory(Path.GetDirectoryName(bk)!);
                        File.WriteAllText(bk, payloadStr, Encoding.UTF8);
                    }
                    catch {}

                    await WriteJsonResponse(resp, new { success = true });
                }
                else if (path == "/api/open-local-folder" && method == "POST")
                {
                    string body = await ReadBodyAsync(req);
                    using var doc = JsonDocument.Parse(body);
                    string target = doc.RootElement.TryGetProperty("path", out var p1) ? p1.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(target) && Directory.Exists(target))
                    {
                        if (OperatingSystem.IsWindows())
                        {
                            Process.Start("explorer.exe", $"\"{target}\"");
                        }
                        else if (OperatingSystem.IsMacCatalyst())
                        {
                            Process.Start("open", $"\"{target}\"");
                        }
                        await WriteJsonResponse(resp, new { success = true });
                    }
                    else
                    {
                        await WriteErrorResponse(resp, HttpStatusCode.NotFound, "Directory not found");
                    }
                }
                else if (path == "/api/trigger-backup" && method == "POST")
                {
                    string schedPath = GetScheduleFilePath();
                    string logPath = GetLogFilePath();

                    var now = DateTime.Now;
                    string dateStr = now.ToString("yyyy_MM_dd");

                    // Backup schedules
                    if (File.Exists(schedPath))
                    {
                        string raw = File.ReadAllText(schedPath, Encoding.UTF8);
                        using var doc = JsonDocument.Parse(raw);
                        int counter = 0;
                        JsonElement arrayData = doc.RootElement;

                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            if (doc.RootElement.TryGetProperty("ScheduleBackupCounter", out var cVal)) counter = cVal.GetInt32();
                            if (doc.RootElement.TryGetProperty("data", out var dVal)) arrayData = dVal;
                        }

                        counter++;
                        var payload = new { ScheduleBackupCounter = counter, data = arrayData.Clone() };
                        string payloadStr = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(schedPath, payloadStr, Encoding.UTF8);

                        string bkDir = Path.Combine(Path.GetDirectoryName(schedPath)!, "backups");
                        Directory.CreateDirectory(bkDir);
                        string fileName = $"schedules_Backup_{dateStr}_{counter:D8}.json";
                        File.WriteAllText(Path.Combine(bkDir, fileName), payloadStr, Encoding.UTF8);

                        // Save fallback copy
                        string fb = GetScheduleBackupPath();
                        Directory.CreateDirectory(Path.GetDirectoryName(fb)!);
                        File.WriteAllText(fb, payloadStr, Encoding.UTF8);
                    }

                    // Backup logs
                    if (File.Exists(logPath))
                    {
                        string raw = File.ReadAllText(logPath, Encoding.UTF8);
                        using var doc = JsonDocument.Parse(raw);
                        int counter = 0;
                        JsonElement arrayData = doc.RootElement;

                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            if (doc.RootElement.TryGetProperty("LogsBackupCounter", out var cVal)) counter = cVal.GetInt32();
                            if (doc.RootElement.TryGetProperty("data", out var dVal)) arrayData = dVal;
                        }

                        counter++;
                        var payload = new { LogsBackupCounter = counter, data = arrayData.Clone() };
                        string payloadStr = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(logPath, payloadStr, Encoding.UTF8);

                        string bkDir = Path.Combine(Path.GetDirectoryName(logPath)!, "backups");
                        Directory.CreateDirectory(bkDir);
                        string fileName = $"logs_Backup_{dateStr}_{counter:D8}.json";
                        File.WriteAllText(Path.Combine(bkDir, fileName), payloadStr, Encoding.UTF8);

                        // Save fallback copy
                        string fb = GetLogBackupPath();
                        Directory.CreateDirectory(Path.GetDirectoryName(fb)!);
                        File.WriteAllText(fb, payloadStr, Encoding.UTF8);
                    }

                    await WriteJsonResponse(resp, new { success = true });
                }
                else if (path == "/api/export-prerecord" && method == "POST")
                {
                    string body = await ReadBodyAsync(req);
                    using var doc = JsonDocument.Parse(body);

                    string dateStrRaw = doc.RootElement.GetProperty("prerecordDate").GetString() ?? "";
                    double lenMinutes = doc.RootElement.GetProperty("lengthMinutes").GetDouble();
                    var items = doc.RootElement.GetProperty("items").EnumerateArray();
                    string destOverride = doc.RootElement.TryGetProperty("exportDestination", out var ed) ? ed.GetString() ?? "" : "";
                    string fPrefix = doc.RootElement.TryGetProperty("folderPrefix", out var fp) ? fp.GetString() ?? "" : "Show";
                    string tPrefix = doc.RootElement.TryGetProperty("textPrefix", out var tp) ? tp.GetString() ?? "" : "Show";
                    string pPrefix = doc.RootElement.TryGetProperty("playlistPrefix", out var pp) ? pp.GetString() ?? "" : "Show";

                    _currentSettings.TryGetValue("localPathMP3s", out var mp3sFolder);
                    string sourceFolder = string.IsNullOrWhiteSpace(mp3sFolder) ? _dataDir : mp3sFolder;
                    string destBaseFolder = !string.IsNullOrWhiteSpace(destOverride) ? destOverride : sourceFolder;

                    Directory.CreateDirectory(destBaseFolder);

                    DateTime dTime = DateTime.Parse(dateStrRaw);
                    string dateFlipped = dTime.ToString("yyyy-MM'('MMM')'-dd").ToUpper();
                    string timeFlipped = dTime.ToString("HH-mm");

                    int hours = (int)(lenMinutes / 60);
                    int mins = (int)(lenMinutes % 60);
                    string durationStr = mins == 0 ? $"{hours} Hrs" : $"{hours} Hrs {mins} Min";

                    string folderName = $"{fPrefix} - Export - {dateFlipped} at {timeFlipped} - {durationStr}";
                    string finalExportFolder = Path.Combine(destBaseFolder, folderName);
                    Directory.CreateDirectory(finalExportFolder);

                    int copied = 0;
                    int missing = 0;
                    var report = new List<object>();

                    var m3uLines = new List<string> { "#EXTM3U" };
                    var txtLines = new List<string>
                    {
                        "========================================================================",
                        "              PRERECORD BROADCAST SCHEDULE SUMMARY",
                        "========================================================================",
                        $"Air Date: {dateFlipped}",
                        $"Start Time: {dTime:HH:mm}",
                        $"Duration: {lenMinutes} minutes",
                        "========================================================================",
                        "",
                        "SEQUENCE OF SCHEDULED SPECIALS & BREAKS:",
                        "------------------------------------------------------------------------"
                    };

                    int index = 0;
                    foreach (var item in items)
                    {
                        index++;
                        string slot = item.GetProperty("slotTime").GetString() ?? "00:00";
                        string slotSafe = slot.Replace(':', '-');
                        string schedNameRaw = item.GetProperty("scheduleName").GetString() ?? "Unnamed Break";
                        string filteredName = string.Concat(schedNameRaw.Split(Path.GetInvalidFileNameChars())).Trim();

                        string finalFileName = $"Break {index:D2} at {slotSafe} - {filteredName}.mp3";
                        string origFileName = item.GetProperty("fileName").GetString() ?? "";

                        string sourcePath = Path.Combine(sourceFolder, Path.GetFileName(origFileName));
                        string destPath = Path.Combine(finalExportFolder, finalFileName);

                        string status = "Missing";
                        if (!string.IsNullOrWhiteSpace(origFileName) && File.Exists(sourcePath))
                        {
                            try
                            {
                                File.Copy(sourcePath, destPath, true);
                                copied++;
                                status = "Found & Copied";
                            }
                            catch (Exception ex)
                            {
                                missing++;
                                status = $"Copy Error: {ex.Message}";
                            }
                        }
                        else
                        {
                            missing++;
                        }

                        report.Add(new {
                            index = index,
                            slotTime = slot,
                            scheduleName = schedNameRaw,
                            originalFile = origFileName,
                            exportedFile = finalFileName,
                            status = status
                        });

                        m3uLines.Add($"#EXTINF:-1,Break {index} - {slot} - {schedNameRaw}");
                        m3uLines.Add(finalFileName);

                        if (status == "Found & Copied")
                        {
                            txtLines.Add($"{index}. Slot: {slot}");
                            txtLines.Add($"   Exported File: {finalFileName}");
                            txtLines.Add($"   Title: {schedNameRaw}");
                            txtLines.Add($"   Source File: {origFileName}");
                        }
                        else
                        {
                            txtLines.Add($"{index}. MISSING FILE - THIS FILE COULD NOT BE FOUND.  PLEASE REVERIFY AND EXPORT.");
                            txtLines.Add($"   Slot: {slot}");
                            txtLines.Add($"   Exported File: {finalFileName}");
                            txtLines.Add($"   Title: {schedNameRaw}");
                            txtLines.Add($"   Source File: {origFileName}");
                        }
                        txtLines.Add("------------------------------------------------------------------------");
                    }

                    string txtName = $"{tPrefix} - Plan - {dateFlipped} at {timeFlipped} - {durationStr}.txt";
                    string m3uName = $"{pPrefix} - Playlist - {dateFlipped} at {timeFlipped} - {durationStr}.m3u";

                    File.WriteAllLines(Path.Combine(finalExportFolder, txtName), txtLines, Encoding.UTF8);
                    File.WriteAllLines(Path.Combine(finalExportFolder, m3uName), m3uLines, Encoding.UTF8);

                    await WriteJsonResponse(resp, new {
                        success = true,
                        exportFolderPath = finalExportFolder,
                        exportFolderName = folderName,
                        copiedCount = copied,
                        missingCount = missing,
                        totalCount = index,
                        txtFilename = txtName,
                        m3uFilename = m3uName,
                        report = report
                    });
                }
                else if (path == "/api/write-file" && method == "POST")
                {
                    string body = await ReadBodyAsync(req);
                    using var doc = JsonDocument.Parse(body);
                    string dest = doc.RootElement.GetProperty("filePath").GetString() ?? "";
                    bool isBinary = doc.RootElement.TryGetProperty("isBinary", out var ib) && ib.GetBoolean();
                    string content = doc.RootElement.GetProperty("content").GetString() ?? "";

                    if (string.IsNullOrWhiteSpace(dest))
                    {
                        await WriteErrorResponse(resp, HttpStatusCode.BadRequest, "filePath is required");
                        return;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    if (isBinary)
                    {
                        byte[] data = Convert.FromBase64String(content);
                        File.WriteAllBytes(dest, data);
                    }
                    else
                    {
                        File.WriteAllText(dest, content, Encoding.UTF8);
                    }
                    await WriteJsonResponse(resp, new { success = true });
                }
                else
                {
                    // Fallback to serving assets from compiled React bundle
                    await ServeStaticWebAsset(resp, path);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to process HTTP endpoint {path}: {ex.Message}");
                await WriteErrorResponse(resp, HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private async Task ServeStaticWebAsset(HttpListenerResponse resp, string relativePath)
        {
            if (relativePath == "/") relativePath = "/index.html";

            // Map safe file paths
            string rawFile = relativePath.TrimStart('/');
            string safePath = Path.Combine(_distFolder, rawFile);

            if (!File.Exists(safePath))
            {
                // Serve SPA / fallback index.html index
                safePath = Path.Combine(_distFolder, "index.html");
                if (!File.Exists(safePath))
                {
                    await WriteErrorResponse(resp, HttpStatusCode.NotFound, "Vite layout is missing - build frontend sources first");
                    return;
                }
            }

            string ext = Path.GetExtension(safePath).ToLower();
            resp.ContentType = ext switch
            {
                ".html" or ".htm" => "text/html",
                ".css" => "text/css",
                ".js" or ".mjs" => "application/javascript",
                ".json" => "application/json",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".mp3" => "audio/mpeg",
                _ => "application/octet-stream"
            };

            resp.ContentLength64 = new FileInfo(safePath).Length;
            using var fileIn = File.OpenRead(safePath);
            await fileIn.CopyToAsync(resp.OutputStream);
            resp.OutputStream.Close();
        }

        private async Task<string> ReadBodyAsync(HttpListenerRequest req)
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            return await reader.ReadToEndAsync();
        }

        private async Task WriteJsonResponse(HttpListenerResponse resp, object payload)
        {
            resp.ContentType = "application/json";
            string str = JsonSerializer.Serialize(payload);
            byte[] buffer = Encoding.UTF8.GetBytes(str);
            resp.ContentLength64 = buffer.Length;
            await resp.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            resp.OutputStream.Close();
        }

        private async Task WriteJsonResponse(HttpListenerResponse resp, JsonElement payload)
        {
            resp.ContentType = "application/json";
            string str = payload.GetRawText();
            byte[] buffer = Encoding.UTF8.GetBytes(str);
            resp.ContentLength64 = buffer.Length;
            await resp.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            resp.OutputStream.Close();
        }

        private async Task WriteHtmlResponse(HttpListenerResponse resp, string html)
        {
            resp.ContentType = "text/html; charset=utf-8";
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            resp.ContentLength64 = buffer.Length;
            await resp.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            resp.OutputStream.Close();
        }

        private async Task WriteErrorResponse(HttpListenerResponse resp, HttpStatusCode status, string msg)
        {
            resp.StatusCode = (int)status;
            resp.ContentType = "application/json";
            string str = JsonSerializer.Serialize(new { error = msg });
            byte[] buffer = Encoding.UTF8.GetBytes(str);
            resp.ContentLength64 = buffer.Length;
            await resp.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            resp.OutputStream.Close();
        }

        private string GetOAuthHTMLContent()
        {
            // Fully compliant inlined Google login success/error page matching server.ts exactly
            return @"<!DOCTYPE html>
<html>
<head>
  <meta charset=""utf-8"">
  <title>Interstitial-er Sign-In Success</title>
  <style>
    body {
      background-color: #0f172a;
      color: #cbd5e1;
      font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif;
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
      margin: 0;
      padding: 16px;
    }
    .card {
      background-color: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 32px;
      max-width: 480px;
      width: 100%;
      box-shadow: 0 4px 6px -1px rgb(0 0 0 / 0.1);
      text-align: center;
    }
    .title {
      color: #f1f5f9;
      font-size: 20px;
      font-weight: 700;
      margin-top: 0;
      margin-bottom: 12px;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }
    .status-text {
      font-weight: 600;
      font-size: 14px;
      margin-top: 16px;
      margin-bottom: 8px;
    }
    .status-success { color: #34d399; }
    .status-error { color: #f87171; }
    .status-pending { color: #60a5fa; }
    .desc {
      font-size: 13px;
      color: #94a3b8;
      line-height: 1.6;
      margin-bottom: 24px;
    }
    .token-container {
      margin-top: 20px;
      text-align: left;
    }
    .token-label {
      font-size: 9px;
      font-weight: bold;
      color: #94a3b8;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      margin-bottom: 6px;
      display: block;
    }
    .token-box {
      width: 100%;
      height: 60px;
      background-color: #0f172a;
      border: 1px solid #334155;
      border-radius: 4px;
      color: #38bdf8;
      font-family: monospace;
      font-size: 11px;
      padding: 8px;
      box-sizing: border-box;
      resize: none;
      word-break: break-all;
    }
    .btn-copy {
      background-color: #3b82f6;
      border: none;
      color: white;
      padding: 6px 12px;
      font-size: 11px;
      border-radius: 4px;
      cursor: pointer;
      margin-top: 8px;
      font-weight: bold;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }
    .brand {
      font-size: 11px;
      color: #64748b;
      font-weight: bold;
      text-transform: uppercase;
      letter-spacing: 0.15em;
      margin-top: 24px;
      border-top: 1px solid #334155;
      padding-top: 16px;
    }
    .loader {
      display: inline-block;
      width: 24px;
      height: 24px;
      border: 3px solid #334155;
      border-top-color: #60a5fa;
      border-radius: 50%;
      animation: spin 1s ease-in-out infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
  </style>
</head>
<body>
  <div class=""card"">
    <div class=""title"">Interstitial-er OAuth</div>
    <div id=""loader-container"" style=""margin: 16px 0;"">
      <div id=""loader"" class=""loader""></div>
    </div>
    <div id=""status"" class=""status-text status-pending"">Exchanging Token...</div>
    <div id=""message"" class=""desc"">Please wait while the application registers your Google Drive access session credentials.</div>
    
    <div id=""token-section"" class=""token-container"" style=""display: none;"">
      <span class=""token-label"">Access Token (Option 2 Manual Copy-Paste)</span>
      <textarea id=""token-textarea"" class=""token-box"" readonly onclick=""this.select()""></textarea>
      <button id=""btn-copy"" class=""btn-copy"">Copy to Clipboard</button>
    </div>

    <div class=""brand"">Interstitial-er</div>
  </div>

  <script>
    const hash = window.location.hash;
    const params = new URLSearchParams(hash.substring(1));
    const accessToken = params.get('access_token');
    const state = params.get('state');
    
    if (accessToken) {
      if (state === 'manual') {
        document.getElementById('loader').style.display = 'none';
        const st = document.getElementById('status');
        st.innerText = 'MANUAL TOKEN GENERATED';
        st.className = 'status-text status-success';
        document.getElementById('message').innerText = 'Please copy the secure access token below and paste it into the Interstitial-er Option: Copy-Paste input field.';
        document.getElementById('token-section').style.display = 'block';
        document.getElementById('token-textarea').value = accessToken;
      } else {
        fetch('/api/register-token', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ token: accessToken })
        })
        .then(res => res.json())
        .then(data => {
          document.getElementById('loader').style.display = 'none';
          const st = document.getElementById('status');
          st.innerText = 'AUTHENTICATION COMPLETED';
          st.className = 'status-text status-success';
          document.getElementById('message').innerHTML = 'Your credentials have been verified and applied.<br>This window will close automatically.';
          setTimeout(() => { window.close(); }, 1200);
        })
        .catch(err => {
          document.getElementById('loader').style.display = 'none';
          const st = document.getElementById('status');
          st.innerText = 'AUTOMATION REGISTRATION FAILED';
          st.className = 'status-text status-error';
          document.getElementById('message').innerText = 'Failed to transmit token. Copy manual token instead:';
          document.getElementById('token-section').style.display = 'block';
          document.getElementById('token-textarea').value = accessToken;
        });
      }

      document.getElementById('btn-copy').addEventListener('click', () => {
        const textarea = document.getElementById('token-textarea');
        textarea.select();
        navigator.clipboard.writeText(accessToken).then(() => {
          const btn = document.getElementById('btn-copy');
          btn.innerText = 'Copied!';
          setTimeout(() => { btn.innerText = 'Copy to Clipboard'; }, 2000);
        });
      });
    } else {
      document.getElementById('loader').style.display = 'none';
      const st = document.getElementById('status');
      st.innerText = 'NO ACCESS TOKEN DETECTED';
      st.className = 'status-text status-error';
    }
  </script>
</body>
</html>";
        }
    }
}
