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
        private static MelonPreferences_Entry<int> _helperSpread;
        private static MelonPreferences_Entry<bool> _helperLabels;
        private static MelonPreferences_Entry<bool> _helperVisibleOnly;
        private static MelonPreferences_Entry<bool> _helperScout;
        private static MelonPreferences_Entry<bool> _autoplay;
        private static MelonPreferences_Entry<string> _autoplayKey;
        private static MelonPreferences_Entry<string> _autoplayStepKey;
        private static MelonPreferences_Entry<int> _autoplayDelay;
        private static MelonPreferences_Entry<bool> _autoplayPicks;
        private static MelonPreferences_Entry<bool> _autoplayBlocksAchievements;
        private static MelonPreferences_Entry<int> _autoplayUseItems;
        private static MelonPreferences_Entry<int> _autoplayShopMinGold;
        private static MelonPreferences_Entry<int> _autoplayShopReserve;
        private static MelonPreferences_Entry<int> _autoplayShopMinRarity;
        private static MelonPreferences_Entry<int> _helperOfferPicks;

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
            // Eight rather than five. With separation on, each one is a genuinely
            // different part of the map, so more of them is more choice rather than
            // more clutter.
            _helperShortlist = _cat.CreateEntry("PlayHelperShortlist", 8,
                description: "How many tiles the overlay highlights.");

            // Every scoring term is an absolute count of nearby pieces, so tiles
            // beside a building always outscore open ground and the top five land in
            // one cluster - five highlights that are really one suggestion. This is
            // how far apart they have to be. 0 turns it off.
            _helperSpread = _cat.CreateEntry("PlayHelperSpread", 3,
                description: "Minimum tiles between highlighted suggestions. 0 shows the raw ranking.");

            _helperLabels = _cat.CreateEntry("PlayHelperLabels", true,
                description: "Draw #rank and xN target count on each highlighted tile.");
            _helperVisibleOnly = _cat.CreateEntry("PlayHelperVisibleOnly", true,
                description: "Only suggest tiles the camera can see. Off searches the whole map.");

            // With nothing in hand, answer the question that comes first: which of
            // the offered buildings, and where.
            _helperScout = _cat.CreateEntry("PlayHelperScout", true,
                description: "With no building in hand, show where each offered building would go.");
            _helperOfferPicks = _cat.CreateEntry("PlayHelperOfferPicks", 2,
                description: "Tiles highlighted per offered building while scouting.");

            _autoplay = _cat.CreateEntry("Autoplay", true,
                description: "Enable auto-placement. The loop itself still starts stopped.");
            _autoplayKey = _cat.CreateEntry("AutoplayKey", "F11",
                description: "Key that starts and stops the autoplay loop.");
            _autoplayStepKey = _cat.CreateEntry("AutoplayStepKey", "F10",
                description: "Key that performs one autoplay action and stops.");
            _autoplayDelay = _cat.CreateEntry("AutoplayDelay", 24,
                description: "Frames between autoplay actions. Lower is faster and harder to follow.");
            _autoplayPicks = _cat.CreateEntry("AutoplayPicksBuildings", true,
                description: "Let autoplay choose which offered building to place, not just where.");

            // Default off, and the reasoning belongs with the setting: autoplay makes
            // only legal moves, so it does not change the rules the way a cheat does.
            // Whether an achievement a bot earned is one you earned is a different
            // question, and yours.
            _autoplayBlocksAchievements = _cat.CreateEntry("AutoplayBlocksAchievements", false,
                description: "Treat using autoplay like using a cheat, and stop submitting achievements.");

            // 0 never, 1 when the shelf is full or the milestone is tight, 2 always.
            // Only blueprints are used either way - see autoplay/Items.cs for why the
            // targeting consumables are left alone.
            _autoplayUseItems = _cat.CreateEntry("AutoplayUseItems", 1,
                description: "When autoplay spends blueprint consumables. 0 never, 1 when full or behind, 2 always.");

            // Skipping the shop is not the lazy option - the game pays a reward for
            // it - so with an empty purse the reward is strictly better than a look
            // around.
            _autoplayShopMinGold = _cat.CreateEntry("AutoplayShopMinGold", 25,
                description: "Gold needed before autoplay goes into the shop rather than taking the skip reward.");
            _autoplayShopReserve = _cat.CreateEntry("AutoplayShopReserve", 0,
                description: "Gold autoplay will not spend in the shop.");
            _autoplayShopMinRarity = _cat.CreateEntry("AutoplayShopMinRarity", 2,
                description: "Lowest rarity worth buying. 1 Common, 2 Uncommon, 3 Rare, 4 Masterwork, 5 Legendary.");

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
        internal static int HelperSpread { get { return Mathf.Clamp(_helperSpread.Value, 0, 12); } }
        internal static bool HelperLabels { get { return _helperLabels.Value; } }
        internal static bool HelperVisibleOnly { get { return _helperVisibleOnly.Value; } }
        internal static bool HelperScout { get { return _helperScout.Value; } }

        internal static bool Autoplay { get { return _autoplay.Value; } }
        internal static int AutoplayDelay { get { return Mathf.Clamp(_autoplayDelay.Value, 2, 600); } }
        internal static bool AutoplayPicks { get { return _autoplayPicks.Value; } }
        internal static bool AutoplayBlocksAchievements { get { return _autoplayBlocksAchievements.Value; } }
        internal static int AutoplayUseItems { get { return Mathf.Clamp(_autoplayUseItems.Value, 0, 2); } }
        internal static int AutoplayShopMinGold { get { return Mathf.Max(0, _autoplayShopMinGold.Value); } }
        internal static int AutoplayShopReserve { get { return Mathf.Max(0, _autoplayShopReserve.Value); } }
        internal static int AutoplayShopMinRarity { get { return Mathf.Clamp(_autoplayShopMinRarity.Value, 1, 5); } }

        private static KeyCode _parsedAutoplayKey = KeyCode.None;
        private static KeyCode _parsedAutoplayStepKey = KeyCode.None;

        internal static KeyCode AutoplayKey
        {
            get
            {
                if (_parsedAutoplayKey == KeyCode.None)
                    _parsedAutoplayKey = ParseKey(_autoplayKey.Value, KeyCode.F11, "AutoplayKey");
                return _parsedAutoplayKey;
            }
        }

        internal static KeyCode AutoplayStepKey
        {
            get
            {
                if (_parsedAutoplayStepKey == KeyCode.None)
                    _parsedAutoplayStepKey = ParseKey(_autoplayStepKey.Value, KeyCode.F10, "AutoplayStepKey");
                return _parsedAutoplayStepKey;
            }
        }

        private static KeyCode ParseKey(string name, KeyCode fallback, string setting)
        {
            try
            {
                return (KeyCode)System.Enum.Parse(typeof(KeyCode), name, true);
            }
            catch
            {
                Log.Warn("config", setting + " '" + name + "' is not a KeyCode; using " + fallback);
                return fallback;
            }
        }
        internal static int HelperOfferPicks { get { return Mathf.Clamp(_helperOfferPicks.Value, 1, 6); } }

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
