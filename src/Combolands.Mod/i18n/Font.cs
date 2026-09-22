using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Combolands.Mod.I18n
{
    // Builds a Hangul TMP font asset from a TTF on disk and hangs it off every font
    // the game uses, so any glyph the game's own faces are missing comes from ours.
    //
    // None of the game's five TMP fonts has a single Hangul glyph, so without this
    // the entire translation renders as tofu.
    internal static class Font
    {
        private static TMP_FontAsset _fallback;

        internal static bool Ready { get { return _fallback != null; } }

        internal static void Create()
        {
            var path = Config.FontPath;
            if (!File.Exists(path))
            {
                Log.Error("font", "no font at " + path + "; Korean will render as tofu");
                return;
            }

            // The 7-argument overload passes AtlasPopulationMode.Dynamic and records
            // the source path, which is what lets TMP reload the face later to
            // rasterise a glyph it has not seen. Only the glyphs actually drawn are
            // ever rendered, which is what makes 11,172 syllables affordable.
            _fallback = TMP_FontAsset.CreateFontAsset(
                path, 0, Config.FontSamplingPointSize, 9,
                GlyphRenderMode.SDFAA, 1024, 1024);

            if (_fallback == null)
            {
                Log.Error("font", "CreateFontAsset returned null for " + path);
                return;
            }

            _fallback.name = Path.GetFileNameWithoutExtension(path) + " SDF (mod)";
            Object.DontDestroyOnLoad(_fallback);

            // TMP sizes every glyph as  fontSize / faceInfo.pointSize * faceInfo.scale,
            // reading those from whichever asset supplied the glyph. So the fallback's
            // own scale is what makes Hangul sit at the same optical size as the
            // game's Latin face instead of towering over it.
            if (Mathf.Abs(Config.FontScale - 1f) > 0.001f)
            {
                var scaled = _fallback.faceInfo;
                scaled.scale = Config.FontScale;
                _fallback.faceInfo = scaled;
            }

            var face = _fallback.faceInfo;
            Log.Info("font", "created " + face.familyName + " " + face.styleName
                           + " at " + Config.FontSamplingPointSize + "pt, scale "
                           + _fallback.faceInfo.scale + ", " + _fallback.atlasPopulationMode);

            RegisterGlobal();
        }

        private static void RegisterGlobal()
        {
            var list = TMP_Settings.fallbackFontAssets;
            if (list == null)
                TMP_Settings.fallbackFontAssets = new List<TMP_FontAsset> { _fallback };
            else if (!list.Contains(_fallback))
                list.Add(_fallback);
        }

        // Must run on every scene load, not once at startup.
        //
        // At the main menu only five TMP fonts are loaded. monogram-extended SDF and
        // THEBOLDFONT-FREEVERSION SDF arrive with the game scene's assets, and a
        // single startup pass leaves both of them without Hangul - which shows up as
        // tofu on exactly two screens and nowhere else.
        internal static void RegisterWithLoadedFonts()
        {
            if (_fallback == null) return;

            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
                                 .Where(a => a != null && a != _fallback);

            int added = 0;
            foreach (var font in fonts)
            {
                if (font.fallbackFontAssetTable == null)
                    font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (font.fallbackFontAssetTable.Contains(_fallback)) continue;
                font.fallbackFontAssetTable.Add(_fallback);
                added++;
            }

            if (added > 0) Log.Info("font", "fallback added to " + added + " newly loaded font(s)");
        }
    }
}
