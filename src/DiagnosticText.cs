using System;
using System.Text.RegularExpressions;

namespace AndroidMirror
{
    public static class DiagnosticText
    {
        // Display aliases only: engine commands, files and version parsing keep
        // their actual names. Required notices remain in the packaged licences.
        public static string Display(string value, string directory)
        {
            if (String.IsNullOrEmpty(value)) return "";
            if (!String.IsNullOrEmpty(directory)) value = Regex.Replace(value, Regex.Escape(directory.TrimEnd('\\', '/')), "[Android Mirror]", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"https?://github\.com/Genymobile/scrcpy[^\s<>]*", AppReleaseClient.Repository, RegexOptions.IgnoreCase);
            value = Regex.Replace(value, "scrcpy-server", "Android Mirror service", RegexOptions.IgnoreCase);
            return Regex.Replace(value, "scrcpy", "Android Mirror", RegexOptions.IgnoreCase).TrimEnd();
        }
    }
}
