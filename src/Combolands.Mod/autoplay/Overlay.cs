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
        private static readonly Color[] Tiers =
        {
            new Color(0.30f, 0.95f, 0.45f, 0.55f),   // best
            new Color(0.55f, 0.90f, 0.40f, 0.42f),
            new Color(0.80f, 0.85f, 0.35f, 0.32f),
            new Color(0.90f, 0.70f, 0.30f, 0.24f),
            new Color(0.90f, 0.55f, 0.30f, 0.18f),
        };

        private static Texture2D _fill;
        private static GUIStyle _rank;

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
            var ranked = Plan.Best(board, Config.HelperShortlist * 6);
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

            EnsureStyles();

            // One tile in screen pixels, measured rather than assumed - the camera is
            // orthographic but its size changes with the window.
            var origin = camera.WorldToScreenPoint(Vector3.zero);
            var oneOver = camera.WorldToScreenPoint(new Vector3(1f, 1f, 0f));
            float tileW = Mathf.Abs(oneOver.x - origin.x);
            float tileH = Mathf.Abs(oneOver.y - origin.y);
            if (tileW < 1f || tileH < 1f) return;

            for (int i = 0; i < _shown.Count; i++)
            {
                var candidate = _shown[i];
                var world = camera.WorldToScreenPoint(new Vector3(candidate.X, candidate.Y, 0f));
                if (world.z < 0f) continue;

                var rect = new Rect(world.x - tileW * 0.5f,
                                    Screen.height - world.y - tileH * 0.5f,
                                    tileW, tileH);

                GUI.color = Tiers[Mathf.Min(i, Tiers.Length - 1)];
                GUI.DrawTexture(rect, _fill);
                GUI.color = Color.white;

                if (i < 3) GUI.Label(rect, (i + 1).ToString(), _rank);
            }
        }

        private static void EnsureStyles()
        {
            if (_fill == null)
            {
                _fill = new Texture2D(1, 1);
                _fill.SetPixel(0, 0, Color.white);
                _fill.Apply();
                Object.DontDestroyOnLoad(_fill);
            }
            if (_rank == null)
            {
                _rank = new GUIStyle(GUI.skin.label);
                _rank.alignment = TextAnchor.MiddleCenter;
                _rank.fontStyle = FontStyle.Bold;
                _rank.normal.textColor = Color.white;
            }
        }

        // What the panel shows about the current shortlist.
        internal static string Summary()
        {
            if (!Enabled) return "off";
            if (_shown.Count == 0) return "on - nothing to suggest (hold a building)";

            var best = _shown[0];
            return string.Format("on - best ({0},{1})  reach {2}  reached-by {3}  adjacent {4}  shared {5}",
                best.X, best.Y, best.Score.Covers, best.Score.CoveredBy,
                best.Score.Adjacent, best.Score.Shared);
        }
    }
}
