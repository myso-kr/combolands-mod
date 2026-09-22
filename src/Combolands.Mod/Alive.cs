using UnityEngine;

namespace Combolands.Mod
{
    // Is this thing still real?
    //
    // Unity overloads == on UnityEngine.Object so a destroyed object compares equal
    // to null. That overload is picked by the STATIC type, and everything this mod
    // gets back from reflection is typed `object` - so `building == null` is a plain
    // reference comparison, which a destroyed object sails through. The next property
    // read then throws, because the native half is gone.
    //
    // It showed up exactly where it would. The game destroys a placed building's
    // ghost with `Object.Destroy(go, 0.01f)`, so for a few frames after every
    // placement BuildingController.Buildings holds something that is neither null nor
    // there. Both the helper and the autoplay loop threw on it.
    internal static class Alive
    {
        internal static bool Is(object candidate)
        {
            if (candidate == null) return false;

            // Not a Unity object at all - an ordinary reference, and it exists.
            if (!(candidate is Object)) return true;

            // The cast makes the static type UnityEngine.Object, which is what puts
            // Unity's overload in play. This is the whole point of the file.
            return (Object)candidate != null;
        }
    }
}
