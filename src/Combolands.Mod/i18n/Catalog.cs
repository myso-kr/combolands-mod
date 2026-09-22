using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Combolands.Mod.I18n
{
    // Key -> translated text. Loaded once, read from a Harmony postfix on every
    // string the game draws, so lookup is a dictionary hit and nothing else.
    //
    // A key that is absent is not an error: the postfix leaves the English alone.
    // That is what lets the translation ship partial and grow, and it is why the
    // locale file holds only what has actually been translated.
    internal static class Catalog
    {
        private static Dictionary<string, string> _map =
            new Dictionary<string, string>(StringComparer.Ordinal);

        internal static int Count { get { return _map.Count; } }
        internal static bool Loaded { get; private set; }

        internal static void Load(string language)
        {
            Loaded = false;
            _map = new Dictionary<string, string>(StringComparer.Ordinal);

            if (string.IsNullOrEmpty(language))
            {
                Log.Info("i18n", "no language configured; translation off");
                return;
            }

            var path = Config.LocaleFile(language);
            if (!File.Exists(path))
            {
                Log.Warn("i18n", "no locale file at " + path + "; translation off");
                return;
            }

            try
            {
                // Explicit UTF-8. A locale file read in the system code page turns
                // every Hangul byte pair into two wrong characters, and the failure
                // looks like a font problem rather than an encoding one.
                var text = File.ReadAllText(path, new UTF8Encoding(false));
                _map = Json.ReadFlatObject(text);
                Loaded = true;
                Log.Info("i18n", "loaded " + _map.Count + " strings from " + language);
            }
            catch (Exception e)
            {
                // Refusing the whole file is the point. A half-parsed catalogue would
                // translate some of the UI and leave the rest English, which reads as
                // a dozen separate bugs.
                Log.Error("i18n", "locale file rejected, translation off: " + e.Message);
                _map = new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        internal static bool TryGet(string key, out string translated)
        {
            if (key != null && _map.TryGetValue(key, out translated) && translated.Length > 0)
                return true;
            translated = null;
            return false;
        }
    }
}
