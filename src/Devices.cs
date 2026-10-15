using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace AndroidMirror
{
    public sealed class MirrorOptions
    {
        public bool AudioEnabled = true;
        public bool ControlEnabled = true;
        public List<EngineSetting> Advanced = new List<EngineSetting>();
        public List<EnvironmentSetting> Environment = new List<EnvironmentSetting>();
        public bool Has(string name) { return Advanced.Any(s => s.Enabled && s.Name == name); }
        public MirrorOptions Copy() { return new MirrorOptions { AudioEnabled = AudioEnabled, ControlEnabled = ControlEnabled, Advanced = Advanced.Select(s => s.Copy()).ToList(), Environment = Environment.Select(s => new EnvironmentSetting { Name = s.Name, Value = s.Value }).ToList() }; }
    }
    public sealed class DeviceProfile
    {
        public string Serial = "", Name = "";
        public bool Selected;
        public MirrorOptions Options = new MirrorOptions();
        [XmlIgnore] public string TransportSerial = "";
        public override string ToString() { return (String.IsNullOrEmpty(Name) ? Serial : Name) + (Serial.Length > 4 ? " · " + Serial.Substring(Serial.Length - 4) : ""); }
    }
    public sealed class AppPreferences
    {
        public bool AutoReconnect = true, SelectionInitialized;
        public string Theme = "system";
        public string ConnectionMode = "usb";
        public MirrorOptions DefaultOptions = new MirrorOptions();
        public string EngineUpdatePolicy = "automatic";
        public DateTime LastEngineUpdateCheck = DateTime.MinValue;
        public List<WirelessPeer> WirelessPeers = new List<WirelessPeer>();
        public List<DeviceProfile> Profiles = new List<DeviceProfile>();
        public static AppPreferences Load(string xmlFile, string legacyFile)
        {
            if (File.Exists(xmlFile))
            {
                try
                {
                    using (var input = File.OpenRead(xmlFile))
                    {
                        var result = (AppPreferences)new XmlSerializer(typeof(AppPreferences)).Deserialize(input);
                        result.Profiles = (result.Profiles ?? new List<DeviceProfile>()).Where(p => p != null && !String.IsNullOrWhiteSpace(p.Serial)).GroupBy(p => p.Serial).Select(g => g.First()).ToList();
                        if (result.DefaultOptions == null) result.DefaultOptions = new MirrorOptions();
                        foreach (var options in result.Profiles.Select(p => p.Options ?? (p.Options = new MirrorOptions())).Concat(new[] { result.DefaultOptions }))
                        { if (options.Advanced == null) options.Advanced = new List<EngineSetting>(); if (options.Environment == null) options.Environment = new List<EnvironmentSetting>(); }
                        if (result.WirelessPeers == null) result.WirelessPeers = new List<WirelessPeer>();
                        if (!new[] { "off", "check", "automatic" }.Contains(result.EngineUpdatePolicy)) result.EngineUpdatePolicy = "automatic";
                        if (!new[] { "usb", "wireless", "adaptive" }.Contains(result.ConnectionMode)) result.ConnectionMode = "usb";
                        return result;
                    }
                }
                catch { }
            }
            var preferences = new AppPreferences();
            try
            {
                string[] lines = File.ReadAllLines(legacyFile);
                if (lines.Length > 0) preferences.AutoReconnect = lines[0] != "false";
                if (lines.Length > 1 && !String.IsNullOrWhiteSpace(lines[1]))
                {
                    preferences.Profiles.Add(new DeviceProfile { Serial = lines[1], Name = lines[1], Selected = true });
                    preferences.SelectionInitialized = true;
                }
                if (lines.Length > 2) preferences.Theme = lines[2];
            }
            catch { }
            return preferences;
        }
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (var output = File.Create(temporary)) new XmlSerializer(typeof(AppPreferences)).Serialize(output, this);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
    public sealed class WirelessPeer
    {
        public string Endpoint = "", ServiceName = "", PhysicalId = "";
    }
    public sealed class DeviceSlot
    {
        public DeviceProfile Profile;
        public ConnectionController Controller;
        public Device Device;
        public bool Connected { get { return Device != null && (Device.State == "device" || (Profile.Options.Has("--otg") && !Device.IsWireless)); } }
        public string Status
        {
            get
            {
                if (Controller.Stopping) return "Reopening or stopping…";
                if (Controller.Active != null) return Controller.Active.Ready ? Profile.Options.Has("--otg") ? "USB input active" : Profile.Options.Has("--no-window") || Profile.Options.Has("--no-video") ? "Session running" : "Mirroring" : "Opening mirror…";
                if (!String.IsNullOrEmpty(Controller.Error)) return "Couldn’t start · check Connection help";
                if (!Profile.Options.Has("--otg") && Device != null && Device.State == "unauthorized") return "Allow USB debugging on this device";
                if (!Profile.Options.Has("--otg") && Device != null && Device.State != "device") return "Device offline";
                if (!Connected) return Profile.Selected && Controller.Wanted ? "Waiting to reconnect" : "Disconnected";
                return Profile.Selected ? "Ready to mirror" : "Select to mirror";
            }
        }
        public string Summary { get { return (Device == null ? "" : Device.IsWireless ? "Wi-Fi · " : "USB · ") + (Profile.Options.AudioEnabled ? "Audio on PC" : "Audio on device") + " · " + (Profile.Options.ControlEnabled ? "Mouse & keyboard on" : "View only"); } }
    }
    public sealed class MultiDeviceController : IDisposable
    {
        readonly Dictionary<string, DeviceSlot> slots = new Dictionary<string, DeviceSlot>();
        readonly HashSet<string> removedUntilDisconnect = new HashSet<string>();
        readonly Func<DeviceProfile, IMirrorSession> create;
        readonly Action<Action> dispatch;
        readonly Func<DateTime> now;
        bool batching, disposed;
        public event Action Changed;
        public readonly AppPreferences Preferences;
        public List<Device> Devices = new List<Device>();
        public List<Device> AllDevices = new List<Device>();
        public MultiDeviceController(AppPreferences preferences, Func<DeviceProfile, IMirrorSession> create, Action<Action> dispatch, Func<DateTime> now = null)
        {
            Preferences = preferences; this.create = create; this.dispatch = dispatch; this.now = now;
            foreach (var profile in preferences.Profiles) Add(profile);
        }
        DeviceSlot Add(DeviceProfile profile)
        {
            var slot = new DeviceSlot { Profile = profile };
            slot.Controller = new ConnectionController(serial => create(profile), dispatch, now) { Target = profile.Serial, AutoReconnect = Preferences.AutoReconnect, Wanted = profile.Selected && Preferences.AutoReconnect, AdditionalTransport = device => profile.Options.Has("--otg") && !device.IsWireless };
            slots.Add(profile.Serial, slot); slot.Controller.Changed += Notify;
            return slot;
        }
        public IEnumerable<DeviceSlot> Slots { get { return slots.Values; } }
        public IEnumerable<DeviceSlot> VisibleSlots { get { return Slots.Where(s => s.Device != null || s.Controller.Active != null).OrderByDescending(s => s.Device != null).ThenBy(s => s.Profile.Name); } }
        public int ActiveCount { get { return Slots.Count(s => s.Controller.Active != null); } }
        public bool Waiting { get { return Slots.Any(s => s.Profile.Selected && s.Controller.Wanted && !s.Connected); } }
        public bool CanConnect { get { return Slots.Any(s => s.Profile.Selected && s.Connected && !s.Controller.Stopping); } }
        public void UpdateDevices(List<Device> devices)
        {
            if (disposed) return;
            batching = true; AllDevices = devices;
            removedUntilDisconnect.RemoveWhere(key => !devices.Any(d => d.Key == key));
            devices = devices.Where(d => !removedUntilDisconnect.Contains(d.Key)).ToList();
            devices = devices.Where(d => (Preferences.ConnectionMode == "adaptive" || (Preferences.ConnectionMode == "wireless" ? d.IsWireless : !d.IsWireless)) && (!d.IsWireless || !OptionsFor(d.Key).Has("--otg"))).GroupBy(d => d.Key).Select(g => g.OrderByDescending(d => d.State == "device" || (OptionsFor(d.Key).Has("--otg") && !d.IsWireless)).ThenBy(d => d.IsWireless).First()).ToList();
            Devices = devices;
            foreach (var device in devices)
            {
                DeviceSlot slot;
                if (!slots.TryGetValue(device.Key, out slot))
                {
                    var profile = new DeviceProfile { Serial = device.Key, Name = device.Name, Options = Preferences.DefaultOptions.Copy() };
                    Preferences.Profiles.Add(profile); slot = Add(profile);
                }
                slot.Profile.Name = device.Name;
            }
            if (!Preferences.SelectionInitialized && devices.Count > 0)
            {
                Preferences.SelectionInitialized = true;
                if (devices.Count == 1) slots[devices[0].Key].Profile.Selected = true;
                if (devices.Count == 1 && Preferences.AutoReconnect) slots[devices[0].Key].Controller.Wanted = true;
            }
            foreach (var slot in Slots)
            {
                slot.Device = devices.FirstOrDefault(d => d.Key == slot.Profile.Serial);
                if (slot.Device != null)
                {
                    slot.Profile.TransportSerial = slot.Device.Serial;
                    slot.Controller.ChangeTransport(slot.Device.Serial);
                }
                slot.Controller.UpdateDevices(slot.Device == null ? new List<Device>() : new List<Device> { slot.Device });
            }
            batching = false; Notify();
        }
        public void SetConnectionMode(string mode)
        {
            if (!new[] { "usb", "wireless", "adaptive" }.Contains(mode)) throw new ArgumentException("Unknown connection mode.");
            Preferences.ConnectionMode = mode; UpdateDevices(AllDevices.ToList());
        }
        public void Select(string serial, bool selected)
        {
            var slot = slots[serial]; Preferences.SelectionInitialized = true; slot.Profile.Selected = selected;
            if (!selected) slot.Controller.Stop();
            else if (Preferences.AutoReconnect) slot.Controller.Connect();
            Notify();
        }
        public void SetOptions(string serial, bool audio, bool control)
        {
            var slot = slots[serial];
            if (slot.Profile.Options.AudioEnabled == audio && slot.Profile.Options.ControlEnabled == control) return;
            slot.Profile.Options.AudioEnabled = audio; slot.Profile.Options.ControlEnabled = control;
            if (slot.Controller.Wanted) slot.Controller.Restart();
            Notify();
        }
        public void Connect(string serial)
        {
            var slot = slots[serial]; slot.Profile.Selected = true; Preferences.SelectionInitialized = true;
            slot.Controller.Connect(); Notify();
        }
        public void ApplyOptions(string serial, MirrorOptions options)
        {
            var slot = slots[serial]; bool transportChanged = slot.Profile.Options.Has("--otg") != options.Has("--otg"); slot.Profile.Options = options.Copy();
            if (transportChanged) UpdateDevices(AllDevices.ToList());
            if (slot.Controller.Wanted) slot.Controller.Restart(); Notify();
        }
        MirrorOptions OptionsFor(string key) { DeviceSlot slot; return slots.TryGetValue(key, out slot) ? slot.Profile.Options : Preferences.DefaultOptions; }
        public void Remove(string serial)
        {
            DeviceSlot slot; if (!slots.TryGetValue(serial, out slot)) return;
            batching = true;
            if (AllDevices.Any(d => d.Key == serial)) removedUntilDisconnect.Add(serial);
            slot.Controller.Stop(); slot.Controller.Dispose(); slots.Remove(serial);
            Preferences.Profiles.Remove(slot.Profile);
            Preferences.WirelessPeers.RemoveAll(peer => peer.PhysicalId == serial || peer.Endpoint == slot.Profile.TransportSerial || (!String.IsNullOrEmpty(peer.ServiceName) && slot.Profile.TransportSerial.StartsWith(peer.ServiceName + ".", StringComparison.Ordinal)));
            Devices.RemoveAll(d => d.Key == serial);
            batching = false; Notify();
        }
        public void Stop(string serial) { slots[serial].Controller.Stop(); Notify(); }
        public void ConnectSelected() { batching = true; foreach (var slot in Slots.Where(s => s.Profile.Selected)) slot.Controller.Connect(); batching = false; Notify(); }
        public void StopAll() { batching = true; foreach (var slot in Slots) slot.Controller.Stop(); batching = false; Notify(); }
        public void SetAutoReconnect(bool enabled)
        {
            Preferences.AutoReconnect = enabled; batching = true;
            foreach (var slot in Slots) { slot.Controller.SetAutoReconnect(enabled && slot.Profile.Selected); }
            batching = false; Notify();
        }
        public void Tick() { batching = true; foreach (var slot in Slots) slot.Controller.Tick(); batching = false; Notify(); }
        void Notify() { if (!disposed && !batching && Changed != null) Changed(); }
        public void Dispose() { if (disposed) return; disposed = true; foreach (var slot in Slots) slot.Controller.Dispose(); }
    }
}
