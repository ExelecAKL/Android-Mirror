using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AndroidMirror
{
    public sealed partial class Shell
    {
        string managedDevicesSignature = "";
        void RenderManagedDevices()
        {
            var devices = controller.Slots.OrderBy(s => s.Profile.Name).ToArray();
            string signature = String.Join("|", devices.Select(s => s.Profile.Serial + ":" + s.Profile.Name + ":" + s.Connected));
            var rows = Control<StackPanel>("ManagedDeviceRows");
            if (signature == managedDevicesSignature && rows.Children.Count > 0) return;
            managedDevicesSignature = signature; rows.Children.Clear();
            Control<Expander>("ManageDevices").Header = "Manage devices" + (devices.Length == 0 ? "" : " (" + devices.Length + ")");
            if (devices.Length == 0) { rows.Children.Add(Text("No saved devices.", 13, true)); return; }
            foreach (var device in devices) {
                string serial = device.Profile.Serial, name = device.Profile.Name;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                var label = Text(device.Profile.ToString(), 13); label.FontWeight = FontWeights.SemiBold; copy.Children.Add(label);
                copy.Children.Add(Text(device.Connected ? "Connected" : "Saved device", 12, true));
                var remove = new Button { Content = "Remove", Padding = new Thickness(12, 6, 12, 6), MinHeight = 34, VerticalAlignment = VerticalAlignment.Center };
                Resource(remove, System.Windows.Controls.Control.BackgroundProperty, "Input"); Resource(remove, System.Windows.Controls.Control.ForegroundProperty, "Text"); Resource(remove, System.Windows.Controls.Control.BorderBrushProperty, "Stroke"); Grid.SetColumn(remove, 1);
                remove.Click += async delegate {
                    if (preview || selfCheck) return;
                    if (!await ConfirmDialog("Remove " + name + "?", "Its mirror will stop and its saved settings will be removed. Reconnect the device to add it again.\n\nTo revoke wireless pairing, also forget this PC in the device’s Wireless debugging settings.", "Remove device")) return;
                    controller.Remove(serial); SaveSettings();
                };
                row.Children.Add(copy); row.Children.Add(remove); rows.Children.Add(row);
            }
        }
    }
}
