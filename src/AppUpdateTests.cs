using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using AndroidMirror;

static class AppUpdateTests
{
    sealed class Reply : HttpMessageHandler
    {
        public string Json, Requested;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Requested = request.RequestUri.AbsoluteUri; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json) }); }
    }
    static string Metadata(string tag = "v1.2.0", string host = AppReleaseClient.Repository, string digest = null, bool prerelease = false, bool draft = false, long size = 123)
    {
        string name = "Android-Mirror-" + tag.Substring(1) + "-Setup.exe";
        return new JavaScriptSerializer().Serialize(new { tag_name = tag, draft, prerelease, body = "New sharing features", assets = new[] { new { name, size, browser_download_url = host + "/releases/download/" + tag + "/" + name, digest = digest ?? "sha256:" + new string('a', 64) } } });
    }
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(Action<string, Action> test)
    {
        test("Update popup summaries turn publisher Markdown into readable bounded changes", () => {
            string notes="## Changes\n- **Keep sharing** when minimized.\n- Fix [connection errors](https://example.com/details).\n```powershell\nignored code\n```\n<script>\n";
            string summary=ReleaseNotesSummary.Create(notes);
            Assert(summary.Contains("• Keep sharing when minimized.")&&summary.Contains("Fix connection errors.")&&!summary.Contains("**")&&!summary.Contains("https://")&&!summary.Contains("ignored code")&&!summary.Contains("<script>"),"Release notes were not formatted for the popup");
            Assert(ReleaseNotesSummary.Create("").Contains("did not include")&&ReleaseNotesSummary.Create(String.Join("\n",Enumerable.Repeat("- "+new string('x',500),20))).Length<1700,"Missing or oversized notes were mishandled");
        });
        test("Connection log branding changes display text without losing device or error details", () => {
            string root = @"C:\Downloads\scrcpy-win64-v5.0.1";
            string log = DiagnosticText.Display("itel S66LN: scrcpy 5.0.1 <https://github.com/Genymobile/scrcpy>\n" + root + "\\engine\\scrcpy-server: 1 file pushed\nERROR: device 123 unauthorized",root);
            Assert(log.Contains("Android Mirror 5.0.1") && log.Contains(AppReleaseClient.Repository) && log.Contains("[Android Mirror]\\engine\\Android Mirror service: 1 file pushed") && log.Contains("ERROR: device 123 unauthorized") && log.IndexOf("scrcpy",StringComparison.OrdinalIgnoreCase)<0 && !log.Contains("Genymobile"),"Log branding lost connection details or leaked the old banner");
        });
        test("App updater accepts only a stable verified release from our repository", () => {
            var release = AppReleaseClient.Parse(Metadata());
            Assert(release.Version == "1.2.0" && release.NewerThan("1.1.1") && !release.NewerThan("1.2.0") && !release.NewerThan("1.3.0") && release.Notes == "New sharing features", "Release version or notes failed");
            int rejected = 0;
            foreach (string json in new[] { Metadata(host:"https://example.com/Android-Mirror"), Metadata(host:AppReleaseClient.Repository + "-fake"), Metadata(digest:"sha256:bad"), Metadata(prerelease:true), Metadata(draft:true), Metadata(size:0), Metadata(size:600L*1024*1024), Metadata(tag:"v1.2.0-beta") }) try { AppReleaseClient.Parse(json); } catch (IOException) { rejected++; }
            Assert(rejected == 8, "Untrusted, incomplete, or preview app release was accepted");
        });
        test("App and engine release endpoints stay independent", () => {
            var reply = new Reply { Json = Metadata() };
            using (var client = new AppReleaseClient(reply)) {
                client.Get(CancellationToken.None).GetAwaiter().GetResult(); Assert(reply.Requested == AppReleaseClient.Api, "Wrong app update endpoint");
                client.Get(CancellationToken.None,"v1.2.0").GetAwaiter().GetResult(); Assert(reply.Requested.EndsWith("/Android-Mirror/releases/tags/v1.2.0"), "Installer verification did not pin the selected release");
                bool rejected = false; try { client.Get(CancellationToken.None,"../latest").GetAwaiter().GetResult(); } catch (ArgumentException) { rejected = true; } Assert(rejected,"Invalid version path accepted");
            }
        });
        test("Update prompts and background sharing preferences survive upgrades", () => {
            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"app-prefs-"+Guid.NewGuid().ToString("N")+".xml");
            try {
                var defaults = new AppPreferences(); Assert(defaults.CheckAppUpdates && defaults.KeepSharingWhenMinimized,"New features must default on");
                File.WriteAllText(file,"<AppPreferences><AutoReconnect>true</AutoReconnect></AppPreferences>");
                var old = AppPreferences.Load(file,""); Assert(old.CheckAppUpdates && old.KeepSharingWhenMinimized,"Older preferences did not inherit safe defaults");
                old.CheckAppUpdates=false;old.KeepSharingWhenMinimized=false;old.DeferredAppVersion="v1.2.0";old.DeferAppUpdateUntil=new DateTime(2026,10,16,0,0,0,DateTimeKind.Utc);old.ApprovedEngineVersion="v5.0.2";old.DeferredEngineVersion="v5.0.3";old.DeferEngineUpdateUntil=old.DeferAppUpdateUntil;old.Save(file);
                var saved=AppPreferences.Load(file,"");Assert(!saved.CheckAppUpdates&&!saved.KeepSharingWhenMinimized&&saved.DeferredAppVersion=="v1.2.0"&&saved.DeferAppUpdateUntil==old.DeferAppUpdateUntil,"Update choices were not retained");
                Assert(saved.ApprovedEngineVersion=="v5.0.2"&&saved.DeferredEngineVersion=="v5.0.3"&&saved.DeferEngineUpdateUntil==old.DeferEngineUpdateUntil,"Engine update consent was not retained separately");
                Assert(!AppReleaseClient.IsInstalled(AppDomain.CurrentDomain.BaseDirectory),"Portable copy was treated as installed");
            } finally { if(File.Exists(file))File.Delete(file); }
        });
    }
    public static int Live()
    {
        using(var client=new AppReleaseClient()) {var release=client.Get(CancellationToken.None).GetAwaiter().GetResult();Console.WriteLine("PASS official Android Mirror release metadata: "+release.Tag+" · "+release.Size+" bytes · verified SHA-256 provided");}return 0;
    }
}
