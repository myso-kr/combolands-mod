using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Combolands.Mod.Autoplay
{
    // Whether to go in, what to buy, and when to leave.
    //
    // Skipping is not the lazy option - the game pays a skip reward for it - so the
    // decision is real: gold is only worth spending if there is something worth
    // buying, and with an empty purse the reward is strictly better.
    //
    // Everything a shop sells is bought by clicking it, and ShopItem.OnPointerClick
    // does the whole transaction: it checks CanPurchase, deducts the money and marks
    // the item sold. So this decides, and the game transacts.
    internal static class Shop
    {
        private const string Panel = "UI.ShopPanel";
        private const string Interaction = "Interaction.InteractionController";
        private const string Score = "GameState.ScoreController";

        internal static string Last = "";

        internal static bool Act()
        {
            if (!InShop()) return false;

            var panel = Singletons.Get(Panel);
            if (panel == null) return false;

            var exterior = Probe<object>(panel, "Exterior");
            if (Alive.Is(exterior) && Probe<bool>(exterior, "IsShown"))
                return EnterOrSkip(exterior);

            if (BuySomething()) return true;
            return Leave(panel);
        }

        // Only while the game says we are shopping. ShopItem is also what a pack's
        // options are made of, and clicking one of those as though it were for sale
        // would spend a pick on whatever happened to be first.
        private static bool InShop()
        {
            var controller = Singletons.Get(Interaction);
            if (controller == null) return false;

            var current = Reflect.Property(controller.GetType(), "CurrentInteractionState");
            var shopping = Reflect.Property(controller.GetType(), "Shopping");
            if (current == null || shopping == null) return false;

            var state = current.GetValue(controller, null);
            return state != null && ReferenceEquals(state, shopping.GetValue(controller, null));
        }

        // --- the door -------------------------------------------------------------

        private static bool EnterOrSkip(object exterior)
        {
            // SkipShop's own precondition, which it does not check: its first line
            // dereferences the skip reward and its last nulls it, while IsShown is
            // cleared later by a coroutine. Without this the second call throws.
            var hasReward = Alive.Is(Probe<object>(exterior, "_currentSkipReward"));

            if (Gold() >= Config.AutoplayShopMinGold)
            {
                if (Call(exterior, "EnterShop"))
                {
                    Last = "went into the shop";
                    Log.Info("autoplay", Last);
                    return true;
                }
            }

            if (!hasReward) return false;
            if (!Call(exterior, "SkipShop")) return false;

            Last = "skipped the shop - not enough gold to be worth it";
            Log.Info("autoplay", Last);
            return true;
        }

        // --- buying ---------------------------------------------------------------

        private static bool BuySomething()
        {
            var type = Anchors.Type("UI.Shop.ShopItem");
            if (type == null) return false;

            UnityEngine.Object[] items;
            try { items = Resources.FindObjectsOfTypeAll(type); }
            catch { return false; }

            var purse = Gold() - Config.AutoplayShopReserve;
            if (purse <= 0) return false;

            object best = null;
            float bestValue = float.NegativeInfinity;
            int bestPrice = 0;
            string bestName = "";

            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i] as Component;
                if (!Alive.Is(item)) continue;
                if (!item.gameObject.activeInHierarchy) continue;

                var price = PriceOf(item);
                if (price <= 0 || price > purse) continue;
                if (!Affordable(item)) continue;

                var value = Worth(item);
                if (value < Config.AutoplayShopMinRarity) continue;
                if (value <= bestValue) continue;

                bestValue = value;
                bestPrice = price;
                best = item;
                bestName = Probe<string>(item, "Name") ?? "something";
            }

            if (best == null) return false;
            if (!LeftClick(best)) return false;

            Last = string.Format("bought {0} for {1}", bestName, bestPrice);
            Log.Info("autoplay", Last);
            return true;
        }

        // Rarity is the game's own statement of quality and needs no unit conversion,
        // so it carries the decision. A building on top of that can be judged against
        // the actual board, which is worth more than a guess - but only buildings can,
        // so it adjusts rather than decides.
        private static float Worth(Component item)
        {
            float rarity = Probe<int>(item, "Rarity");

            var tag = Probe<int>(item, "Tag");
            if (tag == 0) return rarity;

            Offer offer;
            if (!Offers.Describe(tag, out offer)) return rarity;

            var board = Board.ReadFor(offer, null);
            if (board == null) return rarity;

            var picks = Plan.Best(board, 1, 0);
            if (picks.Count == 0) return rarity;

            // A tenth of a rarity step per point, so a well-placed common can edge out
            // an uncommon that fits nowhere, and no amount of board score turns a
            // common into a legendary.
            return rarity + Mathf.Clamp(picks[0].Score.Total * 0.1f, -0.9f, 0.9f);
        }

        private static bool Leave(object panel)
        {
            if (!Call(panel, "FinishShopping")) return false;
            Last = "left the shop";
            Log.Info("autoplay", Last);
            return true;
        }

        // --- plumbing -------------------------------------------------------------

        private static int Gold()
        {
            return Singletons.Read(Score, "Money", 0);
        }

        private static int PriceOf(Component item)
        {
            var tag = Probe<object>(item, "PriceTag");
            if (!Alive.Is(tag)) return 0;
            return Probe<int>(tag, "Price");
        }

        private static bool Affordable(Component item)
        {
            bool ambiguous;
            var can = Reflect.MethodByName(item.GetType(), "CanPurchase", 0, out ambiguous);
            if (can == null) return true;

            try
            {
                var result = can.Invoke(item, null);
                return result is bool && (bool)result;
            }
            catch { return false; }
        }

        private static T Probe<T>(object target, string member)
        {
            if (!Alive.Is(target)) return default(T);
            try
            {
                var property = Reflect.Property(target.GetType(), member);
                if (property != null) return Cast<T>(property.GetValue(target, null));

                var field = Reflect.Field(target.GetType(), member);
                if (field != null) return Cast<T>(field.GetValue(target));
            }
            catch { }
            return default(T);
        }

        // Rarity and GameTag come back as game enums; everything downstream wants an
        // int, and an unboxing cast from a boxed enum to int throws.
        private static T Cast<T>(object value)
        {
            if (value == null) return default(T);
            if (value is T) return (T)value;
            if (typeof(T) == typeof(int)) return (T)(object)Convert.ToInt32(value);
            return default(T);
        }

        private static bool Call(object target, string method)
        {
            if (!Alive.Is(target)) return false;

            bool ambiguous;
            var info = Reflect.MethodByName(target.GetType(), method, 0, out ambiguous);
            if (info == null) return false;

            info.Invoke(target, null);
            return true;
        }

        private static bool LeftClick(object target)
        {
            bool ambiguous;
            var click = Reflect.MethodByName(target.GetType(), "OnPointerClick", 1, out ambiguous);
            if (click == null) return false;

            var data = new PointerEventData(EventSystem.current);
            data.button = PointerEventData.InputButton.Left;
            click.Invoke(target, new object[] { data });
            return true;
        }
    }
}
