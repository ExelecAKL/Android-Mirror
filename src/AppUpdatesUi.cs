using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AndroidMirror
{
    public sealed partial class Shell
    {
        readonly string appVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        EngineRelease availableApp;
        bool appCheckBusy, appInstallBusy, appPromptBusy;
        string notifiedApp = "";
        void InitializeAppUpdates()
        {
            Control<CheckBox>("AppUpdatesToggle").IsChecked = preferences.CheckAppUpdates;
            Control<CheckBox>("AppUpdatesToggle").Checked += delegate { if (!updating) { preferences.CheckAppUpdates = true; SaveSettings(); } };
            Control<CheckBox>("AppUpdatesToggle").Unchecked += delegate { if (!updating) { preferences.CheckAppUpdates = false; SaveSettings(); } };
            Control<Button>("CheckAppUpdates").Click += async delegate { await CheckAppUpdates(true); };
            Control<Button>("InstallAppUpdate").Click += async delegate { await PromptAppUpdate(true); };
            Control<TextBlock>("AppUpdateStatus").Text = "Android Mirror " + appVersion;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--update-failed") >= 0) Control<TextBlock>("AppUpdateStatus").Text += " · The previous update did not finish. Open Settings to try again.";
        }
        async Task CheckAppUpdates(bool requested)
        {
            if (preview || selfCheck || closed || appCheckBusy || appInstallBusy) return;
            appCheckBusy = true; Control<Button>("CheckAppUpdates").IsEnabled = false;
            Control<TextBlock>("AppUpdateStatus").Text = "Checking Android Mirror releases…";
            try
            {
                using (var client = new AppReleaseClient()) availableApp = await client.Get(updatesLifetime.Token);
                if (closed) return;
                preferences.LastAppUpdateCheck = DateTime.UtcNow; SaveSettings();
                bool newer = availableApp.NewerThan(appVersion);
                Control<TextBlock>("AppUpdateStatus").Text = newer ? "Android Mirror " + availableApp.Version + " is available." : "Android Mirror " + appVersion + " is up to date.";
                Control<TextBox>("AppReleaseNotes").Text = availableApp.Notes;
                Control<Button>("InstallAppUpdate").Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
                Control<Button>("InstallAppUpdate").Content = AppReleaseClient.IsInstalled(directory) ? "Update Android Mirror" : "Download new version";
                if (newer && notifiedApp != availableApp.Tag)
                {
                    notifiedApp = availableApp.Tag;
                    if (!window.IsVisible && tray != null) tray.NotifyAppUpdate(availableApp.Version);
                }
                if (newer && requested) await PromptAppUpdate(true);
            }
            catch (OperationCanceledException) { if (!closed) Control<TextBlock>("AppUpdateStatus").Text = "The update check was cancelled or timed out."; }
            catch (Exception error) { if (!closed) Control<TextBlock>("AppUpdateStatus").Text = "Couldn’t check Android Mirror updates: " + error.Message; }
            finally { appCheckBusy = false; if (!closed) Control<Button>("CheckAppUpdates").IsEnabled = true; }
        }
        async void TickAppUpdates()
        {
            if (preview || selfCheck || closed || updatingEngine || enginePromptBusy || appInstallBusy || !preferences.CheckAppUpdates) return;
            if (!appCheckBusy && DateTime.UtcNow - preferences.LastAppUpdateCheck > TimeSpan.FromDays(1))
            {
                preferences.LastAppUpdateCheck = DateTime.UtcNow; SaveSettings();
                await CheckAppUpdates(false);
            }
            if (closed || availableApp == null || !availableApp.NewerThan(appVersion) || !window.IsVisible || dialogResult != null || appPromptBusy || (preferences.DeferredAppVersion == availableApp.Tag && DateTime.UtcNow < preferences.DeferAppUpdateUntil)) return;
            await PromptAppUpdate(false);
        }
        async Task PromptAppUpdate(bool requested)
        {
            if (availableApp == null || !availableApp.NewerThan(appVersion) || appPromptBusy || enginePromptBusy || updatingEngine || appInstallBusy || closed || dialogResult != null) return;
            var release = availableApp; bool installed = AppReleaseClient.IsInstalled(directory);
            appPromptBusy = true;
            try
            {
                string details = installed ? "Install the latest Android Mirror update and restart? Active mirrors will close and reconnect according to your settings. Your preferences and current engine will be kept.\n\nWindows will ask for administrator approval." : "A new Android Mirror version is ready. Open its download page to update this portable copy, or install the new version in Program Files. Your saved preferences are kept.";
                if (!await ConfirmDialog("Android Mirror " + release.Version + " is available", details, installed ? "Install update" : "Open downloads", "Remind me later", "App update", release.Notes))
                {
                    preferences.DeferredAppVersion = release.Tag; preferences.DeferAppUpdateUntil = DateTime.UtcNow.AddDays(1); SaveSettings(); return;
                }
                if (installed) await InstallAppUpdate(release);
                else { Process.Start(new ProcessStartInfo(AppReleaseClient.Repository + "/releases/tag/" + release.Tag) { UseShellExecute = true }); preferences.DeferredAppVersion = release.Tag; preferences.DeferAppUpdateUntil = DateTime.UtcNow.AddDays(1); SaveSettings(); }
            }
            catch (Exception error) { if (!closed) Control<TextBlock>("AppUpdateStatus").Text = "Couldn’t open the app update: " + error.Message; }
            finally { appPromptBusy = false; }
        }
        async Task InstallAppUpdate(EngineRelease release)
        {
            if (updatingEngine || appInstallBusy) return;
            appInstallBusy = true; Control<Button>("InstallAppUpdate").IsEnabled = false;
            try
            {
                string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android Mirror", "updates");
                string path = Path.Combine(cache, "Android-Mirror-" + release.Version + "-Setup.exe");
                if (!File.Exists(path) || EngineReleaseClient.Hash(path) != release.Sha256)
                    using (var client = new EngineReleaseClient()) await client.Download(release, path, progress => OnUI(() => Control<TextBlock>("AppUpdateStatus").Text = "Downloading Android Mirror " + release.Version + "… " + progress + "%"), updatesLifetime.Token);
                if (closed) return;
                if (new FileInfo(path).Length != release.Size || EngineReleaseClient.Hash(path) != release.Sha256) throw new IOException("The installer checksum did not match. Your installed app was kept.");
                Control<TextBlock>("AppUpdateStatus").Text = "Starting the verified update…";
                string helper = Path.Combine(directory, "tools", "App Updater.exe");
                if (!File.Exists(helper)) throw new IOException("The app update helper is missing. Download the latest installer from GitHub.");
                preferences.DeferredAppVersion = release.Tag; preferences.DeferAppUpdateUntil = DateTime.UtcNow.AddDays(1); SaveSettings();
                using (var process = Process.Start(new ProcessStartInfo(helper, release.Tag) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory }))
                {
                    await Task.Run(() => process.WaitForExit());
                    if (!closed) Control<TextBlock>("AppUpdateStatus").Text = process.ExitCode == 1223 ? "Update cancelled. Android Mirror is still running." : process.ExitCode == 0 ? "Android Mirror was updated." : "The update could not finish. Your installed app was kept; try again.";
                }
            }
            catch (System.ComponentModel.Win32Exception error) { Control<TextBlock>("AppUpdateStatus").Text = error.NativeErrorCode == 1223 ? "Update cancelled. Android Mirror is still running." : error.Message; }
            catch (OperationCanceledException) { if (!closed) Control<TextBlock>("AppUpdateStatus").Text = "Update download cancelled. Your installed app was kept."; }
            catch (Exception error) { if (!closed) Control<TextBlock>("AppUpdateStatus").Text = "Couldn’t update Android Mirror: " + error.Message; }
            finally { appInstallBusy = false; if (!closed) Control<Button>("InstallAppUpdate").IsEnabled = true; }
        }
    }
}
