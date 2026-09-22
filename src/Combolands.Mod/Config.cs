using System.IO;
using MelonLoader;
using UnityEngine;

namespace Combolands.Mod
{
    // MelonPreferences binding, and the one place that knows where our files live.
    // Written to UserData/MelonPreferences.cfg on first run.
    internal static class Config
    {
        private static MelonPreferences_Category _cat;

        private static MelonPreferences_Entry<string> _language;
        private static MelonPreferences_Entry<bool> _translate;
        private static MelonPreferences_Entry<int> _fontSize;
        private static MelonPreferences_Entry<string> _fontFile;
        private static MelonPreferences_Entry<float> _fontScale;
        private static MelonPreferences_Entry<bool> _dumpStrings;

        private static MelonPreferences_Entry<bool> _cheats;
        private static MelonPreferences_Entry<string> _widgetKey;
        private static MelonPreferences_Entry<bool> _blockAchievements;
        private static MelonPreferences_Entry<float> _widgetScale;

        private static MelonPreferences_Entry<bool> _helper;
        private static MelonPreferences_Entry<string> _helperKey;
        private static MelonPreferences_Entry<int> _helperShortlist;

        internal static void Load()
        {
            _cat = MelonPreferences.CreateCategory("Combolands", "Combolands Mod");

            _language = _cat.CreateEntry("Language", "ko",
                description: "Locale folder under UserData/Combolands/locale. Empty disables translation.");
            _translate = _cat.CreateEntry("Translate", true,
                description: "Apply the language patch.");

            // 48 is what M1 proved with, and it is generous for an 11-pixel face: the
            // atlas spilled to a second 1024x1024 texture at ~225 glyphs. Lower costs
            // memory nothing and may read crisper; it is exposed so it can be judged
            // by eye rather than argued about.
            _fontSize = _cat.CreateEntry("FontSamplingPointSize", 32,
                description: "TMP sampling point size for the Korean fallback font.");
            _fontFile = _cat.CreateEntry("FontFile", "Galmuri11.ttf",
                description: "TTF under UserData/Combolands/fonts.");

            // Hangul fills its em box; Latin does not. At one point size a Korean
            // face therefore reads much larger than the game's own, which is exactly
            // what it looked like at 1.0. TMP scales each glyph by the faceInfo of
            // whichever asset supplied it, so this is the dial that fixes it.
            _fontScale = _cat.CreateEntry("FontScale", 0.8f,
                description: "Korean glyph size relative to the game's own font. 1.0 leaves it alone.");

            _cheats = _cat.CreateEntry("Cheats", true,
                description: "Enable the cheat widget.");
            _widgetKey = _cat.CreateEntry("WidgetKey", "F8",
                description: "Key that opens the cheat widget. Any UnityEngine.KeyCode name.");

            // Default on, and deliberately not phrased as a nanny setting: an
            // achievement unlocked by a cheat cannot be taken back, and it lands on
            // an account the player keeps long after this run.
            _blockAchievements = _cat.CreateEntry("BlockAchievements", true,
                description: "Stop submitting Steam achievements once any cheat is used this session.");

            // IMGUI draws in pixels with no notion of DPI, so the default skin is
            // unreadable on anything above 1080p - on a 1440p ultrawide the panel is
            // about a tenth of the screen. 0 derives a scale from the window height.
            _widgetScale = _cat.CreateEntry("WidgetScale", 0f,
                description: "Cheat widget scale. 0 picks one from the window height.");

            _helper = _cat.CreateEntry("PlayHelper", true,
                description: "Enable the placement helper overlay.");
            _helperKey = _cat.CreateEntry("PlayHelperKey", "F9",
                description: "Key that toggles the placement overlay. Any UnityEngine.KeyCode name.");
            _helperShortlist = _cat.CreateEntry("PlayHelperShortlist", 5,
                description: "How many tiles the overlay highlights.");

            _dumpStrings = _cat.CreateEntry("DumpStrings", false,
                description: "Developer: write every LocalizedStringAsset to generated/strings.en.json on first scene.");

            // Without this there is no UserData/MelonPreferences.cfg until the game
            // exits cleanly, so a player who wants to change the font size has
            // nothing to edit.
            MelonPreferences.Save();
        }

        internal static string Language { get { return _language.Value; } }
        internal static bool Translate { get { return _translate.Value; } }
        internal static int FontSamplingPointSize { get { return _fontSize.Value; } }
        internal static string FontFile { get { return _fontFile.Value; } }
        internal static float FontScale { get { return _fontScale.Value; } }
        internal static bool DumpStrings { get { return _dumpStrings.Value; } }

        internal static bool Cheats { get { return _cheats.Value; } }
        internal static bool BlockAchievements { get { return _blockAchievements.Value; } }

        internal static bool PlayHelper { get { return _helper.Value; } }
        internal static int HelperShortlist { get { return Mathf.Clamp(_helperShortlist.Value, 1, 20); } }

        private static KeyCode _parsedHelperKey = KeyCode.None;

        internal static KeyCode HelperKey
        {
            get
            {
                if (_parsedHelperKey != KeyCode.None) return _parsedHelperKey;
                try
                {
                    _parsedHelperKey = (KeyCode)System.Enum.Parse(typeof(KeyCode), _helperKey.Value, true);
                }
                catch
                {
                    Log.Warn("config", "PlayHelperKey '" + _helperKey.Value + "' is not a KeyCode; using F9");
                    _parsedHelperKey = KeyCode.F9;
                }
                return _parsedHelperKey;
            }
        }

        internal static float WidgetScale
        {
            get
            {
                var configured = _widgetScale.Value;
                if (configured > 0.01f) return configured;
                return Mathf.Clamp(Screen.height / 720f, 1f, 3f);
            }
        }

        // Parsed once. A typo falls back to F8 with a warning rather than leaving the
        // widget unreachable and unexplained.
        private static KeyCode _parsedKey = KeyCode.None;

        internal static KeyCode WidgetKey
        {
            get
            {
                if (_parsedKey != KeyCode.None) return _parsedKey;
                try
                {
                    _parsedKey = (KeyCode)System.Enum.Parse(typeof(KeyCode), _widgetKey.Value, true);
                }
                catch
                {
                    Log.Warn("config", "WidgetKey '" + _widgetKey.Value + "' is not a KeyCode; using F8");
                    _parsedKey = KeyCode.F8;
                }
                return _parsedKey;
            }
        }

        // Application.dataPath is <game>/Combolands_Data, so its parent is the install.
        internal static string GameDir
        {
            get { return Path.GetDirectoryName(Application.dataPath); }
        }

        internal static string DataDir
        {
            get { return Path.Combine(GameDir, Path.Combine("UserData", "Combolands")); }
        }

        internal static string LocaleFile(string lang)
        {
            return Path.Combine(DataDir, Path.Combine("locale", Path.Combine(lang, "strings.json")));
        }

        internal static string FontPath
        {
            get { return Path.Combine(DataDir, Path.Combine("fonts", FontFile)); }
        }

        internal static string DumpPath
        {
            get { return Path.Combine(DataDir, Path.Combine("generated", "strings.en.json")); }
        }
    }
}
