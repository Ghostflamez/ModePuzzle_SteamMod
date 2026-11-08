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
            var client = new StoreClient
            {
                identifier = Tuning.SmugglerId,
                displayName = Loc.T("smuggler_name"),
                spriteName = SpriteDict.GetShadyMerchantSpriteName(),
                clientFaction = "FACTION_BLACK_MARKET",
                clientIntent = StoreClient.ClientIntent.SELL,
                cooldown = Tuning.SmugglerCooldownDays,
            };
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
    }
}
