using HarmonyLib;

namespace ModePuzzle.Acquisition
{
    /// <summary>
    /// 可疑的走私犯: a black-market seller, built like the game's 黄牛贩子, who only sells the optimizer at a
    /// markup. Registered in the client dictionary so a queued visit survives saving and loading.
    /// </summary>
    static class Smuggler
    {
        public static void Enable()
        {
            StoreClientListDict.storeClientDict[Tuning.SmugglerId] = Create;
            ModHook.OnGenerateCustomerNormal += OnGenerateCustomers;
        }

        public static void Disable()
        {
            ModHook.OnGenerateCustomerNormal -= OnGenerateCustomers;
            StoreClientListDict.storeClientDict.Remove(Tuning.SmugglerId);
        }

        /// <summary>Debug: make the smuggler the next customer.</summary>
        public static void QueueNext() => PlayerStore.Instance?.storeClientManager?.AddNextClient(Create());

        static void OnGenerateCustomers(StoreClientManager manager)
        {
            if (manager == null) return;
            if (ModePuzzleMod.DevSmugglerDaily?.Value == true) { manager.AddClient(Create()); return; }
            var station = StoreStation.Instance;
            if (station == null || station.dayCounter < Tuning.SmugglerFirstDay) return;
            if (StoreClientManager.IsClientOnCooldown(Tuning.SmugglerId) || Ownership.OwnsOptimizer()) return;
            if (!RNG.Roll(Tuning.SmugglerDailyChance)) return;
            manager.AddClient(Create());
        }

        static StoreClient Create()
        {
            var client = NewClient(StoreClient.ClientIntent.SELL);
            client.mainDialogue.SetText(client.displayName, Loc.T("smuggler_line_1"));
            client.mainDialogue.NextDialogue()
                .SetText(client.displayName, Loc.T("smuggler_line_2"))
                .SetEndAction(() => Ownership.OfferStolen(Tuning.SmugglerHeatMin, Tuning.SmugglerHeatMax));
            client.onItemAddedToWeighted = item =>
            {
                if (item != null && !GeneralHelper.IsItemOwned(item))
                    item.AddItemFeature(ItemFeatureList.ScalperMarkup(Tuning.SmugglerMarkupPercent));
            };
            client.AddBasicDialog();
            client.CompleteClientCreation();
            return client;
        }

        /// <summary>
        /// The same smuggler when he finds the player already has an optimizer (bought after his visit was
        /// queued): amazed, he sells nothing and buys stolen goods instead, below market like the game's
        /// 可疑的商人, with the same budget formula.
        /// </summary>
        public static StoreClient CreateFence()
        {
            var client = NewClient(StoreClient.ClientIntent.BUY);
            client.SetBudget(Tuning.FenceBudgetBase + RNG.GetRandomInt(0, Tuning.FenceBudgetRandom)
                             + StoreStation.Instance.dayCounter * Tuning.FenceBudgetPerDay);
            client.clientBuyingTagList.Add("STOLEN_TAG");
            client.mainDialogue.SetText(client.displayName, Loc.T("smuggler_fence_line_1"));
            client.mainDialogue.NextDialogue().SetText(client.displayName, Loc.T("smuggler_fence_line_2"))
                .NextDialogue().SetText(client.displayName, Loc.T("smuggler_fence_line_3", client.GetBudget()));
            client.onItemAddedToWeighted = item =>
            {
                if (item != null && GeneralHelper.IsItemOwned(item)) item.AddItemFeature(ItemFeatureList.DiscountedBuy25());
            };
            client.AddBasicDialog();
            client.CompleteClientCreation();
            return client;
        }

        static StoreClient NewClient(StoreClient.ClientIntent intent) => new StoreClient
        {
            identifier = Tuning.SmugglerId,
            displayName = Loc.T("smuggler_name"),
            spriteName = SpriteDict.GetShadyMerchantSpriteName(),
            clientFaction = "FACTION_BLACK_MARKET",
            clientIntent = intent,
            cooldown = Tuning.SmugglerCooldownDays,
        };
    }

    /// <summary>Right before a queued customer walks in: a smuggler meeting a player who already owns an
    /// optimizer becomes the fence.</summary>
    [HarmonyPatch(typeof(PlayerStore), "GetNextClient")]
    static class SmugglerArrivalPatch
    {
        static void Prefix(PlayerStore __instance)
        {
            var stack = __instance?.storeClientManager?.clientStack;
            if (stack == null || stack.Count == 0 || stack[0]?.identifier != Tuning.SmugglerId) return;
            if (Ownership.OwnsOptimizer()) stack[0] = Smuggler.CreateFence();
        }
    }
}
