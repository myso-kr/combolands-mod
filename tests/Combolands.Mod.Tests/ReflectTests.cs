using Combolands.Mod;
using Xunit;

namespace Combolands.Mod.Tests
{
    // The regression these pin actually happened, and it was expensive to notice:
    // the placement helper stopped highlighting anything, and kept not highlighting
    // anything, because a reflection lookup threw inside a Guard and the module went
    // quiet for the rest of the session.
    //
    // The game shadows members with `new` - Entities.Building redeclares GamePiece's
    // Behaviour with a narrower return type - and Type.GetProperty with
    // FlattenHierarchy throws AmbiguousMatchException when it finds both.
    public class ReflectTests
    {
        private class Base
        {
            public virtual object Behaviour { get { return null; } }
            public int Shared;
            public virtual int Describe(object a) { return 1; }
            protected string Hidden { get { return "base"; } }
        }

        private class Derived : Base
        {
            // The shape that broke it.
            public new string Behaviour { get { return "derived"; } }
            public new int Shared;
            public new int Describe(object a) { return 2; }
            protected new string Hidden { get { return "derived"; } }
        }

        [Fact]
        public void AShadowedPropertyResolvesToTheDerivedOneInsteadOfThrowing()
        {
            var found = Reflect.Property(typeof(Derived), "Behaviour");

            Assert.NotNull(found);
            Assert.Equal(typeof(Derived), found.DeclaringType);
            Assert.Equal(typeof(string), found.PropertyType);
        }

        [Fact]
        public void AShadowedFieldResolvesToTheDerivedOne()
        {
            var found = Reflect.Field(typeof(Derived), "Shared");

            Assert.NotNull(found);
            Assert.Equal(typeof(Derived), found.DeclaringType);
        }

        [Fact]
        public void AShadowedMethodResolvesToTheDerivedOne()
        {
            bool ambiguous;
            var found = Reflect.MethodByName(typeof(Derived), "Describe", 1, out ambiguous);

            Assert.False(ambiguous);
            Assert.NotNull(found);
            Assert.Equal(typeof(Derived), found.DeclaringType);
        }

        [Fact]
        public void NonPublicShadowedMembersResolveToo()
        {
            // Every anchor this mod reads that is private lives on some derived type,
            // so the walk has to see non-public declarations at each level.
            var found = Reflect.Property(typeof(Derived), "Hidden");

            Assert.NotNull(found);
            Assert.Equal(typeof(Derived), found.DeclaringType);
        }

        [Fact]
        public void InheritedMembersAreStillFoundWhenNotShadowed()
        {
            var found = Reflect.Field(typeof(Derived), "OnlyOnBase");
            Assert.Null(found);   // there is no such member at all

            var inherited = Reflect.Property(typeof(Inheritor), "Behaviour");
            Assert.NotNull(inherited);
            Assert.Equal(typeof(Base), inherited.DeclaringType);
        }

        private class Inheritor : Base { }

        [Fact]
        public void AMissingMemberIsNullRatherThanAThrow()
        {
            bool ambiguous;
            Assert.Null(Reflect.Property(typeof(Derived), "NoSuchThing"));
            Assert.Null(Reflect.Field(typeof(Derived), "NoSuchThing"));
            Assert.Null(Reflect.Method(typeof(Derived), "NoSuchThing", null));
            Assert.Null(Reflect.MethodByName(typeof(Derived), "NoSuchThing", 0, out ambiguous));
            Assert.False(ambiguous);
        }

        private class Overloaded
        {
            public int Two(int a, int b) { return 0; }
            public int Two(string a, string b) { return 0; }
        }

        [Fact]
        public void TwoOverloadsOfTheSameArityAreReportedRatherThanGuessedAt()
        {
            bool ambiguous;
            var found = Reflect.MethodByName(typeof(Overloaded), "Two", 2, out ambiguous);

            Assert.Null(found);
            Assert.True(ambiguous);
        }

        [Fact]
        public void SpellingOutTheArgumentTypesPicksTheRightOverload()
        {
            var found = Reflect.Method(typeof(Overloaded), "Two", new[] { typeof(int), typeof(int) });

            Assert.NotNull(found);
            Assert.Equal(typeof(int), found.GetParameters()[0].ParameterType);
        }
    }
}
