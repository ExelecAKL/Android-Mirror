using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AndroidMirror
{
    public sealed partial class Shell
    {
        string engineVersion = "5.0.1", downloadedEngine = "", notifiedUpdate = "";
        EngineRelease availableEngine;
        bool updateCheckBusy, updateDownloadBusy, updatingEngine, customEngine, engineVersionKnown, enginePromptBusy;
        DateTime nextDownloadAttempt, nextInstallAttempt;
        readonly CancellationTokenSource updatesLifetime = new CancellationTokenSource();
        void InitializeUpdates()
        {
            Control<ComboBox>("EngineUpdatePolicy").SelectedIndex = preferences.EngineUpdatePolicy == "off" ? 0 : preferences.EngineUpdatePolicy == "check" ? 1 : 2;
            Control<ComboBox>("EngineUpdatePolicy").SelectionChanged += delegate {
                if (updating) return;
                preferences.EngineUpdatePolicy = new[] { "off", "check", "automatic" }[Math.Max(0, Control<ComboBox>("EngineUpdatePolicy").SelectedIndex)]; SaveSettings();
            };
            Control<Button>("CheckEngineUpdates").Click += async delegate { await CheckEngineUpdates(true); };
            Control<Button>("InstallEngineUpdate").Click += async delegate { await PrepareAndInstallUpdate(true); };
            Control<Button>("ResetAppDefaults").Click += delegate { ResetAppDefaults(); };
            Control<Button>("ResetDeviceDefaults").Click += delegate {
                if (String.IsNullOrEmpty(settingsSerial)) return;
                controller.ApplyOptions(settingsSerial, new MirrorOptions()); SaveSettings(); RenderSettings();
            };
            if (!preview && !selfCheck) ReadEngineVersion();
        }
        async void ReadEngineVersion()
        {
            try {
                var reply = await new AdbCommands(engineDirectory, EngineLayout.RuntimeExecutable).Run("--version", null, 6000, wirelessLifetime.Token);
                var match = Regex.Match(reply.Text, @"^scrcpy\s+([0-9]+\.[0-9]+(?:\.[0-9]+)?)");
                if (match.Success) engineVersion = match.Groups[1].Value;
                try { await Task.Run(() => EngineUpdateFiles.VerifyUnmodified(engineDirectory)); } catch (Exception) { customEngine = true; }
                engineVersionKnown = match.Success;
                if (!closed) Control<TextBlock>("EngineUpdateStatus").Text = "Installed engine: " + engineVersion;
            } catch (OperationCanceledException) { } catch (Exception error) { if (!closed) Control<TextBlock>("EngineUpdateStatus").Text = error.Message; }
        }
        async Task CheckEngineUpdates(bool requested)
        {
            if (preview || selfCheck || updateCheckBusy || updatingEngine) return;
            updateCheckBusy = true; Control<Button>("CheckEngineUpdates").IsEnabled = false;
            Control<TextBlock>("EngineUpdateStatus").Text = "Checking official releases…";
            try {
                EngineRelease release;
                using (var client = new EngineReleaseClient()) release = await client.Get(null, updatesLifetime.Token);
                if (availableEngine == null || availableEngine.Tag != release.Tag) { downloadedEngine = ""; nextDownloadAttempt = DateTime.MinValue; }
                availableEngine = release;
                if (closed) return;
                preferences.LastEngineUpdateCheck = DateTime.UtcNow; SaveSettings();
                bool newer = availableEngine.NewerThan(engineVersion);
                Control<TextBlock>("EngineUpdateStatus").Text = newer ? "Engine " + availableEngine.Version + " available · installed " + engineVersion : "Engine " + engineVersion + " is up to date.";
                Control<Button>("InstallEngineUpdate").Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
                Control<Button>("InstallEngineUpdate").IsEnabled = !customEngine;
                if (customEngine) Control<TextBlock>("EngineUpdateStatus").Text += " Local engine changes detected; installation is paused to preserve them.";
                Control<TextBox>("EngineReleaseNotes").Text = availableEngine.Notes;
                if (newer && tray != null && notifiedUpdate != availableEngine.Tag) { notifiedUpdate = availableEngine.Tag; tray.NotifyUpdate(availableEngine.Version); }
                if (newer && requested && !customEngine) await PromptEngineUpdate();
            } catch (OperationCanceledException) { if (!closed && requested) Control<TextBlock>("EngineUpdateStatus").Text = "Update check timed out. Your installed engine was kept."; }
            catch (Exception error) { if (!closed) Control<TextBlock>("EngineUpdateStatus").Text = "Couldn’t check online: " + error.Message; }
            finally { updateCheckBusy = false; if (!closed) Control<Button>("CheckEngineUpdates").IsEnabled = true; }
        }
        async Task DownloadEngineUpdate(bool requested = false)
        {
            if (availableEngine == null || updateDownloadBusy || preview || selfCheck || customEngine || (!requested && DateTime.UtcNow < nextDownloadAttempt)) return;
            var release = availableEngine; nextDownloadAttempt = DateTime.UtcNow.AddMinutes(30);
            updateDownloadBusy = true; Control<Button>("InstallEngineUpdate").IsEnabled = false;
            try {
                string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android Mirror", "updates");
                string path = Path.Combine(cache, "engine-" + release.Tag + ".zip");
                if (!File.Exists(path) || EngineReleaseClient.Hash(path) != release.Sha256) {
                    using (var client = new EngineReleaseClient()) await client.Download(release, path, progress => OnUI(() => Control<TextBlock>("EngineUpdateStatus").Text = "Downloading engine " + release.Version + "… " + progress + "%"), updatesLifetime.Token);
                }
                if (closed || availableEngine.Tag != release.Tag) return;
                downloadedEngine = path; Control<TextBlock>("EngineUpdateStatus").Text = "Engine " + release.Version + " is verified and ready. Installation waits until mirroring is idle.";
                Control<Button>("InstallEngineUpdate").Content = "Install verified update";
            } catch (OperationCanceledException) { } catch (Exception error) { if (!closed) Control<TextBlock>("EngineUpdateStatus").Text = error.Message; }
            finally { updateDownloadBusy = false; if (!closed) Control<Button>("InstallEngineUpdate").IsEnabled = true; }
        }
        async Task PrepareAndInstallUpdate(bool requested = false)
        {
            if (updatingEngine || appInstallBusy || preview || selfCheck || availableEngine == null || customEngine || (!requested && DateTime.UtcNow < nextInstallAttempt)) return;
            if (preferences.ApprovedEngineVersion != availableEngine.Tag) { await PromptEngineUpdate(); return; }
            if (downloadedEngine.Length == 0) await DownloadEngineUpdate(requested);
            if (downloadedEngine.Length == 0 || closed) return;
            if (controller.ActiveCount > 0 || otgSession != null) { Control<TextBlock>("EngineUpdateStatus").Text = "Update ready. Stop the active mirrors before installing."; return; }
            var wanted = controller.Slots.Where(s => s.Controller.Wanted).Select(s => s.Profile.Serial).ToArray();
            var release = availableEngine; nextInstallAttempt = DateTime.UtcNow.AddHours(24);
            bool paused = false;
            try {
                string helper = Path.Combine(directory, "tools", "Engine Updater.exe");
                if (!File.Exists(helper)) throw new IOException("The update helper is missing. Reinstall Android Mirror.");
                EngineUpdateFiles.VerifyUnmodified(engineDirectory);
                updatingEngine = true; paused = true; Control<Grid>("AppContent").IsEnabled = false; timer.Stop(); controller.StopAll();
                if (tracker != null) { tracker.Dispose(); tracker = null; }
                wirelessLifetime.Cancel(); CancelPairing(); await Task.Delay(700);
                var info = new ProcessStartInfo(helper, "--tag=" + release.Tag + " --package=" + MirrorProcess.Quote(downloadedEngine) + " --parent=" + Process.GetCurrentProcess().Id) { UseShellExecute = true, WorkingDirectory = directory };
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd('\\') + "\\";
                if (Path.GetFullPath(directory).StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)) info.Verb = "runas";
                using (var process = Process.Start(info)) {
                    await Task.Run(() => process.WaitForExit());
                    if (process.ExitCode != 0) throw new IOException("The update was cancelled or could not be installed. The previous engine remains available.");
                }
                engineVersion = release.Version; downloadedEngine = "";
                var help = await new AdbCommands(engineDirectory, EngineLayout.RuntimeExecutable).Run("--help", null, 6000, updatesLifetime.Token);
                engineCatalog = EngineCatalog.Parse(help.Text);
                var supported = engineCatalog.Features.Select(f => f.Name).ToArray();
                foreach (var options in preferences.Profiles.Select(p => p.Options).Concat(new[] { preferences.DefaultOptions })) options.Advanced.RemoveAll(s => !supported.Contains(s.Name));
                SaveSettings();
                Control<Button>("InstallEngineUpdate").Visibility = Visibility.Collapsed;
                Control<TextBlock>("EngineUpdateStatus").Text = "Engine " + engineVersion + " installed. Saved settings were kept.";
            } catch (Exception error) { if (!closed) { Control<TextBlock>("EngineUpdateStatus").Text = error.Message; preferences.ApprovedEngineVersion = ""; preferences.DeferredEngineVersion = release.Tag; preferences.DeferEngineUpdateUntil = DateTime.UtcNow.AddDays(1); SaveSettings(); } }
            finally {
                updatingEngine = false;
                if (!closed && paused) { wirelessLifetime.Dispose(); wirelessLifetime = new CancellationTokenSource(); Control<Grid>("AppContent").IsEnabled = true; StartTracker(); timer.Start(); foreach (string serial in wanted) controller.Connect(serial); }
            }
        }
        async Task PromptEngineUpdate()
        {
            if (closed || preview || selfCheck || customEngine || availableEngine == null || !availableEngine.NewerThan(engineVersion) || enginePromptBusy || appPromptBusy || dialogResult != null || appInstallBusy || updatingEngine || !window.IsVisible) return;
            enginePromptBusy = true; var release = availableEngine;
            try
            {
                if (!await ConfirmDialog("Mirroring engine " + release.Version + " is available", "This updates the engine that mirrors your devices. Your Android Mirror app and saved settings are kept. Installation waits until active mirrors stop. Windows may ask for administrator approval.", "Install update", "Remind me later", "Engine based update", release.Notes))
                {
                    preferences.ApprovedEngineVersion = "";
                    preferences.DeferredEngineVersion = release.Tag; preferences.DeferEngineUpdateUntil = DateTime.UtcNow.AddDays(1); SaveSettings(); return;
                }
                preferences.ApprovedEngineVersion = release.Tag; SaveSettings();
                await DownloadEngineUpdate(true);
                if (!closed && downloadedEngine.Length > 0 && controller.ActiveCount == 0 && otgSession == null) await PrepareAndInstallUpdate(true);
            }
            catch (Exception error) { if (!closed) Control<TextBlock>("EngineUpdateStatus").Text = "Couldn’t prepare the engine update: " + error.Message; }
            finally { enginePromptBusy = false; }
        }
        async void TickEngineUpdates()
        {
            bool approved = availableEngine != null && availableEngine.NewerThan(engineVersion) && preferences.ApprovedEngineVersion == availableEngine.Tag;
            if (preview || selfCheck || closed || updatingEngine || appInstallBusy || !engineVersionKnown || (preferences.EngineUpdatePolicy == "off" && !approved)) return;
            if (preferences.EngineUpdatePolicy != "off" && !updateCheckBusy && DateTime.UtcNow - preferences.LastEngineUpdateCheck > TimeSpan.FromDays(1)) {
                preferences.LastEngineUpdateCheck = DateTime.UtcNow;
                await CheckEngineUpdates(false);
            }
            if (!closed && availableEngine != null && availableEngine.NewerThan(engineVersion) && !customEngine && preferences.ApprovedEngineVersion != availableEngine.Tag && window.IsVisible && dialogResult == null && !enginePromptBusy && !appPromptBusy && !(preferences.DeferredEngineVersion == availableEngine.Tag && DateTime.UtcNow < preferences.DeferEngineUpdateUntil)) await PromptEngineUpdate();
            if (closed || customEngine || availableEngine == null || !availableEngine.NewerThan(engineVersion) || controller.ActiveCount > 0 || otgSession != null || updateDownloadBusy || enginePromptBusy || appPromptBusy || dialogResult != null) return;
            if (preferences.ApprovedEngineVersion == availableEngine.Tag) { await PrepareAndInstallUpdate(); return; }
            if (preferences.EngineUpdatePolicy != "automatic") return;
            if (downloadedEngine.Length == 0) await DownloadEngineUpdate();
        }
        async void ResetAppDefaults()
        {
            if (preview || selfCheck) return;
            if (!await ConfirmDialog("Restore default settings?", "This resets app and device options, returns to USB only, and disables Start with Windows. Your device pairing keys are kept.", "Restore defaults")) return;
            preferences.DefaultOptions = new MirrorOptions(); preferences.EngineUpdatePolicy = "automatic";
            ChangeBackgroundSharing(true);
            preferences.CheckAppUpdates = true; preferences.DeferredAppVersion = ""; preferences.DeferAppUpdateUntil = DateTime.MinValue;
            preferences.ApprovedEngineVersion = ""; preferences.DeferredEngineVersion = ""; preferences.DeferEngineUpdateUntil = DateTime.MinValue;
            updating = true; Control<CheckBox>("AppUpdatesToggle").IsChecked = true; updating = false;
            foreach (var slot in controller.Slots.ToArray()) controller.ApplyOptions(slot.Profile.Serial, new MirrorOptions());
            controller.SetConnectionMode("usb"); controller.SetAutoReconnect(true); themeMode = "system";
            if (tray != null) tray.StartupEnabled = false;
            updating = true; Control<ComboBox>("EngineUpdatePolicy").SelectedIndex = 2; updating = false;
            RefreshAppearance(); SaveSettings();
        }
    }
}
