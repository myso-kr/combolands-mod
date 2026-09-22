using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using HarmonyInstance = HarmonyLib.Harmony;

namespace Combolands.Mod.Autoplay
{
    // Draws the shortlist. Reads, never writes.
    //
    // That is the whole point of stage 1: the helper can be wrong about every tile it
    // highlights and the worst outcome is a player ignoring it. Nothing here calls
    // into the game, so there is no state to corrupt and no save to lose - which is
    // what makes it safe to ship before the valuation is any good.
    //
    // It is drawn in IMGUI rather than by spawning sprites for the same reason. A
    // sprite is a GameObject in the game's scene; a rectangle in OnGUI is not.
    internal static class Overlay
    {
        // Best to worst, interpolated across however many suggestions there are.
        //
        // This used to be five fixed tiers ending at alpha 0.18, which meant the
        // fourth and fifth were effectively invisible - the shortlist said five and
        // the screen showed three. The floor is now high enough that the last entry
        // is a suggestion rather than a rumour.
        private static readonly Color Best = new Color(0.30f, 0.95f, 0.45f);
        private static readonly Color Worst = new Color(0.95f, 0.62f, 0.25f);
        private const float AlphaBest = 0.55f;
        private const float AlphaWorst = 0.30f;

        private static Texture2D _fill;
        private static GUIStyle _rankStyle, _noteStyle;
        private static int _styleTileHeight = -1;

        // One hue per offered building when scouting, so three suggestions for the
        // Hunting Lodge are visibly not three suggestions for the Apothecary.
        private static readonly Color[] OfferHues =
        {
            new Color(0.35f, 0.90f, 0.45f),
            new Color(0.40f, 0.70f, 1.00f),
            new Color(1.00f, 0.72f, 0.30f),
            new Color(0.85f, 0.55f, 0.95f),
        };
        private const string OfferLetters = "ABCD";

        private struct Marked
        {
            public Plan.Candidate Candidate;
            public int Offer;      // -1 when the player is holding the piece
            public int Rank;
        }

        private static List<Marked> _marks = new List<Marked>();
        private static string _scoutLine = "";

        private static List<Plan.Candidate> _shown = new List<Plan.Candidate>();
        private static object _lastPiece;
        private static RectInt _lastWindow;
        private static bool _dirty = true;
        private static int _cooldown;

        internal static bool Enabled;

        internal static void Toggle()
        {
            Enabled = !Enabled;
            if (!Enabled) Clear();
            Log.Info("helper", "placement overlay " + (Enabled ? "on" : "off"));
        }

        internal static void Clear()
        {
            _shown.Clear();
            _marks.Clear();
            _scoutLine = "";
            _lastPiece = null;
            _lastWindow = default(RectInt);
            _dirty = true;
        }

        // The game's own "the board changed" announcement.
        //
        // BuildingExtensions.ResetCaches() is called from seventeen places - a
        // building created or removed, a stat changed, a paint applied, a council
        // vote landing - and it is exactly the set of events that invalidate a
        // shortlist. Polling the building count, which is what this used to do, misses
        // every change that leaves the count alone.
        internal static void Apply(HarmonyInstance harmony)
        {
            var extensions = Anchors.Type("Entities.BuildingExtensions");
            var reset = Anchors.Method(extensions, "ResetCaches");
            if (reset == null)
            {
                Log.Warn("helper", "BuildingExtensions.ResetCaches not found - "
                                 + "suggestions will only refresh when the held building changes");
                return;
            }

            harmony.Patch(reset, postfix: new HarmonyMethod(
                typeof(Overlay).GetMethod(nameof(MarkDirty), BindingFlags.NonPublic | BindingFlags.Static)));
            Log.Info("helper", "board-change hook installed");
        }

        private static void MarkDirty()
        {
            _dirty = true;
        }

        // --- recompute -----------------------------------------------------------

        // Called from OnUpdate. The shortlist is recomputed when the held piece or the
        // building count changes, not every frame: reading 1,188 tiles through
        // reflection is a few milliseconds, which is fine once and a stutter at 60Hz.
        internal static void Refresh()
        {
            if (!Enabled) return;

            var piece = State.PieceBeingPlaced();

            // Nothing in hand: scout the choice bar instead. That is the question a
            // player asks BEFORE picking a card up - which of these three, and where -
            // and it is the one the helper could not answer until now.
            if (piece == null)
            {
                if (Config.HelperScout) Scout();
                else Clear();
                return;
            }

            // Same trap the autoplay loop fell into: the ghost follows the cursor,
            // and off the map the game's own accessors throw rather than return
            // nothing. The helper does not move it back - the player is plainly not
            // placing right now - it just says nothing until they are.
            if (!Exec.OnMap(piece)) { _shown.Clear(); _marks.Clear(); return; }

            // Still rate-limited. ResetCaches can fire several times in one frame
            // during a trigger chain, and recomputing per call would be a stutter for
            // no gain.
            if (_cooldown > 0) { _cooldown--; return; }

            var window = VisibleTiles();
            if (!_dirty && ReferenceEquals(piece, _lastPiece) && window.Equals(_lastWindow)) return;

            _lastPiece = piece;
            _lastWindow = window;
            _dirty = false;
            _cooldown = 6;

            var board = Board.Read(piece, Config.HelperVisibleOnly
                ? new Rect(window.xMin, window.yMin, window.width, window.height)
                : (Rect?)null);
            if (board == null) { _shown.Clear(); return; }

            // Ranked wide, then filtered by the game's own rule, then cut to what is
            // worth looking at. Asking the game about every tile would be thousands of
            // reflection calls; asking about thirty is nothing.
            var ranked = Plan.Best(board, Config.HelperShortlist * 6, Config.HelperSpread);
            var legal = new List<Plan.Candidate>(Config.HelperShortlist);
            foreach (var candidate in ranked)
            {
                if (legal.Count >= Config.HelperShortlist) break;
                if (Board.CanBuildAt(piece, candidate.X, candidate.Y)) legal.Add(candidate);
            }
            _shown = legal;

            _marks.Clear();
            for (int i = 0; i < _shown.Count; i++)
                _marks.Add(new Marked { Candidate = _shown[i], Offer = -1, Rank = i });
            _scoutLine = "";
        }

        // --- scouting ------------------------------------------------------------

        private static int _lastOfferSignature = int.MinValue;

        private static void Scout()
        {
            if (_cooldown > 0) { _cooldown--; return; }

            var offers = Offers.Current();
            if (offers.Count == 0) { _marks.Clear(); _shown.Clear(); _scoutLine = ""; return; }

            var window = VisibleTiles();
            int signature = Signature(offers, window);
            if (!_dirty && signature == _lastOfferSignature) return;

            _lastOfferSignature = signature;
            _lastPiece = null;
            _dirty = false;
            _cooldown = 6;

            var rect = Config.HelperVisibleOnly
                ? new Rect(window.xMin, window.yMin, window.width, window.height)
                : (Rect?)null;

            var marks = new List<Marked>();
            var summary = new System.Text.StringBuilder();

            for (int i = 0; i < offers.Count && i < OfferHues.Length; i++)
            {
                var board = Board.ReadFor(offers[i], rect);
                if (board == null) continue;

                var picks = Plan.Best(board, Config.HelperOfferPicks, Config.HelperSpread);
                for (int r = 0; r < picks.Count; r++)
                    marks.Add(new Marked { Candidate = picks[r], Offer = i, Rank = r });

                if (summary.Length > 0) summary.Append("    ");
                summary.Append(OfferLetters[i]);
                summary.Append(" ");
                summary.Append(offers[i].Name);
                summary.Append(picks.Count > 0
                    ? string.Format(" -> ({0},{1}) x{2:0.#}", picks[0].X, picks[0].Y, picks[0].Score.TargetHits)
                    : " -> nowhere legal in view");
            }

            _marks = marks;
            _shown.Clear();
            _scoutLine = summary.ToString();
        }

        // Cheap "has anything changed" over the offers plus the view.
        private static int Signature(List<Offer> offers, RectInt window)
        {
            int hash = 17;
            for (int i = 0; i < offers.Count; i++) hash = hash * 31 + offers[i].Tag;
            hash = hash * 31 + window.xMin;
            hash = hash * 31 + window.yMin;
            hash = hash * 31 + window.width;
            hash = hash * 31 + window.height;
            return hash;
        }

        // Which tiles the camera can actually show, in tile coordinates.
        //
        // A suggestion off the edge of the screen is invisible - highlights are drawn
        // in world space - so the shortlist just looks short. One tile of slack each
        // way keeps a suggestion from vanishing the instant the view nudges.
        private static RectInt VisibleTiles()
        {
            var camera = Camera.main;
            if (camera == null) return new RectInt(0, 0, 9999, 9999);

            var bottomLeft = camera.ScreenToWorldPoint(Vector3.zero);
            var topRight = camera.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, 0f));

            int minX = Mathf.FloorToInt(bottomLeft.x) - 1;
            int minY = Mathf.FloorToInt(bottomLeft.y) - 1;
            int maxX = Mathf.CeilToInt(topRight.x) + 1;
            int maxY = Mathf.CeilToInt(topRight.y) + 1;
            return new RectInt(minX, minY, maxX - minX, maxY - minY);
        }

        // --- draw ----------------------------------------------------------------

        internal static void Draw()
        {
            if (!Enabled || _marks.Count == 0) return;
            if (Event.current.type != EventType.Repaint) return;

            var camera = Camera.main;
            if (camera == null) return;

            // One tile in screen pixels, measured rather than assumed - the camera is
            // orthographic but its size changes with the window.
            var origin = camera.WorldToScreenPoint(Vector3.zero);
            var oneOver = camera.WorldToScreenPoint(new Vector3(1f, 1f, 0f));
            float tileW = Mathf.Abs(oneOver.x - origin.x);
            float tileH = Mathf.Abs(oneOver.y - origin.y);
            if (tileW < 1f || tileH < 1f) return;

            // After the measurement: the label size follows the tile size, so zooming
            // has to rebuild the styles.
            EnsureStyles(Mathf.RoundToInt(tileH));

            for (int i = 0; i < _marks.Count; i++)
            {
                var mark = _marks[i];
                var world = camera.WorldToScreenPoint(
                    new Vector3(mark.Candidate.X, mark.Candidate.Y, 0f));
                if (world.z < 0f) continue;

                var rect = new Rect(world.x - tileW * 0.5f,
                                    Screen.height - world.y - tileH * 0.5f,
                                    tileW, tileH);

                GUI.color = mark.Offer < 0
                    ? Tint(mark.Rank, _marks.Count)
                    : OfferTint(mark.Offer, mark.Rank);
                GUI.DrawTexture(rect, _fill);
                GUI.color = Color.white;

                if (Config.HelperLabels) Label(rect, mark);
            }
        }

        // "#2" is a rank; "x3" is how many things this building wants within reach.
        //
        // Both carry a marker rather than being left as bare numbers, and EVERY
        // suggestion is labelled now - not just the first three. An unlabelled
        // highlight is one whose standing the player has to guess.
        private static void Label(Rect tile, Marked mark)
        {
            var candidate = mark.Candidate;

            // Simulated tiles carry real points, so say points. "x3 targets" was the
            // best a proximity valuation could offer; "+840/wk" is what the tile is
            // actually worth and is checkable against the game's own display.
            bool hasTargets = candidate.Score.Simulated
                ? System.Math.Abs(candidate.Score.PerWeek) >= 1
                : candidate.Score.TargetHits >= 0.05f;
            string head = mark.Offer < 0
                ? "#" + (mark.Rank + 1)
                : OfferLetters[mark.Offer] + (mark.Rank + 1).ToString();

            var rank = hasTargets
                ? new Rect(tile.x, tile.y - tile.height * 0.10f, tile.width, tile.height * 0.70f)
                : tile;
            Shadowed(rank, head, _rankStyle);

            if (!hasTargets) return;

            var note = new Rect(tile.x, tile.y + tile.height * 0.42f, tile.width, tile.height * 0.52f);
            Shadowed(note, candidate.Score.Simulated
                ? Points(candidate.Score.PerWeek)
                : "x" + candidate.Score.TargetHits.ToString("0.#"), _noteStyle);
        }

        // Thousands are what this game deals in by the third milestone, and five
        // digits on a tile the size of a thumbnail is unreadable.
        private static string Points(double points)
        {
            var sign = points < 0 ? "-" : "+";
            var size = System.Math.Abs(points);

            if (size >= 1000000) return sign + (size / 1000000).ToString("0.#") + "M";
            if (size >= 1000) return sign + (size / 1000).ToString("0.#") + "k";
            return sign + size.ToString("0");
        }

        // Tiles sit on grass, sand, ocean and forest, so white alone is illegible
        // about a third of the time. One dark offset copy is cheaper than an outline
        // shader and enough.
        private static void Shadowed(Rect rect, string text, GUIStyle style)
        {
            var shadow = new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(shadow, text, style);
            GUI.color = Color.white;
            GUI.Label(rect, text, style);
        }

        private static Color OfferTint(int offer, int rank)
        {
            var colour = OfferHues[offer % OfferHues.Length];
            colour.a = Mathf.Lerp(AlphaBest, AlphaWorst, rank / 3f);
            return colour;
        }

        private static Color Tint(int index, int count)
        {
            float t = count <= 1 ? 0f : (float)index / (count - 1);
            var colour = Color.Lerp(Best, Worst, t);
            colour.a = Mathf.Lerp(AlphaBest, AlphaWorst, t);
            return colour;
        }

        private static void EnsureStyles(int tileHeight)
        {
            if (_fill == null)
            {
                _fill = new Texture2D(1, 1);
                _fill.SetPixel(0, 0, Color.white);
                _fill.Apply();
                Object.DontDestroyOnLoad(_fill);
            }

            // Rebuilt when the tile size changes, which is what zooming does. A fixed
            // point size is unreadable at one zoom and covers the tile at another.
            if (_rankStyle != null && _styleTileHeight == tileHeight) return;
            _styleTileHeight = tileHeight;

            _rankStyle = new GUIStyle(GUI.skin.label);
            _rankStyle.alignment = TextAnchor.MiddleCenter;
            _rankStyle.fontStyle = FontStyle.Bold;
            _rankStyle.normal.textColor = Color.white;
            _rankStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(tileHeight * 0.50f), 9, 40);

            _noteStyle = new GUIStyle(_rankStyle);
            _noteStyle.fontStyle = FontStyle.Normal;
            _noteStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(tileHeight * 0.34f), 8, 26);
        }

        // The one line that says what the marks on the map mean.
        internal const string Legend =
            "#n = rank for the building in hand.   An = rank for offered building A.   "
          + "xN = how many things it wants within reach.";

        // What the panel shows about the current shortlist.
        internal static string Summary()
        {
            if (!Enabled) return "off";
            if (Log.HasFailed("helper.refresh"))
                return string.Format("{0} error(s) so far - see MelonLoader/Latest.log",
                    Log.FailureCount("helper.refresh"));
            if (_scoutLine.Length > 0) return "scouting:  " + _scoutLine;
            if (_shown.Count == 0) return "on - nothing to suggest (hold a building)";

            var best = _shown[0];

            if (best.Score.Simulated)
                return string.Format(
                    "{0} suggestions.  #1 at ({1},{2}):  {3} a week, {4} over the milestone.  [simulated]",
                    _shown.Count, best.X, best.Y,
                    Points(best.Score.PerWeek), Points(best.Score.OverMilestone));

            return string.Format(
                "{0} suggestions.  #1 at ({1},{2}):  x{3:0.#} targets,  {4} adjacent,  {5} shared,  room {6}"
                + "  [estimated - no rule set dumped]",
                _shown.Count, best.X, best.Y, best.Score.TargetHits,
                best.Score.Adjacent, best.Score.Shared, best.Score.Room);
        }
    }
}
