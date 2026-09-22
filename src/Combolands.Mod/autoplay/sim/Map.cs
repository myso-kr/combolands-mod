using System;
using System.Collections.Generic;

namespace Combolands.Mod.Autoplay.Sim
{
    // A board the simulator can put a building on and take it off again.
    //
    // The real board is Unity objects that score by mutating each other. This is the
    // same board as data: tiles with a type and an occupant, buildings with a tag and
    // a multiplier. Placing here costs an array write, so a planner can try a
    // thousand boards in the time the game takes to draw one frame.
    internal sealed class Map
    {
        internal readonly int Width;
        internal readonly int Height;

        private readonly int[] _tileType;
        private readonly int[] _occupant;     // index into _buildings, or -1
        private readonly bool[] _blocked;     // CantBuildOn - a rock, the edge of the map

        private readonly List<Placed> _buildings = new List<Placed>();

        internal Map(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("a board with no tiles");

            Width = width;
            Height = height;

            _tileType = new int[width * height];
            _occupant = new int[width * height];
            _blocked = new bool[width * height];

            for (int i = 0; i < _occupant.Length; i++) _occupant[i] = -1;
        }

        internal struct Placed
        {
            public int Tag;
            public int X;
            public int Y;
            public float Multiplier;
        }

        internal int Count { get { return _buildings.Count; } }

        internal Placed At(int index) { return _buildings[index]; }

        internal bool Inside(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        private int Index(int x, int y) { return y * Width + x; }

        // --- tiles ------------------------------------------------------------------

        internal int TileType(int x, int y)
        {
            return Inside(x, y) ? _tileType[Index(x, y)] : -1;
        }

        internal void SetTile(int x, int y, int type, bool blocked = false)
        {
            if (!Inside(x, y)) return;
            _tileType[Index(x, y)] = type;
            _blocked[Index(x, y)] = blocked;
        }

        internal bool IsEmpty(int x, int y)
        {
            return Inside(x, y) && _occupant[Index(x, y)] < 0;
        }

        internal bool IsBuildable(int x, int y)
        {
            return Inside(x, y) && !_blocked[Index(x, y)] && _occupant[Index(x, y)] < 0;
        }

        internal int OccupantAt(int x, int y)
        {
            return Inside(x, y) ? _occupant[Index(x, y)] : -1;
        }

        // --- placing ----------------------------------------------------------------

        // Returns the building's index, so a caller can take it off again. Placement
        // legality is Rules' business, not this one's: a Map will hold whatever it is
        // given, and the planner is what refuses to give it something illegal.
        internal int Place(int tag, int x, int y, float multiplier = 1f)
        {
            if (!Inside(x, y)) throw new ArgumentOutOfRangeException("x", "off the board");
            if (_occupant[Index(x, y)] >= 0) throw new InvalidOperationException("that tile is taken");

            _buildings.Add(new Placed { Tag = tag, X = x, Y = y, Multiplier = multiplier });
            _occupant[Index(x, y)] = _buildings.Count - 1;
            return _buildings.Count - 1;
        }

        // Only the most recent placement, and deliberately so. A planner tries a
        // placement, scores, and undoes it; supporting arbitrary removal would mean
        // keeping the index list stable, which costs more than the one case is worth.
        internal void UndoLastPlacement()
        {
            if (_buildings.Count == 0) throw new InvalidOperationException("nothing to undo");

            var last = _buildings[_buildings.Count - 1];
            _buildings.RemoveAt(_buildings.Count - 1);
            _occupant[Index(last.X, last.Y)] = -1;
        }

        internal void SetMultiplier(int index, float multiplier)
        {
            var building = _buildings[index];
            building.Multiplier = multiplier;
            _buildings[index] = building;
        }

        internal Map Clone()
        {
            var copy = new Map(Width, Height);
            Array.Copy(_tileType, copy._tileType, _tileType.Length);
            Array.Copy(_occupant, copy._occupant, _occupant.Length);
            Array.Copy(_blocked, copy._blocked, _blocked.Length);
            copy._buildings.AddRange(_buildings);
            return copy;
        }

        // --- the two neighbourhoods -------------------------------------------------

        // Eight-neighbourhood, which is what the game means by adjacent -
        // Grid.Get8NeighboursEnumerable is what its adjacency effects walk.
        internal static readonly int[] AdjacentDX = { -1, 0, 1, -1, 1, -1, 0, 1 };
        internal static readonly int[] AdjacentDY = { -1, -1, -1, 0, 0, 1, 1, 1 };

        // The game's filled circle: dx*dx + dy*dy < r*r + r, excluding the centre.
        // Copied rather than called - see docs/ANCHORS.md row P11 - and pinned by
        // tests, because nothing in the game will tell us if it changes.
        internal static bool InRange(int dx, int dy, int range)
        {
            if (dx == 0 && dy == 0) return false;
            return dx * dx + dy * dy < range * range + range;
        }
    }
}
