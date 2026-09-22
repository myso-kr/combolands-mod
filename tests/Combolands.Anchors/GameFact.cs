using Xunit;
using Xunit.Sdk;

namespace Combolands.Anchors
{
    // Skipped, not failed, when there is no game to read.
    //
    // A CI runner does not own a copy of Combolands and never will, so "no game
    // installed" has to be a different colour from "an anchor broke". A test that
    // passed silently instead would be worse than no test: the run would stay green
    // whether the catalogue held or nobody had checked it.
    //
    // The skip reason names the environment variable, because the second question
    // after "why was this skipped" is always "how do I make it not skip".
    public sealed class GameTheoryAttribute : TheoryAttribute
    {
        public GameTheoryAttribute()
        {
            if (Game.Metadata == null) Skip = Game.WhyMissing;
        }
    }

    public sealed class GameFactAttribute : FactAttribute
    {
        public GameFactAttribute()
        {
            if (Game.Metadata == null) Skip = Game.WhyMissing;
        }
    }
}
