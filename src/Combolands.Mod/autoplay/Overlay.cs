using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

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

        private static List<Plan.Candidate> _shown = new List<Plan.Candidate>();
        private static object _lastPiece;
        private static int _lastBuildingCount = -1;
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
            _lastPiece = null;
            _lastBuildingCount = -1;
        }

        // --- recompute -----------------------------------------------------------

        // Called from OnUpdate. The shortlist is recomputed when the held piece or the
        // building count changes, not every frame: reading 1,188 tiles through
        // reflection is a few milliseconds, which is fine once and a stutter at 60Hz.
        internal static void Refresh()
        {
            if (!Enabled) return;

            var piece = State.PieceBeingPlaced();
            if (piece == null) { Clear(); return; }

            if (_cooldown > 0) { _cooldown--; return; }

            var count = CountBuildings();
            if (ReferenceEquals(piece, _lastPiece) && count == _lastBuildingCount) return;

            _lastPiece = piece;
            _lastBuildingCount = count;
            _cooldown = 10;

            var board = Board.Read(piece);
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
        }

        private static PropertyInfo _buildingsProperty;

        private static int CountBuildings()
        {
            var controller = Singletons.Get("Entities.BuildingController");
            if (controller == null) return -1;

            if (_buildingsProperty == null)
                _buildingsProperty = Anchors.Property(controller.GetType(), "Buildings");
            if (_buildingsProperty == null) return -1;

            var list = _buildingsProperty.GetValue(controller, null) as System.Collections.ICollection;
            return list == null ? -1 : list.Count;
        }

        // --- draw ----------------------------------------------------------------

        internal static void Draw()
        {
            if (!Enabled || _shown.Count == 0) return;
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

            for (int i = 0; i < _shown.Count; i++)
            {
                var candidate = _shown[i];
                var world = camera.WorldToScreenPoint(new Vector3(candidate.X, candidate.Y, 0f));
                if (world.z < 0f) continue;

                var rect = new Rect(world.x - tileW * 0.5f,
                                    Screen.height - world.y - tileH * 0.5f,
                                    tileW, tileH);

                GUI.color = Tint(i, _shown.Count);
                GUI.DrawTexture(rect, _fill);
                GUI.color = Color.white;

                if (Config.HelperLabels) Label(rect, i, candidate);
            }
        }

        // "#2" is a rank; "x3" is how many things this building wants within reach.
        //
        // Both carry a marker rather than being left as bare numbers, and EVERY
        // suggestion is labelled now - not just the first three. An unlabelled
        // highlight is one whose standing the player has to guess.
        private static void Label(Rect tile, int index, Plan.Candidate candidate)
        {
            bool hasTargets = candidate.Score.TargetHits >= 0.05f;

            var rank = hasTargets
                ? new Rect(tile.x, tile.y - tile.height * 0.10f, tile.width, tile.height * 0.70f)
                : tile;
            Shadowed(rank, "#" + (index + 1), _rankStyle);

            if (!hasTargets) return;

            var note = new Rect(tile.x, tile.y + tile.height * 0.42f, tile.width, tile.height * 0.52f);
            Shadowed(note, "x" + candidate.Score.TargetHits.ToString("0.#"), _noteStyle);
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
            "#n = rank.   xN = how many things this building wants within reach.";

        // What the panel shows about the current shortlist.
        internal static string Summary()
        {
            if (!Enabled) return "off";
            if (Log.HasFailed("helper.refresh"))
                return "STOPPED after an error - see MelonLoader/Latest.log";
            if (_shown.Count == 0) return "on - nothing to suggest (hold a building)";

            var best = _shown[0];
            return string.Format(
                "{0} suggestions.  #1 at ({1},{2}):  x{3:0.#} targets,  {4} adjacent,  {5} shared,  room {6}",
                _shown.Count, best.X, best.Y, best.Score.TargetHits,
                best.Score.Adjacent, best.Score.Shared, best.Score.Room);
        }
    }
}
