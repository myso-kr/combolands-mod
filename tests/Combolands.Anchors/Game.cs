using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Combolands.Anchors
{
    // Finding an installed game and reading its metadata.
    //
    // The assemblies are not in this repository and must not be. What is here is the
    // knowledge of where a copy lives and how to read one without running it.
    internal static class Game
    {
        // Set COMBOLANDS_DIR to the folder holding Combolands.exe to check a build
        // that is not where Steam usually puts it - a depot download, a second
        // library, a Proton prefix.
        private const string Override = "COMBOLANDS_DIR";

        private static readonly object Gate = new object();
        private static MetadataLoadContext _context;
        private static string _managed;
        private static bool _looked;

        internal static string WhyMissing { get; private set; }

        // Null when there is no game to check, which is the normal state on CI and
        // is a skip rather than a failure. The catalogue is still checked against
        // itself in that case - see CatalogueTests.
        internal static MetadataLoadContext Metadata
        {
            get
            {
                lock (Gate)
                {
                    if (_looked) return _context;
                    _looked = true;
                    _context = Open();
                    return _context;
                }
            }
        }

        internal static string ManagedFolder
        {
            get { var _ = Metadata; return _managed; }
        }

        private static MetadataLoadContext Open()
        {
            // An explicit override that does not resolve is an error, not a reason to
            // go looking elsewhere. Silently checking a different copy than the one
            // that was named is the worst answer this can give.
            var named = Environment.GetEnvironmentVariable(Override);
            if (!string.IsNullOrWhiteSpace(named))
            {
                _managed = Managed(named);
                if (_managed == null)
                {
                    WhyMissing = Override + " is set to " + named
                               + " but there is no Assembly-CSharp.dll under it";
                    return null;
                }
            }
            else
            {
                _managed = Locate();
            }

            if (_managed == null)
            {
                WhyMissing = "no installed Combolands found - set " + Override
                           + " to the folder holding Combolands.exe";
                return null;
            }

            var assemblies = Directory.GetFiles(_managed, "*.dll");

            // Unity ships its own mscorlib, and it is the one the game assemblies were
            // compiled against. Handing MetadataLoadContext the runtime's core
            // assembly instead would make every System type resolve to a different
            // identity than the metadata claims.
            var core = assemblies.FirstOrDefault(
                a => string.Equals(Path.GetFileName(a), "mscorlib.dll", StringComparison.OrdinalIgnoreCase));
            if (core == null)
            {
                WhyMissing = "found " + _managed + " but it has no mscorlib.dll";
                return null;
            }

            try
            {
                return new MetadataLoadContext(new PathAssemblyResolver(assemblies), "mscorlib");
            }
            catch (Exception error)
            {
                WhyMissing = "could not read " + _managed + ": " + error.Message;
                return null;
            }
        }

        private static string Locate()
        {
            // The default Steam library on each platform, and the common second ones.
            // A missing drive or an unreadable path is just a candidate that does not
            // match; nothing here throws on a machine without the game.
            foreach (var library in Libraries())
            {
                var found = Managed(Path.Combine(library, "steamapps", "common", "Combolands"));
                if (found != null) return found;
            }
            return null;
        }

        // Accepts either the game folder or the Managed folder itself, because both
        // are reasonable things to point COMBOLANDS_DIR at.
        private static string Managed(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return null;

            try
            {
                var managed = Path.Combine(candidate, "Combolands_Data", "Managed");
                if (File.Exists(Path.Combine(managed, "Assembly-CSharp.dll"))) return managed;
                if (File.Exists(Path.Combine(candidate, "Assembly-CSharp.dll"))) return candidate;
            }
            catch { }

            return null;
        }

        private static IEnumerable<string> Libraries()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            yield return @"C:\Program Files (x86)\Steam";
            yield return @"C:\SteamLibrary";
            yield return @"D:\SteamLibrary";
            yield return @"D:\Steam";
            yield return Path.Combine(home, ".steam", "steam");
            yield return Path.Combine(home, ".local", "share", "Steam");
            yield return Path.Combine(home, "Library", "Application Support", "Steam");
        }

        // The assembly an anchor lives in. Crux's own code is in Assembly-CSharp;
        // the font anchors are in Unity's TextMeshPro package, which moves with the
        // engine version rather than with the game.
        internal static Assembly Load(string simpleName)
        {
            var context = Metadata;
            if (context == null) return null;

            try { return context.LoadFromAssemblyName(simpleName); }
            catch { return null; }
        }

        // A core-library type as the METADATA sees it. typeof(int) is a type from the
        // running .NET, which never equals mscorlib-from-the-game System.Int32, so
        // matching an overload by parameter type has to go through here.
        internal static Type Core(string fullName)
        {
            var context = Metadata;
            if (context == null) return null;

            try { return context.CoreAssembly.GetType(fullName, throwOnError: false); }
            catch { return null; }
        }

        internal static Type Find(string assembly, string fullName)
        {
            var loaded = Load(assembly);
            if (loaded == null) return null;

            try { return loaded.GetType(fullName, throwOnError: false); }
            catch { return null; }
        }
    }
}
