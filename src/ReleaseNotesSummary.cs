using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AndroidMirror
{
    public static class ReleaseNotesSummary
    {
        public static string Create(string markdown)
        {
            var bullets = new List<string>(); var paragraphs = new List<string>(); bool code = false;
            foreach (string raw in (markdown ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("```")) { code = !code; continue; }
                if (code || line.StartsWith("#") || line.StartsWith("<!--") || line == "---" || line.Length == 0) continue;
                bool bullet = Regex.IsMatch(line, @"^[-*+]\s+");
                line = Regex.Replace(line, @"^[-*+]\s+", "");
                line = Regex.Replace(line, @"!?\[([^\]]*)\]\([^)]*\)", "$1");
                line = Regex.Replace(line, @"<[^>]*>", "");
                line = line.Replace("**", "").Replace("__", "").Replace("`", "").Trim();
                if (line.Length == 0) continue;
                if (line.Length > 400) line = line.Substring(0, 397) + "…";
                if (bullet) bullets.Add("• " + line); else paragraphs.Add(line);
            }
            var selected = bullets.Count > 0 ? bullets : paragraphs;
            if (selected.Count == 0) return "The publisher did not include a list of changes for this release.";
            string result = String.Join("\n\n", selected.Take(6));
            if (result.Length > 1500) result = result.Substring(0, 1497) + "…";
            if (selected.Count > 6 || result.EndsWith("…")) result += "\n\nMore details are available in Settings → Updates.";
            return result;
        }
    }
}
