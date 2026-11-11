using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CursorFlow
{
    internal sealed class LocalizedEntry
    {
        public string key;
        public string fr;
        public string en;
        public string es;
    }

    // Presentation-only localization. Persisted tool/action values are never translated.
    internal static class L
    {
        private static readonly Dictionary<string, LocalizedEntry> byKey = new Dictionary<string, LocalizedEntry>(StringComparer.Ordinal);
        private static readonly Dictionary<string, LocalizedEntry> entries = Load();
        private static string language = "fr";
        internal static string Language { get { return language; } }
        internal static int EntryCount { get { return entries.Count; } }
        internal static string NormalizeLanguage(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "fr";
            string code = value.Trim().Replace('_', '-').ToLowerInvariant().Split('-')[0];
            return code == "en" || code == "es" ? code : "fr";
        }
        internal static void Initialize(string value)
        {
            // Called only during startup; choosing a language saves it for the next launch.
            // Do not change CurrentCulture, Windows settings, keyboard layout or an active stroke.
            language = NormalizeLanguage(value);
        }
        private static Dictionary<string, LocalizedEntry> Load()
        {
            var result = new Dictionary<string, LocalizedEntry>(StringComparer.Ordinal);
            using (Stream stream = typeof(L).Assembly.GetManifestResourceStream("Haloryva.Localization.json"))
            {
                if (stream == null) return result; // A broken resource must not prevent the French UI from starting.
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    var items = new JavaScriptSerializer().Deserialize<List<LocalizedEntry>>(reader.ReadToEnd());
                    foreach (var item in items)
                    {
                        byKey.Add(item.key, item);
                        if (!result.ContainsKey(item.fr)) result.Add(item.fr, item);
                    }
                }
            }
            return result;
        }
        internal static string T(string source)
        {
            if (String.IsNullOrEmpty(source) || language == "fr") return source ?? "";
            LocalizedEntry item;
            if (!entries.TryGetValue(source, out item)) return source;
            string translated = language == "es" ? item.es : item.en;
            return String.IsNullOrEmpty(translated) ? source : translated;
        }
        internal static string Key(string key)
        {
            LocalizedEntry item;
            if (!byKey.TryGetValue(key, out item)) return key;
            return language == "es" ? item.es : language == "en" ? item.en : item.fr;
        }
        internal static string Count(int count, string singular, string plural)
        {
            // French uses the singular for 0 and 1; English and Spanish only for 1.
            bool one = language == "fr" ? count == 0 || count == 1 : count == 1;
            return count.ToString(CultureInfo.CurrentCulture) + T(one ? singular : plural);
        }
        internal static string Format(string source, params object[] args)
        {
            return String.Format(CultureInfo.CurrentCulture, T(source), args);
        }
    }
}
