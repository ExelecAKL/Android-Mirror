using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AndroidMirror
{
    public sealed class AppReleaseClient : IDisposable
    {
        public const string Repository = "https://github.com/ExelecAKL/Android-Mirror";
        public const string Api = "https://api.github.com/repos/ExelecAKL/Android-Mirror/releases/latest";
        readonly HttpClient http;
        public AppReleaseClient(HttpMessageHandler handler = null)
        {
            http = handler == null ? new HttpClient() : new HttpClient(handler);
            http.Timeout = TimeSpan.FromSeconds(20);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AndroidMirror/" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }
        public async Task<EngineRelease> Get(CancellationToken token, string tag = null)
        {
            if (tag != null && !Regex.IsMatch(tag, @"^v[0-9]+\.[0-9]+\.[0-9]+$")) throw new ArgumentException("Invalid Android Mirror version.");
            string url = tag == null ? Api : Api.Substring(0, Api.Length - "latest".Length) + "tags/" + tag;
            using (var response = await http.GetAsync(url, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }
        public static EngineRelease Parse(string json)
        {
            var data = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(json);
            if (data == null || !data.ContainsKey("tag_name") || !data.ContainsKey("assets")) throw new IOException("The Android Mirror release is incomplete.");
            string tag = Convert.ToString(data["tag_name"]);
            if (!Regex.IsMatch(tag, @"^v[0-9]+\.[0-9]+\.[0-9]+$") || !data.ContainsKey("draft") || !data.ContainsKey("prerelease") || Convert.ToBoolean(data["draft"]) || Convert.ToBoolean(data["prerelease"])) throw new IOException("Only stable Android Mirror releases can be installed.");
            string version = tag.Substring(1), expected = "Android-Mirror-" + version + "-Setup.exe";
            var assets = data["assets"] as System.Collections.IEnumerable;
            if (assets != null) foreach (Dictionary<string, object> asset in assets)
            {
                if (!asset.ContainsKey("name") || Convert.ToString(asset["name"]) != expected) continue;
                string url = asset.ContainsKey("browser_download_url") ? Convert.ToString(asset["browser_download_url"]) : "";
                string digest = asset.ContainsKey("digest") ? Convert.ToString(asset["digest"]) : "";
                long size = asset.ContainsKey("size") ? Convert.ToInt64(asset["size"]) : 0;
                if (!String.Equals(url, Repository + "/releases/download/" + tag + "/" + expected, StringComparison.Ordinal) || !Regex.IsMatch(digest, @"^sha256:[a-fA-F0-9]{64}$") || size < 1 || size > 512L * 1024 * 1024) throw new IOException("The Android Mirror installer could not be verified.");
                return new EngineRelease { Tag = tag, Version = version, Url = url, Sha256 = digest.Substring(7).ToLowerInvariant(), Size = size, Notes = data.ContainsKey("body") ? Convert.ToString(data["body"]) : "" };
            }
            throw new IOException("The Android Mirror release has no Windows installer.");
        }
        public static bool IsInstalled(string directory)
        {
            string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Android Mirror");
            return String.Equals(Path.GetFullPath(directory).TrimEnd('\\'), installed.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(directory, "install-manifest.txt"));
        }
        public void Dispose() { http.Dispose(); }
    }
}
