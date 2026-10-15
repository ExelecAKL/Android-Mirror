using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using AndroidMirror;

static class EngineUpdateTests
{
    sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".engine-update-test-" + Guid.NewGuid().ToString("N"));
        public string Engine { get { return Path.Combine(Root, "engine"); } }
        public string Stage { get { return Path.Combine(Root, "stage"); } }
        public Fixture() {
            Directory.CreateDirectory(Engine); Directory.CreateDirectory(Stage);
            foreach (string name in new[] { "same.dll", "changed.dll", "removed.dll" }) File.WriteAllText(Path.Combine(Engine, name), "old " + name);
            File.WriteAllText(Path.Combine(Engine, "keep-user-file.txt"), "user");
            File.WriteAllText(Path.Combine(Engine, EngineLayout.Executable), "branded UI entry");
            var names = new[] { "same.dll", "changed.dll", "removed.dll" };
            File.WriteAllText(Path.Combine(Engine, EngineUpdateFiles.Inventory), new JavaScriptSerializer().Serialize(new EngineInventory { Version = "1.0.0", Files = names.ToList(), Hashes = names.ToDictionary(n => n, n => EngineReleaseClient.Hash(Path.Combine(Engine, n))) }));
            File.Copy(Path.Combine(Engine, "same.dll"), Path.Combine(Stage, "same.dll")); File.WriteAllText(Path.Combine(Stage, "changed.dll"), "new"); File.WriteAllText(Path.Combine(Stage, "added.dll"), "new");
            File.WriteAllLines(Path.Combine(Root, "install-manifest.txt"), new[] { "engine/same.dll", "engine/changed.dll", "engine/removed.dll", "engine/" + EngineLayout.Executable, "engine/" + EngineUpdateFiles.Inventory, "Android Mirror.exe", "install-manifest.txt" });
        }
        public string[] NewFiles = { "same.dll", "changed.dll", "added.dll" };
        public void Dispose() {
            string full = Path.GetFullPath(Root), prefix = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\') + "\\";
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith(".engine-update-test-")) throw new IOException("Unsafe test cleanup path.");
            foreach (string file in Directory.GetFiles(full, "*", SearchOption.AllDirectories)) File.Delete(file);
            foreach (string directory in Directory.GetDirectories(full, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length)) if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            Directory.Delete(full);
        }
    }
    sealed class ReplyHandler : HttpMessageHandler
    {
        public byte[] Data;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Data) }); }
    }
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static byte[] CodeSection(string path) {
        using (var file = File.OpenRead(path)) using (var reader = new BinaryReader(file)) {
            file.Position = 0x3c; int pe = reader.ReadInt32(); file.Position = pe + 6; int sections = reader.ReadUInt16(); file.Position = pe + 20; int optional = reader.ReadUInt16();
            for (int i = 0; i < sections; i++) { file.Position = pe + 24 + optional + i * 40; string name = Encoding.ASCII.GetString(reader.ReadBytes(8)).TrimEnd('\0'); file.Position += 8; int size = reader.ReadInt32(), offset = reader.ReadInt32(); if (name == ".text") { file.Position = offset; return reader.ReadBytes(size); } }
        }
        throw new IOException("The executable code section was not found.");
    }
    static string Json(string url = "https://github.com/Genymobile/scrcpy/releases/download/v2.0.0/scrcpy-win64-v2.0.0.zip", string digest = null) {
        return new JavaScriptSerializer().Serialize(new { tag_name = "v2.0.0", draft = false, prerelease = false, body = "Changes", assets = new[] { new { name = "scrcpy-win64-v2.0.0.zip", browser_download_url = url, digest = digest ?? "sha256:" + new string('a', 64), size = 3 } } });
    }
    public static void Run(Action<string, Action> test)
    {
        test("Upstream updates retain the engine name and icon without altering executable code", () => {
            using (var f = new Fixture()) {
                string zip = Path.Combine(f.Root, "upstream.zip"), stage = Path.Combine(f.Root, "branding-stage");
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) {
                    archive.CreateEntryFromFile(System.Reflection.Assembly.GetExecutingAssembly().Location, "scrcpy-win64-v2.0.0/scrcpy.exe");
                    foreach (string name in new[] { "adb.exe", "scrcpy-server", "LICENSE.txt" }) using (var writer = new StreamWriter(archive.CreateEntry("scrcpy-win64-v2.0.0/" + name).Open())) writer.Write("fixture");
                }
                var files = EngineUpdateFiles.Extract(zip, stage, "v2.0.0"); string executable = Path.Combine(stage, EngineLayout.RuntimeExecutable), originalHash = EngineReleaseClient.Hash(executable); byte[] code = CodeSection(executable);
                Assert(File.Exists(executable) && !File.Exists(Path.Combine(stage, "scrcpy.exe")), "The engine name reverted to the upstream name");
                string icon = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "assets", "icons", "Android Mirror.ico"));
                EngineBranding.Apply(executable, icon);
                Assert(CodeSection(executable).SequenceEqual(code) && EngineReleaseClient.Hash(executable) != originalHash, "Branding changed executable code or no icon resource was applied");
                string iconCheck = Path.Combine(f.Root, "icon-check.exe"); File.Copy(executable, iconCheck);
                using (var expected = new Icon(icon, 32, 32)) using (var actual = Icon.ExtractAssociatedIcon(iconCheck)) using (var wanted = expected.ToBitmap()) using (var rendered = actual.ToBitmap()) {
                    int matching = 0; for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) if (wanted.GetPixel(x, y).ToArgb() == rendered.GetPixel(x, y).ToArgb()) matching++;
                    Assert(matching > 1000, "The engine did not expose Android Mirror's icon");
                }
                EngineUpdateFiles.Apply(f.Root, stage, files, "2.0.0", text => { }, originalExecutableHash: originalHash, brandIconHash: EngineReleaseClient.Hash(icon)); EngineUpdateFiles.VerifyUnmodified(f.Engine);
                var inventory = EngineUpdateFiles.ReadInventory(f.Engine); Assert(inventory.Source == "upstream-branded" && inventory.OriginalExecutableSha256 == originalHash, "Upstream provenance or branded update inventory was lost");
            }
        });
        test("Updater accepts only a verifiable official Windows release", () => {
            var release = EngineReleaseClient.Parse(Json()); Assert(release.NewerThan("1.0.0") && !release.NewerThan("2.0.0") && release.Notes == "Changes", "Stable release parsing failed");
            int rejected = 0; foreach (string json in new[] { Json("https://example.com/engine.zip"), Json(digest: "sha256:invalid"), Json().Replace("\"prerelease\":false", "\"prerelease\":true") }) try { EngineReleaseClient.Parse(json); } catch (IOException) { rejected++; }
            Assert(rejected == 3, "Untrusted or preview release accepted");
        });
        test("Failed download verification preserves the previous cached package", () => {
            using (var f = new Fixture()) using (var client = new EngineReleaseClient(new ReplyHandler { Data = Encoding.ASCII.GetBytes("bad") })) {
                string path = Path.Combine(f.Root, "package.zip"); File.WriteAllText(path, "keep"); bool rejected = false;
                try { client.Download(EngineReleaseClient.Parse(Json()), path, null, CancellationToken.None).GetAwaiter().GetResult(); } catch (IOException) { rejected = true; }
                Assert(rejected && File.ReadAllText(path) == "keep" && !File.Exists(path + ".partial"), "Bad package replaced a trusted download");
            }
        });
        test("Engine update changes only modified, new, and removed owned files", () => {
            using (var f = new Fixture()) {
                DateTime unchanged = File.GetLastWriteTimeUtc(Path.Combine(f.Engine, "same.dll"));
                var plan = EngineUpdateFiles.Apply(f.Root, f.Stage, f.NewFiles, "2.0.0", text => { });
                Assert(plan.Added.Count == 1 && plan.Changed.Count == 1 && plan.Removed.Count == 1 && plan.Unchanged == 1, "Wrong delta plan");
                Assert(File.ReadAllText(Path.Combine(f.Engine, "changed.dll")) == "new" && !File.Exists(Path.Combine(f.Engine, "removed.dll")) && File.ReadAllText(Path.Combine(f.Engine, "keep-user-file.txt")) == "user" && File.GetLastWriteTimeUtc(Path.Combine(f.Engine, "same.dll")) == unchanged, "User or unchanged files were modified");
                EngineUpdateFiles.VerifyUnmodified(f.Engine);
                string manifest = File.ReadAllText(Path.Combine(f.Root, "install-manifest.txt")); Assert(manifest.Contains("engine/added.dll") && !manifest.Split('\n').Any(line => line.Trim() == "engine/removed.dll") && manifest.Contains("updates/backups/") && manifest.Contains("engine/" + EngineLayout.Executable) && File.ReadAllText(Path.Combine(f.Engine, EngineLayout.Executable)) == "branded UI entry", "Uninstall manifest or branded entry did not follow the update");
            }
        });
        test("A failed engine update rolls every changed file and inventory back", () => {
            using (var f = new Fixture()) {
                string before = File.ReadAllText(Path.Combine(f.Engine, EngineUpdateFiles.Inventory)); bool failed = false;
                try { EngineUpdateFiles.Apply(f.Root, f.Stage, f.NewFiles, "2.0.0", text => { }, name => { if (name == "changed.dll") throw new IOException("Simulated copy failure"); }); } catch (IOException) { failed = true; }
                Assert(failed && File.ReadAllText(Path.Combine(f.Engine, "changed.dll")) == "old changed.dll" && File.Exists(Path.Combine(f.Engine, "removed.dll")) && !File.Exists(Path.Combine(f.Engine, "added.dll")) && File.ReadAllText(Path.Combine(f.Engine, EngineUpdateFiles.Inventory)) == before, "Rollback lost the former engine");
            }
        });
        test("Local engine modifications pause binary updates before touching files", () => {
            using (var f = new Fixture()) {
                File.WriteAllText(Path.Combine(f.Engine, "changed.dll"), "our custom patch"); bool blocked = false;
                try { EngineUpdateFiles.Apply(f.Root, f.Stage, f.NewFiles, "2.0.0", text => { }); } catch (IOException) { blocked = true; }
                Assert(blocked && File.ReadAllText(Path.Combine(f.Engine, "changed.dll")) == "our custom patch" && !Directory.Exists(Path.Combine(f.Root, "updates")), "Custom engine changes were overwritten");
            }
        });
        test("An engine update cannot claim an unrelated file, even if its bytes match", () => {
            using (var f = new Fixture()) {
                File.WriteAllText(Path.Combine(f.Engine, "added.dll"), "new"); bool rejected = false;
                try { EngineUpdateFiles.Plan(f.Engine, f.Stage, f.NewFiles); } catch (IOException) { rejected = true; }
                Assert(rejected && File.ReadAllText(Path.Combine(f.Engine, "added.dll")) == "new", "Unrelated file became update-owned");
            }
        });
        test("Update archive traversal is rejected and the partial stage is removed", () => {
            using (var f = new Fixture()) {
                string zip = Path.Combine(f.Root, "bad.zip"), stage = Path.Combine(f.Root, "bad-stage");
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) { using (var output = new StreamWriter(archive.CreateEntry("scrcpy-win64-v2.0.0/../escape.txt").Open())) output.Write("bad"); }
                bool rejected = false; try { EngineUpdateFiles.Extract(zip, stage, "v2.0.0"); } catch (IOException) { rejected = true; }
                Assert(rejected && !File.Exists(Path.Combine(f.Root, "escape.txt")) && !Directory.Exists(stage), "Archive escaped staging");
            }
        });
    }
    public static int Live()
    {
        using (var client = new EngineReleaseClient()) {
            var release = client.Get(null, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine("Official stable engine: " + release.Version + "; Windows x64 SHA-256 available.");
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "live-update-validation"); Directory.CreateDirectory(directory);
            string archive = Path.Combine(directory, "engine-" + release.Tag + ".zip"); client.Download(release, archive, null, CancellationToken.None).GetAwaiter().GetResult();
            string stage = Path.Combine(directory, "stage-" + Guid.NewGuid().ToString("N")); var files = EngineUpdateFiles.Extract(archive, stage, release.Tag);
            Console.WriteLine("PASS official release downloaded, checksum verified, and safely extracted: " + files.Count + " files. Installed engine was not changed."); EngineUpdateFiles.RemoveStage(stage, files);
        }
        return 0;
    }
}
