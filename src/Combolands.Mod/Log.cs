using System;
using MelonLoader;

namespace Combolands.Mod
{
    // One format, so there is one thing to grep in MelonLoader/Latest.log.
    //
    // Guard() exists because a Harmony patch that throws does not crash the game - it
    // leaves the feature quietly dead. Everything that runs inside a patch or a Unity
    // callback goes through here so the failure says which module, once, instead of
    // spamming the log every frame.
    internal static class Log
    {
        // module -> when it may complain again, and how many times it has failed.
        private static readonly System.Collections.Generic.Dictionary<string, DateTime> Muted =
            new System.Collections.Generic.Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly System.Collections.Generic.Dictionary<string, int> Counts =
            new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);

        // Long enough that a per-frame failure does not flood the log, short enough
        // that a problem which is still happening says so. Going quiet forever is how
        // the last two bugs hid: the helper had been throwing for an hour and the only
        // sign was a single line near the top of the file.
        private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(30);

        internal static void Info(string module, string message)
        {
            MelonLogger.Msg(module + ": " + message);
        }

        internal static void Warn(string module, string message)
        {
            MelonLogger.Warning(module + ": " + message);
        }

        internal static void Error(string module, string message)
        {
            MelonLogger.Error(module + ": " + message);
        }

        // Whether a module has already thrown and gone quiet. The panel reads this so
        // a dead feature reports itself in the UI instead of just doing nothing.
        internal static bool HasFailed(string module)
        {
            return Counts.ContainsKey(module);
        }

        internal static int FailureCount(string module)
        {
            int count;
            return Counts.TryGetValue(module, out count) ? count : 0;
        }

        internal static void Guard(string module, Action body)
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                Note(module, e);
            }
        }

        internal static T Guard<T>(string module, Func<T> body, T fallback)
        {
            try
            {
                return body();
            }
            catch (Exception e)
            {
                Note(module, e);
                return fallback;
            }
        }

        // Full detail the first time, then at most once every Quiet - with the count,
        // so "it happened once" and "it is happening every frame" look different.
        private static void Note(string module, Exception error)
        {
            int count;
            Counts.TryGetValue(module, out count);
            Counts[module] = count + 1;

            DateTime silentUntil;
            var now = DateTime.UtcNow;
            if (Muted.TryGetValue(module, out silentUntil) && now < silentUntil) return;

            Muted[module] = now + Quiet;
            MelonLogger.Error(count == 0
                ? module + " failed: " + error
                : module + " has now failed " + (count + 1) + " times, most recently: " + error);
        }
    }
}
