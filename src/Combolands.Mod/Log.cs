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
        private static readonly System.Collections.Generic.HashSet<string> Silenced =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

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

        internal static void Guard(string module, Action body)
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                // Report a given module's first failure in full, then stay quiet. A
                // patch on a per-frame method would otherwise write thousands of
                // identical stack traces and bury whatever came next.
                if (Silenced.Add(module))
                    MelonLogger.Error(module + " failed and is now silent: " + e);
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
                if (Silenced.Add(module))
                    MelonLogger.Error(module + " failed and is now silent: " + e);
                return fallback;
            }
        }
    }
}
