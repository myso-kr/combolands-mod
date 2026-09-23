using System;
using System.Collections.Generic;

namespace Combolands.Mod.Autoplay.Sim
{
    // What the game knows about a building, in a form that needs no game.
    //
    // Every field here was read off `_BuildingBehaviour` once, by the dumper, and
    // written to `generated/rules.json`. That file is not in the repository - it is
    // derived from the game, like `generated/strings.en.json`, and anyone with a copy
    // of the game can make it again. What IS committed is what the trainer produces
    // from it, which is ours.
    //
    // The shape follows `GetScorePreview` exactly, because reproducing that function
    // is the whole point. Anything it does not read is not here.
    internal sealed class Piece
    {
        public int Tag;
        public string Name = "";
        public int[] Categories = Empty;
        public int Rarity;
        public int Range;

        // Turns between payouts. `CountIsReadyToActivate` counts down and only pays
        // on zero, so a cooldown-3 building earns its score every third week - and a
        // planner that treated it as weekly income would overstate it threefold.
        // Zero and one both mean "every week".
        public int Cooldown = 1;

        // True when `reach` was not declared by the building and was inferred from it
        // having a range and targets. Woodcutter is the plain case: it scores, but it
        // offers the game no preview because it pays on a cooldown. Modelled, and
        // marked, rather than silently dropped or silently invented.
        public bool ReachInferred;

        // --- activation -------------------------------------------------------------
        //
        // Eighteen buildings in the game activate their neighbours, and an activated
        // building scores again out of turn, ignoring its own cooldown. That is the
        // trigger cascade the plan document once said could not be evaluated without
        // committing to it - and most of it can, because the shape is always the
        // same: collect what is activatable in reach, drop yourself and whoever
        // activated you, then pick `ActivationCount` of them AT RANDOM.
        //
        // Random selection is why this is an expectation rather than a simulation. A
        // candidate is chosen with probability min(count, candidates) / candidates,
        // and the expected extra score is that probability times what it scores. Over
        // a ten-week milestone an expectation is the right quantity; a single sampled
        // outcome would be noise dressed as precision.

        // How many neighbours it activates each time it fires. Zero for the great
        // majority, which is what makes this cheap.
        public int ActivationCount;

        // Whether it can BE activated. An activator skips anything that says no.
        public bool CanBeActivated;

        // Whether it activates others. Not derivable from any field - the game says
        // it by calling AddOnActivatedTrigger in code - so it comes from a list the
        // dumper carries. See docs/ANCHORS.md row M7.
        public bool Activates;

        // Adjacent, InRange or SelfOnly - and it is ONE of them, not a mixture. This
        // is the field the old heuristic did not have: it counted a target that was
        // in range OR adjacent, for every building, which overstated every
        // adjacency-scoring building by the whole of its range.
        public Reach Reach = Reach.None;

        // What it pays for what. Each map is target -> points, exactly as
        // GetScoreForTag / GetTargetCategoryScoreByCategory / GetScoreForRarity /
        // GetScoreForTileType answer.
        public Dictionary<int, int> TagScores = new Dictionary<int, int>();
        public Dictionary<int, int> CategoryScores = new Dictionary<int, int>();
        public Dictionary<int, int> RarityScores = new Dictionary<int, int>();
        public Dictionary<int, int> TileTypeScores = new Dictionary<int, int>();

        // GetScoreParam(origin): what it scores for occupying its own tile, which is
        // what a SelfOnly building lives on and what any building adds for itself.
        public int SelfScore;

        // Where it may be built. Empty means the dump found no restriction.
        public int[] TileTypes = Empty;

        // Whether two of these may sit in each other's range.
        public bool SameTypeRestricted;

        // How faithfully this one is simulated. A behaviour that overrides
        // GetScorePreview, or whose end-of-turn does something the base class does
        // not, is marked here rather than quietly modelled wrong - see Fidelity.
        public Fidelity Fidelity = Fidelity.Exact;

        internal static readonly int[] Empty = new int[0];

        public bool HasCategory(int category)
        {
            for (int i = 0; i < Categories.Length; i++)
                if (Categories[i] == category) return true;
            return false;
        }

        public override string ToString()
        {
            return Name.Length > 0 ? Name : "tag " + Tag;
        }
    }

    // ScorePreviewMode, by the name the game gives it.
    internal enum Reach
    {
        None = 0,
        InRange = 1,
        Adjacent = 2,
        SelfOnly = 3,
    }

    // How much of this building the simulator actually reproduces.
    //
    // Saying so is the difference between a simulator and a guess wearing a
    // simulator's clothes. A plan built mostly on Exact pieces is worth trusting; one
    // built on Approximate pieces is worth a warning, and the planner can say which
    // it had.
    internal enum Fidelity
    {
        // The base GetScorePreview covers it: targets in reach, scored once per tile.
        Exact = 0,

        // Its behaviour overrides GetScorePreview, or its end-of-turn does something
        // besides score - it transforms, spawns, activates another building. The
        // static score is still computed and is still a lower bound; what it triggers
        // is not modelled.
        Approximate = 1,

        // Nothing useful was read. Treated as scoring nothing, which is the safe
        // direction: the planner will under-rate it rather than build a milestone
        // plan around a building it does not understand.
        Unknown = 2,
    }

    // The whole dumped rule set.
    internal sealed class Rules
    {
        public string Game = "";
        public int BuildId;
        public DateTime Dumped;

        public readonly Dictionary<int, Piece> Pieces = new Dictionary<int, Piece>();

        // Category and tile-type names, for messages only. The simulator works in
        // numbers; a person reading a plan needs the words.
        public readonly Dictionary<int, string> CategoryNames = new Dictionary<int, string>();
        public readonly Dictionary<int, string> TileTypeNames = new Dictionary<int, string>();

        public Piece Get(int tag)
        {
            Piece found;
            return Pieces.TryGetValue(tag, out found) ? found : null;
        }

        public int Count { get { return Pieces.Count; } }

        // How much of the set is reproduced exactly. Published rather than inferred,
        // because a number like "84% exact" is the one fact that says how far a
        // simulated plan can be trusted.
        public int CountOf(Fidelity fidelity)
        {
            int n = 0;
            foreach (var piece in Pieces.Values)
                if (piece.Fidelity == fidelity) n++;
            return n;
        }

        // --- reading the dump ------------------------------------------------------

        internal static Rules Parse(string json)
        {
            var document = JsonValue.Parse(json);
            var rules = new Rules();

            var meta = document["meta"];
            if (meta != null)
            {
                rules.Game = Text(meta["game"]);
                rules.BuildId = meta["buildid"] == null ? 0 : meta["buildid"].AsInt();

                DateTime when;
                if (DateTime.TryParse(Text(meta["dumped"]), out when)) rules.Dumped = when;
            }

            Names(document["categories"], rules.CategoryNames);
            Names(document["tileTypes"], rules.TileTypeNames);

            var pieces = document["pieces"];
            if (pieces == null || pieces.Type != JsonValue.Kind.Array)
                throw new FormatException("the rule set has no \"pieces\" array");

            foreach (var entry in pieces.Items)
            {
                var piece = ReadPiece(entry);
                if (rules.Pieces.ContainsKey(piece.Tag))
                    throw new FormatException("tag " + piece.Tag + " appears twice in the rule set");
                rules.Pieces[piece.Tag] = piece;
            }

            if (rules.Pieces.Count == 0)
                throw new FormatException("the rule set is empty");

            return rules;
        }

        private static Piece ReadPiece(JsonValue entry)
        {
            var piece = new Piece
            {
                Tag = Required(entry, "tag").AsInt(),
                Name = Text(entry["name"]),
                Rarity = Number(entry["rarity"]),
                Range = Number(entry["range"]),
                Cooldown = Math.Max(1, Number(entry["cooldown"])),
                ReachInferred = entry["reachInferred"] != null && entry["reachInferred"].AsBool(),
                ActivationCount = Number(entry["activationCount"]),
                CanBeActivated = entry["canBeActivated"] != null && entry["canBeActivated"].AsBool(),
                Activates = entry["activates"] != null && entry["activates"].AsBool(),
                SelfScore = Number(entry["selfScore"]),
                SameTypeRestricted = entry["sameTypeRestricted"] != null
                                  && entry["sameTypeRestricted"].AsBool(),
                Categories = Ints(entry["categories"]),
                TileTypes = Ints(entry["tileTypes"]),
            };

            var reach = Text(entry["reach"]);
            switch (reach)
            {
                case "InRange":  piece.Reach = Reach.InRange;  break;
                case "Adjacent": piece.Reach = Reach.Adjacent; break;
                case "SelfOnly": piece.Reach = Reach.SelfOnly; break;
                case "None": case "": piece.Reach = Reach.None; break;
                default: throw new FormatException("tag " + piece.Tag + " has an unknown reach \"" + reach + "\"");
            }

            var fidelity = Text(entry["fidelity"]);
            switch (fidelity)
            {
                case "exact": case "": piece.Fidelity = Fidelity.Exact; break;
                case "approximate":    piece.Fidelity = Fidelity.Approximate; break;
                case "unknown":        piece.Fidelity = Fidelity.Unknown; break;
                default: throw new FormatException("tag " + piece.Tag + " has an unknown fidelity \"" + fidelity + "\"");
            }

            Scores(entry["tagScores"], piece.TagScores);
            Scores(entry["categoryScores"], piece.CategoryScores);
            Scores(entry["rarityScores"], piece.RarityScores);
            Scores(entry["tileTypeScores"], piece.TileTypeScores);

            return piece;
        }

        private static JsonValue Required(JsonValue owner, string field)
        {
            var found = owner == null ? null : owner[field];
            if (found == null) throw new FormatException("a piece with no \"" + field + "\"");
            return found;
        }

        private static string Text(JsonValue value)
        {
            return value == null ? "" : value.AsString("");
        }

        private static int Number(JsonValue value)
        {
            return value == null ? 0 : value.AsInt();
        }

        private static int[] Ints(JsonValue value)
        {
            if (value == null || value.Type != JsonValue.Kind.Array) return Piece.Empty;

            var list = new List<int>(value.Count);
            foreach (var item in value.Items) list.Add(item.AsInt());
            return list.ToArray();
        }

        // Keys are numbers written as strings, because JSON object keys always are.
        private static void Scores(JsonValue value, Dictionary<int, int> into)
        {
            if (value == null || value.Type != JsonValue.Kind.Object) return;

            foreach (var pair in value.Fields)
            {
                int key;
                if (!int.TryParse(pair.Key, out key))
                    throw new FormatException("\"" + pair.Key + "\" is not a numeric key");
                into[key] = pair.Value.AsInt();
            }
        }

        private static void Names(JsonValue value, Dictionary<int, string> into)
        {
            if (value == null || value.Type != JsonValue.Kind.Object) return;

            foreach (var pair in value.Fields)
            {
                int key;
                if (int.TryParse(pair.Key, out key)) into[key] = pair.Value.AsString("");
            }
        }
    }
}
