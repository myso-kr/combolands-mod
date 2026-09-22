using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Combolands.Anchors
{
    // One test case per anchor, named by its id, so a game update produces "P17
    // failed" rather than "the mod stopped working".
    public class AnchorTests
    {
        private readonly ITestOutputHelper _out;

        public AnchorTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> Checkable
        {
            get { return Catalogue.All.Where(a => a.Bound).Select(a => new object[] { a.Id }); }
        }

        [GameTheory]
        [MemberData(nameof(Checkable))]
        public void Anchor_still_resolves(string id)
        {
            var anchor = Catalogue.All.Single(a => a.Id == id);
            var missing = anchor.Check();

            if (missing.Count == 0)
            {
                _out.WriteLine(anchor + ": " + Describe(anchor));
                return;
            }

            Assert.Fail(anchor + " no longer resolves against "
                + Game.ManagedFolder + ":\n  " + string.Join("\n  ", missing)
                + "\n\nSee docs/ANCHORS.md row " + anchor.Id + " for what this breaks and where to fix it.");
        }

        private static string Describe(Anchor anchor)
        {
            if (anchor.TypeOnly) return anchor.Type + " exists";
            return anchor.Type + " - " + string.Join(", ", anchor.Members.Select(m => m.ToString()));
        }

        // P1 continued. MapController.Grid returns a constructed generic whose name
        // the mod never spells out - it takes the property type and asks that. The
        // check follows the same chain, because a Grid that renamed Width would break
        // autoplay just as thoroughly as a MapController that renamed Grid.
        [GameFact]
        public void The_grid_still_answers()
        {
            var map = Game.Find("Assembly-CSharp", "Environment.MapController");
            Assert.True(map != null, "Assembly-CSharp has no Environment.MapController");

            var grid = Combolands.Mod.Reflect.Property(map, "Grid");
            Assert.True(grid != null, "MapController.Grid is gone - see docs/ANCHORS.md row P1");

            var type = grid.PropertyType;
            Assert.True(Combolands.Mod.Reflect.Property(type, "Width") != null,
                type.Name + ".Width is gone - see docs/ANCHORS.md row P1");
            Assert.True(Combolands.Mod.Reflect.Property(type, "Height") != null,
                type.Name + ".Height is gone - see docs/ANCHORS.md row P1");

            // By parameter type, not by arity: Grid has three GetTile overloads and
            // two of them take two arguments. Board.cs disambiguates the same way,
            // and a check that did not would pass on the float one.
            var int32 = Game.Core("System.Int32");
            Assert.True(int32 != null, "the game mscorlib has no System.Int32");

            Assert.True(Combolands.Mod.Reflect.Method(type, "GetTile", new[] { int32, int32 }) != null,
                type.Name + ".GetTile(int, int) is gone - see docs/ANCHORS.md row P1");
        }

        // The catalogue names two assemblies, and they fail for different reasons: the
        // Assembly-CSharp ones when Crux changes the game, the TextMeshPro ones when
        // Unity is upgraded. Losing either one whole is worth its own line rather than
        // forty identical failures.
        [GameFact]
        public void Both_assemblies_load()
        {
            Assert.True(Game.Load("Assembly-CSharp") != null,
                "Assembly-CSharp.dll did not load from " + Game.ManagedFolder);
            Assert.True(Game.Load("Unity.TextMeshPro") != null,
                "Unity.TextMeshPro.dll did not load from " + Game.ManagedFolder
                + " - the font anchors move with the Unity version, not the game");
        }
    }
}
