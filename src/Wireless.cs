using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AndroidMirror
{
    public sealed class AdbReply
    {
        public int Code;
        public string Text = "";
    }
    public interface IAdbCommands
    {
        Task<AdbReply> Run(string arguments, string input, int timeout, CancellationToken token);
    }
    public sealed class AdbCommands : IAdbCommands
    {
        readonly string directory, executable;
        public AdbCommands(string directory, string executable = "adb.exe") { this.directory = directory; this.executable = executable; }
        public async Task<AdbReply> Run(string arguments, string input, int timeout, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var start = AdbTracker.ProcessInfo(executable, arguments, directory);
            start.RedirectStandardInput = input != null;
            using (var process = new Process { StartInfo = start })
            {
                process.Start();
                Task<string> output = process.StandardOutput.ReadToEndAsync(), errors = process.StandardError.ReadToEndAsync();
                if (input != null) { await process.StandardInput.WriteLineAsync(input).ConfigureAwait(false); process.StandardInput.Close(); }
                Task exit = Task.Run(() => process.WaitForExit());
                if (await Task.WhenAny(exit, Task.Delay(timeout, token)).ConfigureAwait(false) != exit)
                {
                    try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
                    await exit.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    throw new IOException("The device did not respond. Check its address, Wi-Fi connection, and Wireless debugging setting.");
                }
                await exit.ConfigureAwait(false); token.ThrowIfCancellationRequested();
                return new AdbReply { Code = process.ExitCode, Text = (await output.ConfigureAwait(false)) + (await errors.ConfigureAwait(false)) };
            }
        }
    }
    public sealed class MdnsService
    {
        public string Name, Kind, Endpoint;
        public static List<MdnsService> Parse(string text)
        {
            var result = new List<MdnsService>();
            foreach (string line in text.Split('\n'))
            {
                string[] parts = Regex.Split(line.Trim(), @"\s+");
                if (parts.Length < 3 || !parts[1].StartsWith("_adb-tls-", StringComparison.Ordinal)) continue;
                try { string endpoint = WirelessPairing.NormalizeEndpoint(parts[2]); result.Add(new MdnsService { Name = parts[0], Kind = parts[1].TrimEnd('.'), Endpoint = endpoint }); } catch (ArgumentException) { }
            }
            return result;
        }
    }
    public sealed class QrCredentials
    {
        public readonly string ServiceName, Password;
        public QrCredentials()
        {
            byte[] random = new byte[24]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
            ServiceName = "studio-androidmirror-" + BitConverter.ToString(random, 0, 8).Replace("-", "").ToLowerInvariant();
            Password = BitConverter.ToString(random, 8, 16).Replace("-", "").ToLowerInvariant();
        }
        public string Payload { get { return "WIFI:T:ADB;S:" + ServiceName + ";P:" + Password + ";;"; } }
    }
    public sealed class WirelessOutcome
    {
        public bool Connected;
        public string Endpoint = "", ServiceName = "", Message = "";
    }
    public sealed class WirelessPairing
    {
        readonly IAdbCommands adb;
        public WirelessPairing(IAdbCommands adb) { this.adb = adb; }
        public static string Endpoint(string host, string port)
        {
            IPAddress ip; int number;
            if (!IPAddress.TryParse(host.Trim().Trim('[', ']'), out ip) || !Int32.TryParse(port.Trim(), out number) || number < 1 || number > 65535)
                throw new ArgumentException("Enter a valid IP address and a port from 1 to 65535.");
            return (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "[" + ip + "]" : ip.ToString()) + ":" + number;
        }
        public static string NormalizeEndpoint(string endpoint)
        {
            int colon = endpoint.LastIndexOf(':');
            if (colon < 1) throw new ArgumentException("Enter the IP address and port shown on your device.");
            return Endpoint(endpoint.Substring(0, colon), endpoint.Substring(colon + 1));
        }
        public static string Host(string endpoint) { return endpoint.Substring(0, endpoint.LastIndexOf(':')).Trim('[', ']'); }
        public async Task<List<MdnsService>> Discover(CancellationToken token)
        {
            var reply = await adb.Run("mdns services", null, 6000, token).ConfigureAwait(false);
            if (reply.Code != 0) throw new IOException("Network discovery is unavailable. Use manual pairing instead.");
            return MdnsService.Parse(reply.Text);
        }
        public async Task<WirelessOutcome> PairCode(string endpoint, string code, CancellationToken token)
        {
            endpoint = NormalizeEndpoint(endpoint); code = code.Trim();
            if (!Regex.IsMatch(code, @"^[0-9]{6}$")) throw new ArgumentException("Enter the six-digit pairing code shown on your device.");
            return await Pair(endpoint, code, token).ConfigureAwait(false);
        }
        async Task<WirelessOutcome> Pair(string endpoint, string password, CancellationToken token)
        {
            // Keep the secret out of process arguments, diagnostics, and preferences.
            var reply = await adb.Run("pair " + MirrorProcess.Quote(endpoint), password, 30000, token).ConfigureAwait(false);
            if (reply.Code != 0 || reply.Text.IndexOf("Successfully paired", StringComparison.OrdinalIgnoreCase) < 0)
                throw new IOException("Pairing failed. Open a fresh pairing code on the device and try again.");
            for (int attempt = 0; attempt < 6; attempt++)
            {
                List<MdnsService> services;
                try { services = await Discover(token).ConfigureAwait(false); }
                catch (IOException) { break; }
                var matches = services.Where(s => s.Kind.StartsWith("_adb-tls-connect._tcp", StringComparison.Ordinal) && Host(s.Endpoint) == Host(endpoint)).ToArray();
                if (matches.Length == 1) return await Connect(matches[0].Endpoint, matches[0].Name, token).ConfigureAwait(false);
                if (attempt < 5) await Task.Delay(1000, token).ConfigureAwait(false);
            }
            return new WirelessOutcome { Message = "Paired. Enter the connection port from the main Wireless debugging screen, then choose Connect." };
        }
        public async Task<WirelessOutcome> PairQr(QrCredentials credentials, CancellationToken token)
        {
            DateTime expires = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < expires)
            {
                var services = await Discover(token).ConfigureAwait(false);
                var match = services.FirstOrDefault(s => s.Kind.StartsWith("_adb-tls-pairing._tcp", StringComparison.Ordinal) && s.Name == credentials.ServiceName);
                if (match != null) return await Pair(match.Endpoint, credentials.Password, token).ConfigureAwait(false);
                await Task.Delay(1500, token).ConfigureAwait(false);
            }
            throw new IOException("This QR code expired. Choose New QR code to try again.");
        }
        public async Task<WirelessOutcome> Connect(string endpoint, string serviceName, CancellationToken token)
        {
            endpoint = NormalizeEndpoint(endpoint);
            var reply = await adb.Run("connect " + MirrorProcess.Quote(endpoint), null, 10000, token).ConfigureAwait(false);
            bool connected = reply.Code == 0 && (reply.Text.StartsWith("connected to ", StringComparison.OrdinalIgnoreCase) || reply.Text.StartsWith("already connected to ", StringComparison.OrdinalIgnoreCase));
            if (!connected) throw new IOException("Couldn’t connect. Use the connection port from the main Wireless debugging screen; it differs from the pairing port.");
            return new WirelessOutcome { Connected = true, Endpoint = endpoint, ServiceName = serviceName, Message = "Connected over Wi-Fi. Your device will appear in the device list." };
        }
        public async Task<WirelessOutcome> Legacy(string serial, string host, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(serial)) throw new ArgumentException("Choose an authorized USB device first.");
            if (String.IsNullOrWhiteSpace(host)) {
                var address = await adb.Run("-s " + MirrorProcess.Quote(serial) + " shell ip -f inet addr show wlan0", null, 6000, token).ConfigureAwait(false);
                var match = Regex.Match(address.Text, @"\binet\s+([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)/");
                if (!match.Success) throw new IOException("The device Wi-Fi address could not be found. Enter its IP address manually above.");
                host = match.Groups[1].Value;
            }
            string endpoint = Endpoint(host, "5555");
            var reply = await adb.Run("-s " + MirrorProcess.Quote(serial) + " tcpip 5555", null, 6000, token).ConfigureAwait(false);
            if (reply.Code != 0) throw new IOException("The device did not enable legacy Wi-Fi. Confirm USB debugging and retry.");
            await Task.Delay(1000, token).ConfigureAwait(false);
            return await Connect(endpoint, "", token).ConfigureAwait(false);
        }
        public async Task<List<Device>> Identify(List<Device> devices, IDictionary<string, string> cache, CancellationToken token)
        {
            foreach (var device in devices.Where(d => d.IsWireless && d.State == "device"))
            {
                string identity;
                if (!cache.TryGetValue(device.Serial, out identity))
                {
                    try
                    {
                        var reply = await adb.Run("-s " + MirrorProcess.Quote(device.Serial) + " shell getprop ro.serialno", null, 4000, token).ConfigureAwait(false);
                        identity = reply.Code == 0 ? reply.Text.Trim() : "";
                        if (!Regex.IsMatch(identity, @"^[A-Za-z0-9._-]{1,128}$")) identity = "";
                        if (identity.Length > 0) cache[device.Serial] = identity;
                    }
                    catch (IOException) { identity = ""; }
                }
                device.PhysicalId = identity;
            }
            return devices;
        }
    }
}
