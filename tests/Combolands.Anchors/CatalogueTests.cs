using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Combolands.Anchors
{
    // Holding the catalogue and the documentation to each other.
    //
    // These need no game, so they run everywhere the rest of the suite does. They
    // guard the failure this whole project is supposed to prevent: an anchor that
    // exists in one place and not the other. A row with no entry is an anchor nobody
    // checks; an entry with no row is an anchor nobody can look up when it breaks.
    public class CatalogueTests
    {
        // Table rows only: a leading pipe, an id like A1 or P22, another pipe. The
        // header separator and the prose in between do not match.
        private static readonly Regex Row =
            new Regex(@"^\|\s*(?<id>[A-Z]\d{1,2})\s*\|\s*(?<what>[^|]+?)\s*\|", RegexOptions.Multiline);

        private static readonly Lazy<Dictionary<string, string>> Documented =
            new Lazy<Dictionary<string, string>>(Parse);

        [Fact]
        public void Every_documented_anchor_is_checked()
        {
            var orphans = Documented.Value.Keys
                .Where(id => Catalogue.All.All(a => a.Id != id))
                .OrderBy(Order)
                .ToList();

            Assert.True(orphans.Count == 0,
                "docs/ANCHORS.md documents anchors that Catalogue.cs does not check: "
                + string.Join(", ", orphans));
        }

        [Fact]
        public void Every_checked_anchor_is_documented()
        {
            var undocumented = Catalogue.All
                .Where(a => !Documented.Value.ContainsKey(a.Id))
                .Select(a => a.Id)
                .ToList();

            Assert.True(undocumented.Count == 0,
                "Catalogue.cs checks anchors with no row in docs/ANCHORS.md: "
                + string.Join(", ", undocumented)
                + " - a broken anchor is only useful if you can look up what it breaks");
        }

        // The table's own words, so the failure message and the documentation say the
        // same thing. Renaming an anchor in one place should be a compile-or-test
        // event, not a slow drift.
        [Fact]
        public void The_descriptions_agree()
        {
            var drifted = Catalogue.All
                .Where(a => Documented.Value.ContainsKey(a.Id))
                .Where(a => !string.Equals(a.What, Documented.Value[a.Id], StringComparison.Ordinal))
                .Select(a => a.Id + ": catalogue says \"" + a.What + "\", docs say \"" + Documented.Value[a.Id] + "\"")
                .ToList();

            Assert.True(drifted.Count == 0, string.Join("\n", drifted));
        }

        [Fact]
        public void No_anchor_is_listed_twice()
        {
            var repeated = Catalogue.All
                .GroupBy(a => a.Id)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.True(repeated.Count == 0, "duplicate ids in Catalogue.cs: " + string.Join(", ", repeated));
        }

        // An anchor that opts out of the offline check has to say what does answer it,
        // or it is just an anchor nobody verifies wearing a flag.
        [Fact]
        public void Anchors_that_cannot_be_checked_offline_say_why()
        {
            var silent = Catalogue.All
                .Where(a => !a.Bound && string.IsNullOrWhiteSpace(a.Why))
                .Select(a => a.Id)
                .ToList();

            Assert.True(silent.Count == 0,
                "these opt out of the offline check without saying what checks them instead: "
                + string.Join(", ", silent));
        }

        [Fact]
        public void Every_checkable_anchor_names_a_type()
        {
            var nameless = Catalogue.All
                .Where(a => a.Bound && string.IsNullOrWhiteSpace(a.Type))
                .Select(a => a.Id)
                .ToList();

            Assert.True(nameless.Count == 0, "no type to look for: " + string.Join(", ", nameless));
        }

        private static Dictionary<string, string> Parse()
        {
            var path = Path.Combine(RepoRoot(), "docs", "ANCHORS.md");
            var text = File.ReadAllText(path);

            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in Row.Matches(text))
                found[match.Groups["id"].Value] = match.Groups["what"].Value;

            Assert.True(found.Count > 0, "no anchor rows parsed out of " + path
                + " - the table format changed and this check is no longer reading it");
            return found;
        }

        // The test binaries land several folders deep under bin/, and the depth
        // depends on the configuration and target framework. Walking up to the folder
        // that holds docs/ is stable across all of them.
        private static string RepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "docs", "ANCHORS.md")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException(
                "could not find docs/ANCHORS.md above " + AppContext.BaseDirectory);
        }

        // A2 before A10, which string order gets wrong.
        private static (char, int) Order(string id)
        {
            return (id[0], int.Parse(id.Substring(1)));
        }
    }
}
