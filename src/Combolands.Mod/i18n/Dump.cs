using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Combolands.Mod.I18n
{
    // Developer command. Off unless Config.DumpStrings is set.
    //
    // The game's own LocalizedString.GetAssetList() calls
    // Resources.LoadAll<LocalizedStringAsset>(""), so the game will hand over every
    // string it has. That is worth more than an asset ripper here: these are Odin
    // SerializedScriptableObjects, and an external tool has to guess at a layout the
    // game does not have to guess at.
    //
    // Output is the English side. It never goes in the repository - it is the game's
    // text - but tools/ reads it to seed and to measure the translation.
    internal static class Dump
    {
        internal static void Run()
        {
            var type = Anchors.LocalizedStringAsset;
            var keyField = Anchors.Field(type, "Key");
            var getText = Anchors.Method(type, "GetText");
            if (type == null || keyField == null || getText == null) return;

            var loadAll = typeof(Resources)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "LoadAll" && m.IsGenericMethod && m.GetParameters().Length == 1)
                .MakeGenericMethod(type);

            var assets = (Array)loadAll.Invoke(null, new object[] { "" });
            if (assets == null || assets.Length == 0)
            {
                Log.Warn("dump", "Resources.LoadAll returned nothing (A4) - dump skipped");
                return;
            }

            var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
            int blank = 0, duplicate = 0;

            foreach (var asset in assets)
            {
                var key = keyField.GetValue(asset) as string;
                if (string.IsNullOrEmpty(key)) { blank++; continue; }
                if (map.ContainsKey(key)) { duplicate++; continue; }

                // Our own A1 postfix is live by now, so a translated key would dump
                // its Korean and the seed would quietly stop being English. Read the
                // backing field instead of calling GetText().
                map[key] = Original(asset) ?? "";
            }

            var path = Config.DumpPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Json.WriteFlatObject(map), new UTF8Encoding(false));

            Log.Info("dump", map.Count + " keys, " + blank + " blank, " + duplicate
                           + " duplicate -> " + path);
        }

        private static FieldInfo _original;

        private static string Original(object asset)
        {
            if (_original == null)
                _original = Anchors.Field(Anchors.LocalizedStringAsset, "_originalString");
            return _original == null ? null : _original.GetValue(asset) as string;
        }
    }
}
