using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AndroidMirror
{
    public sealed partial class Shell
    {
        readonly CancellationTokenSource wirelessLifetime = new CancellationTokenSource();
        readonly ConcurrentDictionary<string, string> wirelessIdentities = new ConcurrentDictionary<string, string>();
        readonly Dictionary<string, DateTime> reconnectAttempts = new Dictionary<string, DateTime>();
        WirelessPairing wireless;
        CancellationTokenSource pairingCancellation;
        bool wirelessOpen, reconnectBusy;
        DateTime nextWirelessCheck;
        int snapshotRevision;
        void InitializeWireless()
        {
            wireless = new WirelessPairing(new AdbCommands(engineDirectory));
            Control<ComboBox>("ConnectionModePicker").SelectionChanged += delegate {
                if (updating) return;
                string mode = new[] { "usb", "wireless", "adaptive" }[Math.Max(0, Control<ComboBox>("ConnectionModePicker").SelectedIndex)];
                if (mode == preferences.ConnectionMode) return;
                if (mode != "usb" && !preview && !selfCheck && MessageBox.Show(window,
                    "Wireless mirroring may add delay or stutter, depending on your Wi-Fi connection.\n\nUSB is recommended for the lowest latency.\n\nEnable wireless mirroring?",
                    "Android Mirror — Wireless connection", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No) != MessageBoxResult.Yes)
                { RenderSettings(); return; }
                if (mode == "usb") CancelPairing();
                controller.SetConnectionMode(mode); SaveSettings();
                if (!preview && !selfCheck) StartTracker();
            };
            Control<Button>("WirelessSetupButton").Click += delegate { OpenWireless(true); };
            Control<Button>("QrTabButton").Click += delegate { ShowPairingTab(true); };
            Control<Button>("CodeTabButton").Click += delegate { ShowPairingTab(false); };
            Control<Button>("NewQrButton").Click += delegate { BeginQrPairing(); };
            Control<Button>("PairCodeButton").Click += async delegate {
                string endpoint, code = Control<PasswordBox>("PairingCode").Password;
                try { endpoint = WirelessPairing.Endpoint(Control<TextBox>("WirelessIp").Text, Control<TextBox>("PairingPort").Text); }
                catch (ArgumentException error) { WirelessStatus(error.Message); return; }
                await PairingOperation(token => wireless.PairCode(endpoint, code, token), "Pairing with your device…");
                Control<PasswordBox>("PairingCode").Clear();
            };
            Control<Button>("ConnectWifiButton").Click += async delegate {
                string endpoint;
                try { endpoint = WirelessPairing.Endpoint(Control<TextBox>("WirelessIp").Text, Control<TextBox>("ConnectionPort").Text); }
                catch (ArgumentException error) { WirelessStatus(error.Message); return; }
                await PairingOperation(token => wireless.Connect(endpoint, "", token), "Connecting over Wi-Fi…");
            };
            Control<Button>("CancelPairButton").Click += delegate { CancelPairing(); WirelessStatus("Cancelled. Choose New QR code or try manual pairing."); };
        }
        void WirelessStatus(string text) { if (!closed) Control<TextBlock>("WirelessStatus").Text = text; }
        void OpenWireless(bool open)
        {
            if (open && preferences.ConnectionMode == "usb" && !preview && !selfCheck) return;
            wirelessOpen = open;
            Control<ScrollViewer>("HomeView").Visibility = Visibility.Collapsed;
            Control<ScrollViewer>("SettingsView").Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            Control<ScrollViewer>("WirelessView").Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            settingsOpen = true;
            if (open) ShowPairingTab(true); else { CancelPairing(); Control<PasswordBox>("PairingCode").Clear(); }
            var view = Control<ScrollViewer>(open ? "WirelessView" : "SettingsView");
            view.RenderTransform = new TranslateTransform(); AnimateIn(view, 200, 8);
        }
        void ShowPairingTab(bool qr)
        {
            CancelPairing();
            Control<StackPanel>("QrPairPanel").Visibility = qr ? Visibility.Visible : Visibility.Collapsed;
            Control<StackPanel>("ManualPairPanel").Visibility = qr ? Visibility.Collapsed : Visibility.Visible;
            Control<Button>("QrTabButton").Opacity = qr ? 1 : 0.65;
            Control<Button>("CodeTabButton").Opacity = qr ? 0.65 : 1;
            if (qr) BeginQrPairing(); else WirelessStatus("Use the IP address, pairing port, and six-digit code shown in Pair using pairing code.");
        }
        async void BeginQrPairing()
        {
            var credentials = new QrCredentials();
            Control<Image>("PairingQrImage").Source = QrImage.Create(credentials.Payload);
            if (preview || selfCheck) { WirelessStatus("Waiting for your device to scan the QR code…"); return; }
            await PairingOperation(token => wireless.PairQr(credentials, token), "Waiting for your device to scan the QR code…");
        }
        void CancelPairing()
        {
            if (pairingCancellation != null) { pairingCancellation.Cancel(); pairingCancellation = null; }
            Control<Button>("PairCodeButton").IsEnabled = !preview && !selfCheck;
            Control<Button>("ConnectWifiButton").IsEnabled = !preview && !selfCheck;
            Control<Button>("CancelPairButton").Visibility = Visibility.Collapsed;
        }
        async Task PairingOperation(Func<CancellationToken, Task<WirelessOutcome>> operation, string status)
        {
            CancelPairing();
            if (preview || selfCheck) return;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(wirelessLifetime.Token); pairingCancellation = cancellation;
            Control<Button>("PairCodeButton").IsEnabled = false; Control<Button>("ConnectWifiButton").IsEnabled = false;
            Control<Button>("CancelPairButton").Visibility = Visibility.Visible; WirelessStatus(status);
            try
            {
                var result = await operation(cancellation.Token);
                if (pairingCancellation != cancellation || closed) return;
                WirelessStatus(result.Message);
                if (result.Connected)
                {
                    var peer = preferences.WirelessPeers.FirstOrDefault(p => p.Endpoint == result.Endpoint);
                    if (peer == null) { peer = new WirelessPeer(); preferences.WirelessPeers.Add(peer); }
                    peer.Endpoint = result.Endpoint; peer.ServiceName = result.ServiceName; SaveSettings();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (pairingCancellation == cancellation && !closed) WirelessStatus(error.Message); }
            finally { if (pairingCancellation == cancellation) CancelPairing(); cancellation.Dispose(); }
        }
        async void MaybeReconnectWireless()
        {
            if (preview || selfCheck || closed || reconnectBusy || preferences.ConnectionMode == "usb" || !preferences.AutoReconnect || DateTime.UtcNow < nextWirelessCheck) return;
            var selected = preferences.Profiles.Where(p => p.Selected).Select(p => p.Serial).ToArray();
            if (selected.Length == 0) return;
            reconnectBusy = true; nextWirelessCheck = DateTime.UtcNow.AddSeconds(10);
            try
            {
                var peers = preferences.WirelessPeers.ToArray();
                var services = await wireless.Discover(wirelessLifetime.Token);
                if (closed || preferences.ConnectionMode == "usb" || !preferences.AutoReconnect) return;
                foreach (var service in services.Where(s => s.Kind.StartsWith("_adb-tls-connect._tcp", StringComparison.Ordinal)))
                {
                    if (closed || preferences.ConnectionMode == "usb" || !preferences.AutoReconnect) break;
                    bool known = selected.Any(id => ServiceNameMatches(service.Name, id)) || peers.Any(p => selected.Contains(p.PhysicalId) && p.ServiceName.Length > 0 && p.ServiceName == service.Name);
                    if (!known || controller.AllDevices.Any(d => d.State == "device" && (d.Serial == service.Endpoint || d.Serial.StartsWith(service.Name + ".", StringComparison.Ordinal)))) continue;
                    DateTime last; if (reconnectAttempts.TryGetValue(service.Name, out last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(30)) continue;
                    reconnectAttempts[service.Name] = DateTime.UtcNow;
                    try { await wireless.Connect(service.Endpoint, service.Name, wirelessLifetime.Token); } catch (System.IO.IOException) { }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* Discovery may be blocked; the manual path remains available. */ }
            finally { reconnectBusy = false; }
        }
        static bool ServiceNameMatches(string name, string identity) { return name.StartsWith("adb-" + identity + "-", StringComparison.Ordinal); }
    }
}
