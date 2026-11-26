using System;
using System.IO;

namespace FindClick {
    internal static class UiText {
        internal static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FindClick2.1");
        internal static volatile bool English;

        internal static string T(string zh, string en) { return English ? en : zh; }
        internal static string F(string zh, string en, params object[] args) { return string.Format(T(zh, en), args); }

        internal static bool Read(string path) {
            try { return File.ReadAllText(path).Trim() == "en"; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal static bool Save(string path) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, English ? "en" : "zh");
                return true;
            } catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
