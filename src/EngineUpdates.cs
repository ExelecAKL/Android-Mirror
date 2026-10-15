using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AndroidMirror
{
    public sealed class EngineRelease
    {
        public string Tag, Version, Url, Sha256, Notes;
        public long Size;
        public bool NewerThan(string current) { System.Version latest, installed; return System.Version.TryParse(Version, out latest) && System.Version.TryParse(current, out installed) && latest > installed; }
    }
    public sealed class EngineReleaseClient : IDisposable
    {
        const string Api = "https://api.github.com/repos/Genymobile/scrcpy/releases/";
        readonly HttpClient http;
        public EngineReleaseClient(HttpMessageHandler handler = null)
        {
            http = handler == null ? new HttpClient() : new HttpClient(handler);
            http.Timeout = TimeSpan.FromMinutes(5);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AndroidMirror/1.1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }
        public async Task<EngineRelease> Get(string tag, CancellationToken token)
        {
            if (tag != null && !Regex.IsMatch(tag, @"^v?[0-9]+\.[0-9]+(?:\.[0-9]+)?$")) throw new ArgumentException("Invalid engine version.");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using (var response = await http.GetAsync(Api + (tag == null ? "latest" : "tags/" + tag), timeout.Token).ConfigureAwait(false))
                { response.EnsureSuccessStatusCode(); return Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)); }
            }
        }
        public static EngineRelease Parse(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
            var data = serializer.Deserialize<Dictionary<string, object>>(json);
            string tag = Convert.ToString(data["tag_name"]);
            if (!Regex.IsMatch(tag, @"^v?[0-9]+\.[0-9]+(?:\.[0-9]+)?$")) throw new IOException("The release is not a supported stable version.");
            if ((data.ContainsKey("draft") && Convert.ToBoolean(data["draft"])) || (data.ContainsKey("prerelease") && Convert.ToBoolean(data["prerelease"]))) throw new IOException("Preview releases are not installed automatically.");
            string expected = "scrcpy-win64-" + tag + ".zip";
            var assets = data["assets"] as System.Collections.IEnumerable;
            if (assets == null) throw new IOException("The release asset list is missing.");
            foreach (Dictionary<string, object> asset in assets)
            {
                if (Convert.ToString(asset["name"]) != expected) continue;
                string url = Convert.ToString(asset["browser_download_url"]), digest = asset.ContainsKey("digest") ? Convert.ToString(asset["digest"]) : "";
                string prefix = "https://github.com/Genymobile/scrcpy/releases/download/" + tag + "/";
                if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || url.Substring(prefix.Length) != expected || !Regex.IsMatch(digest, @"^sha256:[a-fA-F0-9]{64}$")) throw new IOException("The official release does not provide a verifiable Windows package.");
                long size = Convert.ToInt64(asset["size"]);
                if (size < 1 || size > 512L * 1024 * 1024) throw new IOException("Unexpected engine package size.");
                return new EngineRelease { Tag = tag, Version = tag.TrimStart('v'), Url = url, Sha256 = digest.Substring(7).ToLowerInvariant(), Size = size, Notes = data.ContainsKey("body") ? Convert.ToString(data["body"]) : "" };
            }
            throw new IOException("The official release has no Windows x64 package.");
        }
        public async Task Download(EngineRelease release, string path, Action<int> progress, CancellationToken token)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); string partial = path + ".partial";
            try
            {
                using (var response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var destination = File.Create(partial))
                    {
                        byte[] buffer = new byte[81920]; int count; long total = 0;
                        while ((count = await source.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                        {
                            total += count; if (total > release.Size) throw new IOException("The download is larger than the official package.");
                            await destination.WriteAsync(buffer, 0, count, token).ConfigureAwait(false);
                            if (progress != null) progress((int)(100 * total / release.Size));
                        }
                        if (total != release.Size) throw new IOException("The engine download is incomplete.");
                    }
                }
                if (!String.Equals(Hash(partial), release.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("The engine checksum did not match. Your current engine was kept.");
                if (File.Exists(path)) File.Delete(path); File.Move(partial, path);
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
        public static string Hash(string path) { using (var file = File.OpenRead(path)) using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant(); }
        public void Dispose() { http.Dispose(); }
    }
    public sealed class EngineInventory
    {
        public string Version = "";
        public string Source = "upstream";
        public string OriginalExecutableSha256 = "", BrandIconSha256 = "";
        public List<string> Files = new List<string>();
        public Dictionary<string, string> Hashes = new Dictionary<string, string>();
    }
    public sealed class EngineUpdatePlan
    {
        public List<string> Added = new List<string>(), Changed = new List<string>(), Removed = new List<string>();
        public int Unchanged;
        public override string ToString() { return Added.Count + " added · " + Changed.Count + " changed · " + Removed.Count + " removed · " + Unchanged + " unchanged"; }
    }
    public static class EngineUpdateFiles
    {
        public const string Inventory = "engine-manifest.json";
        public static string SafePath(string root, string relative)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (String.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(":")) throw new IOException("Invalid engine package path.");
            string target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("An engine file leaves its folder.");
            string parent = Path.GetDirectoryName(target);
            while (parent != null && (parent + Path.DirectorySeparatorChar).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            { if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new IOException("Engine paths must not pass through filesystem links."); if (parent + Path.DirectorySeparatorChar == prefix) break; parent = Path.GetDirectoryName(parent); }
            if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("Engine files must not be filesystem links.");
            return target;
        }
        public static EngineInventory ReadInventory(string engine)
        {
            string path = SafePath(engine, Inventory);
            if (!File.Exists(path)) throw new IOException("The engine inventory is missing. Reinstall Android Mirror before updating.");
            var data = new JavaScriptSerializer().Deserialize<EngineInventory>(File.ReadAllText(path));
            if (data == null || data.Files == null || data.Files.Count > 4096) throw new IOException("Invalid engine inventory.");
            foreach (string file in data.Files) SafePath(engine, file); return data;
        }
        public static List<string> Extract(string archivePath, string stage, string tag)
        {
            Directory.CreateDirectory(stage); string expected = "scrcpy-win64-" + tag + "/";
            var files = new List<string>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0; int count = 0;
            try {
            using (var archive = ZipFile.OpenRead(archivePath))
            foreach (var entry in archive.Entries)
            {
                if (String.IsNullOrEmpty(entry.Name)) continue;
                string name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(expected, StringComparison.Ordinal)) throw new IOException("Unexpected engine archive layout.");
                string relative = name.Substring(expected.Length);
                if (relative == EngineLayout.UpstreamExecutable) relative = EngineLayout.RuntimeExecutable;
                string destination = SafePath(stage, relative);
                if (!names.Add(relative) || relative == Inventory || ++count > 4096) throw new IOException("Duplicate or invalid engine file.");
                total += entry.Length; if (total > 1024L * 1024 * 1024) throw new IOException("The extracted engine package is too large.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)); files.Add(relative); entry.ExtractToFile(destination);
            }
            foreach (string required in new[] { EngineLayout.RuntimeExecutable, "adb.exe", "scrcpy-server", "LICENSE.txt" }) if (!names.Contains(required)) throw new IOException("The engine package is incomplete: " + required);
            return files;
            } catch { RemoveStage(stage, files); throw; }
        }
        public static EngineUpdatePlan Plan(string engine, string stage, IEnumerable<string> newFiles)
        {
            VerifyUnmodified(engine);
            var result = new EngineUpdatePlan(); var old = ReadInventory(engine); var next = new HashSet<string>(newFiles, StringComparer.OrdinalIgnoreCase);
            foreach (string file in next)
            {
                string source = SafePath(stage, file), target = SafePath(engine, file);
                if (File.Exists(target) && !old.Files.Contains(file, StringComparer.OrdinalIgnoreCase)) throw new IOException("The update would replace an unrelated file: " + file);
                if (!File.Exists(target)) result.Added.Add(file);
                else if (EngineReleaseClient.Hash(source) == EngineReleaseClient.Hash(target)) result.Unchanged++;
                else {
                    if (!old.Files.Contains(file, StringComparer.OrdinalIgnoreCase)) throw new IOException("The update would replace an unrelated file: " + file);
                    result.Changed.Add(file);
                }
            }
            result.Removed.AddRange(old.Files.Where(f => f != Inventory && !next.Contains(f) && File.Exists(SafePath(engine, f))));
            return result;
        }
        public static void VerifyUnmodified(string engine)
        {
            var inventory = ReadInventory(engine);
            if ((inventory.Source != "upstream" && inventory.Source != "upstream-branded") || inventory.Hashes == null || inventory.Hashes.Count == 0) throw new IOException("This is a custom or unverified engine. Binary updates are paused to preserve its changes. Merge upstream source changes into the engine fork and rebuild instead.");
            foreach (string file in inventory.Files)
            {
                string expected, path = SafePath(engine, file);
                if (!inventory.Hashes.TryGetValue(file, out expected) || !File.Exists(path) || !String.Equals(EngineReleaseClient.Hash(path), expected, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Local engine changes were detected in " + file + ". Automatic installation is paused so those changes are not overwritten.");
            }
        }
        public static EngineUpdatePlan Apply(string packageRoot, string stage, IEnumerable<string> newFiles, string version, Action<string> progress, Action<string> beforeCopy = null, string originalExecutableHash = null, string brandIconHash = null)
        {
            string engine = SafePath(packageRoot, "engine"), backupRelative = "updates/backups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string backup = SafePath(packageRoot, backupRelative); var files = newFiles.ToList(); var plan = Plan(engine, stage, files);
            var formerEngineFiles = new HashSet<string>(ReadInventory(engine).Files.Concat(new[] { Inventory }), StringComparer.OrdinalIgnoreCase);
            var moved = new List<string>(); var written = new List<string>();
            var serializer = new JavaScriptSerializer();
            try
            {
                progress(plan.ToString());
                foreach (string file in plan.Changed.Concat(plan.Removed).Concat(new[] { Inventory }))
                {
                    string source = SafePath(engine, file), destination = SafePath(backup, file);
                    if (!File.Exists(source)) continue; Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Move(source, destination); moved.Add(file);
                }
                foreach (string file in plan.Added.Concat(plan.Changed))
                {
                    if (beforeCopy != null) beforeCopy(file);
                    string destination = SafePath(engine, file); Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
                    { written.Add(file); using (var input = File.OpenRead(SafePath(stage, file))) input.CopyTo(output); }
                }
                using (var output = new StreamWriter(new FileStream(SafePath(engine, Inventory), FileMode.CreateNew, FileAccess.Write), Encoding.UTF8)) { written.Add(Inventory); output.Write(serializer.Serialize(new EngineInventory { Version = version, Source = originalExecutableHash == null ? "upstream" : "upstream-branded", OriginalExecutableSha256 = originalExecutableHash ?? "", BrandIconSha256 = brandIconHash ?? "", Files = files, Hashes = files.ToDictionary(file => file, file => EngineReleaseClient.Hash(SafePath(stage, file))) })); }
                string installManifest = SafePath(packageRoot, "install-manifest.txt");
                if (File.Exists(installManifest))
                {
                    var manifest = File.ReadAllLines(installManifest).Where(f => !f.Replace('\\', '/').StartsWith("engine/", StringComparison.OrdinalIgnoreCase) || !formerEngineFiles.Contains(f.Replace('\\', '/').Substring(7))).ToList();
                    manifest.AddRange(files.Concat(new[] { Inventory }).Select(f => "engine/" + f)); manifest.AddRange(moved.Select(f => backupRelative + "/" + f));
                    foreach (string file in manifest) SafePath(packageRoot, file);
                    string temporary = SafePath(packageRoot, "updates/manifest-" + Guid.NewGuid().ToString("N") + ".tmp");
                    try { File.WriteAllLines(temporary, manifest.Distinct(StringComparer.OrdinalIgnoreCase)); File.Replace(temporary, installManifest, null); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                return plan;
            }
            catch
            {
                foreach (string file in written.AsEnumerable().Reverse()) { string path = SafePath(engine, file); if (File.Exists(path)) File.Delete(path); }
                foreach (string file in moved.AsEnumerable().Reverse()) { string destination = SafePath(engine, file); Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Move(SafePath(backup, file), destination); }
                throw;
            }
        }
        public static void RemoveStage(string root, IEnumerable<string> files)
        {
            var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase); string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            foreach (string file in files) {
                string path = SafePath(root, file); if (File.Exists(path)) File.Delete(path); string parent = Path.GetDirectoryName(path);
                while (parent != null && parent.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { parents.Add(parent); parent = Path.GetDirectoryName(parent); }
            }
            foreach (string parent in parents.OrderByDescending(p => p.Length)) if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
            if (Directory.Exists(fullRoot) && !Directory.EnumerateFileSystemEntries(fullRoot).Any()) Directory.Delete(fullRoot);
        }
    }
}
