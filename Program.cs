using APConnection;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Landfall.Haste;
using Landfall.Modding;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.Localization;
using Zorro.Settings;
using static Integration.Integration;

[LandfallPlugin]
public partial class Program
{
    private static Connection? connection;

    private static readonly Version version = new(0, 3, 0);

    static Program()
    {
        UnityMainThreadDispatcher.Instance().log("AP Program launched");

        GameObject instance = new(nameof(ApDebugLog));
        UnityEngine.Object.DontDestroyOnLoad(instance);
        instance.AddComponent<ApDebugLog>();
        ApDebugLog.Instance.BuildFont();

        UnityMainThreadDispatcher.Instance().log("AP Log created");
        ApDebugLog.Instance.DisplayMessage($"Archiplego Installed v{version}", isDebug: false);

        // Load everything up when the games starts from menu
        On.GameHandler.PlayFromMenu += StaticPlayFromMenuHook;
        On.GameHandler.LoadMainMenu += StaticLoadMainMenuHook;

    }

    private static void StaticPlayFromMenuHook(On.GameHandler.orig_PlayFromMenu orig)
    {


        if (connection != null)
        {
            UnityMainThreadDispatcher.Instance().logError("AP Play button clicked and connection is not null");
        }
        ApDebugLog.Instance.DisplayMessage("Begining Build");

        var settingsHandler = GameHandler.Instance.SettingsHandler;
        var enabledSetting = settingsHandler.GetSetting<ApEnabledSetting>().Value;

        if (!enabledSetting)
        {
            UnityMainThreadDispatcher.Instance().log("AP not started as it was disabled");
            ApDebugLog.Instance.DisplayMessage("AP Disabled");

            UnityMainThreadDispatcher.Instance().log("AP Transitioning to original actions");
            orig();
            return;
        }

        UnityMainThreadDispatcher.Instance().log("AP enabled so begining startup");
        var serverName = settingsHandler.GetSetting<ApServerNameSetting>().Value;
        var serverPort = settingsHandler.GetSetting<ApServerPortSetting>().Value;
        var username = settingsHandler.GetSetting<ApUsernameSetting>().Value;
        var password = settingsHandler.GetSetting<ApPasswordSetting>().Value;

        if (password == "")
        {
            password = null;
        }

        connection = new(serverName, serverPort);

        // This must go BEFORE connection
        connection.BuildItemReciver(GiveItem);
        connection.buildMessageReciver();

        if (!connection.Connect(username, password, version))
        {
            ApDebugLog.Instance.DisplayMessage("Connection Failed");
            return;
        }
        ApDebugLog.Instance.DisplayMessage("Connected");

        Integration.Integration.connection = connection;

        FactSystem.SetFact(new Fact("APForceReloadFirstLoad"), 0f);
        SetHubState();
        for (int i = 1; i <= 10; i++)
        {
            SetFragmentLimits("Shard"+i);
        }
        SetFragmentLimits("Global");

        FactSystem.SubscribeToFact(new Fact("current_unbeaten_shard"), (value) =>
        {
            ApDebugLog.Instance.DisplayMessage($"unbeaten shard update to (zero index){value}");
            if (connection == null)
                return;

            var shard_count = connection.GetItemCount("Progressive Shard");
            // Never allow the value to be not what I want it to be
            if (FactSystem.GetFact(new Fact("APShardUnlockOrder")) == 1f)
            {
                var newvalue = Math.Min(shard_count, (int)FactSystem.GetFact(new Fact("APBossDefeated")));
                // if unlock-order is Boss-locked, then only uplock up until the lowest of either bossbeated or shards
                if (value != newvalue) FactSystem.SetFact(new Fact("current_unbeaten_shard"), newvalue);
            } else
            {
                if (value != shard_count) FactSystem.SetFact(new Fact("current_unbeaten_shard"), shard_count);
            }
        });


        // only need to bother with this once a savefile
        if (FactSystem.GetFact(new Fact("APFirstLoad")) == 0f)
        {
            ClearStoryFlags();
        }
        FactSystem.SetFact(new Fact("APFirstLoad"), 1f);

        SaveSystem.Save();
        
        if (FactSystem.GetFact(new Fact("APDeathlink")) == 1f)
        {
            UnityMainThreadDispatcher.Instance().log("AP DeathLink is Enabled");
            ApDebugLog.Instance.DisplayMessage("Deathlink Enabled");

            connection.deathLinkService!.OnDeathLinkReceived += GiveDeath;

            connection.deathLinkService!.EnableDeathLink();

            On.Player.Die += StaticSendDeathOnDie;

            UnityMainThreadDispatcher.Instance().log("AP DeathLink Hooked");
        }
        else
        {
            UnityMainThreadDispatcher.Instance().log("AP Deathlink is Disabled");
        }

        // Add Game base Actions
        UnityMainThreadDispatcher.Instance().log("AP Creating Hooks");

        // Override the handling of completing of a run to 
        On.GM_API.OnRunEnd += StaticCompleteRunHook;
        On.GM_API.OnPlayerEnteredPortal += StaticFragmentComplete;

        On.GM_API.OnEndBossWin += StaticEndBossHook;

        // When a boss is defeated send the location
        On.GM_API.OnBossDeath += StaticBossDeathHook;

        On.ShopItemHandler.BuyItem += StaticBuyItemHook;
        On.ShopItemHandler.GenerateItems += StaticGenerateItemHook;

        On.Landfall.Haste.SkinPurchasePopup.PurchaseSkin += StaticPurchaseSkinHook;
        On.Landfall.Haste.SkinManager.SetDefaultUnlockedSkins += StaticDefaultUnlockedSkinsHook;

        On.SaveSystem.Load += StaticSaveLoadHook;

        On.Landfall.Haste.MetaProgressionRowUI.AddClicked += StaticMetaProgressionRowAddClickedHook;
        On.PlayerStats.AddStats += StaticPlayerAddStatsHook;

        On.Landfall.Haste.AbilityUnlockScreen.Unlock += StaticMetaProgressionUnlockOverloadHook;
        On.InteractableCharacter.Start += StaticInteractableCharacterStartHook;
        On.InteractableCharacter.Interact += StaticInteractableCharacterInteractHook;

        On.PlayerCharacter.RestartPlayer_Launch_Transform_float += StaticSetSpeed;
        On.PlayerCharacter.RestartPlayer_Still_Transform += StaticSetSpeed;


        UnityMainThreadDispatcher.Instance().log("AP Hooks Complete");

        // Once the player starts in game do the loading as somethings are not setup yet
        On.GM_API.OnSpawnedInHub += StaticLoadHubHook;

        UnityMainThreadDispatcher.Instance().log("AP Transitioning to original actions");
        ApDebugLog.Instance.DisplayMessage("Loading normally now");
        orig();
    }

    /// <summary>
    /// Extra function to remove hooks on exiting the game, so that new hooks arent accidentally installed ontop of existing ones
    /// </summary>
    private static void ClearHooks()
    {
        On.GM_API.OnRunEnd -= StaticCompleteRunHook;
        On.GM_API.OnPlayerEnteredPortal -= StaticFragmentComplete;
        On.GM_API.OnEndBossWin -= StaticEndBossHook;
        On.GM_API.OnBossDeath -= StaticBossDeathHook;
        On.ShopItemHandler.BuyItem -= StaticBuyItemHook;
        On.ShopItemHandler.GenerateItems -= StaticGenerateItemHook;
        On.Landfall.Haste.SkinPurchasePopup.PurchaseSkin -= StaticPurchaseSkinHook;
        On.Landfall.Haste.SkinManager.SetDefaultUnlockedSkins -= StaticDefaultUnlockedSkinsHook;
        On.SaveSystem.Load -= StaticSaveLoadHook;
        On.Landfall.Haste.AbilityUnlockScreen.Unlock -= StaticMetaProgressionUnlockOverloadHook;
        On.InteractableCharacter.Start -= StaticInteractableCharacterStartHook;
        On.InteractableCharacter.Interact -= StaticInteractableCharacterInteractHook;
        On.GM_API.OnSpawnedInHub -= StaticLoadHubHook;
        On.PlayerCharacter.RestartPlayer_Launch_Transform_float -= StaticSetSpeed;
        On.PlayerCharacter.RestartPlayer_Still_Transform -= StaticSetSpeed;
        On.Landfall.Haste.MetaProgressionRowUI.AddClicked -= StaticMetaProgressionRowAddClickedHook;
        On.PlayerStats.AddStats -= StaticPlayerAddStatsHook;
        if (FactSystem.GetFact(new Fact("APDeathlink")) == 1f)
        {
            On.Player.Die -= StaticSendDeathOnDie;
            connection.deathLinkService!.OnDeathLinkReceived -= GiveDeath;
            connection.deathLinkService!.DisableDeathLink();

        }
        UnityMainThreadDispatcher.Instance().log("AP Hooks Removed");
        ApDebugLog.Instance.DisplayMessage("AP Hooks Removed");
    }

    private static void StaticSetSpeed(On.PlayerCharacter.orig_RestartPlayer_Still_Transform orig, PlayerCharacter self, Transform spawnPoint)
    {
        if (FactSystem.GetFact(new Fact("APSpeedUpgrades")) == 1f)
        {
            SecretSetSpeed(self);
        }
        orig(self, spawnPoint);
    }

    private static void StaticSetSpeed(On.PlayerCharacter.orig_RestartPlayer_Launch_Transform_float orig, PlayerCharacter self, Transform spawnPoint, float minVel)
    {
        if (FactSystem.GetFact(new Fact("APSpeedUpgrades")) == 1f)
        {
            SecretSetSpeed(self);
        }
        orig(self, spawnPoint, minVel);
    }

    private static void SecretSetSpeed(PlayerCharacter player)
    {
        var APspeed = FactSystem.GetFact(new Fact("APSpeedUpgradesCollected"));
        float calcSpeed = 0.6f + (APspeed * 0.1f);
        ApDebugLog.Instance.DisplayMessage($"Setting speed to {calcSpeed}");
        player.player.stats.runSpeed.multiplier = calcSpeed;
    }

    private static void StaticSendDeathOnDie(On.Player.orig_Die orig, Player self)
    {
        UnityMainThreadDispatcher.Instance().log("AP Player death Hooked");
        ApDebugLog.Instance.DisplayMessage($"Source: {FactSystem.GetFact(new Fact("APDoubleKillStopper"))}");
        if (FactSystem.GetFact(new Fact("APDoubleKillStopper")) == 1f)
        {
            // this kill came from deathlink, so just kill the player but avoid the re-sending of another deathlink
            FactSystem.SetFact(new Fact("APDoubleKillStopper"), 0f);
            orig(self);
            return;
        }
        ApDebugLog.Instance.DisplayMessage("Death Link sent");
        connection!.deathLinkService!.SendDeathLink(new DeathLink(connection.username));
        orig(self);
    }

    private static HasteSave StaticSaveLoadHook(On.SaveSystem.orig_Load orig)
    {

        // UpdateShardCount(connection!.shardCount);
        var save = orig();
        return save;

    }

    /// <summary>
    /// Hook into to load of the hub. Used to set data on start up
    /// </summary>
    private static void StaticLoadHubHook(On.GM_API.orig_OnSpawnedInHub orig)
    {
        UnityMainThreadDispatcher.Instance().log("AP Loading into Hub");
        ApDebugLog.Instance.DisplayMessage("Loaded into hub");

        UpdateShardCount();
        SetHubState();

        // Only do these on the first load of the game
        if (FactSystem.GetFact(new Fact("APForceReloadFirstLoad")) == 0f)
        {
            FactSystem.SetFact(new Fact("APForceReloadFirstLoad"), 1f);

            SaveSystem.Save();
        }

        orig();
    }

    /// <summary>
    /// Hook for when the Main Menu is loaded. Used to close connection
    /// </summary>
    private static void StaticLoadMainMenuHook(On.GameHandler.orig_LoadMainMenu orig)
    {
        if (connection != null)
        {
            ClearHooks();
            connection.Close();
            connection = null;
            ApDebugLog.Instance.DisplayMessage("Removed connection");
            FactSystem.SetFact(new Fact("APForceReloadFirstLoad"), 0f);
        }

        orig();
    }


    /// <summary>
    /// Hook into run completion. Used to control Player Progress
    /// </summary>
    private static void StaticCompleteRunHook(On.GM_API.orig_OnRunEnd orig, RunHandler.LastRunState state)
    {
        if (connection == null)
        {
            ApDebugLog.Instance.DisplayMessage("AP On Complete Run the connection is null");
            return;
        }
        ApDebugLog.Instance.DisplayMessage("Run Complete");
        UpdateShardCount();
    }

    /// <summary>
    /// Hook into when a player clears a fragment. Used to give fragment locations
    /// </summary>
    private static void StaticFragmentComplete(On.GM_API.orig_OnPlayerEnteredPortal orig, Player player)
    {
        if (connection == null)
        {
            ApDebugLog.Instance.DisplayMessage("AP On Fragment Clear the connection is null");
            return;
        }
        // make sure youre actually in a fragment, not a rest/shop/challenge
        if (FactSystem.GetFact(new Fact("APFragmentsanity")) > 0f && RunHandler.IsInLevelOrChallenge())
        {
            string? locationName; // location to send
            string? FragKeyword; // suffix to put on everything
            if (FactSystem.GetFact(new Fact("APFragmentsanity")) == 1f)
            {
                var currentShard = RunHandler.RunData.shardID + 1;
                locationName = $"Shard {currentShard} Fragment Clear ";
                FragKeyword = "Shard" + currentShard;
            } else
            {
                locationName = "Global Fragment Clear ";
                FragKeyword = "Global";
            }

            if (FactSystem.GetFact(new Fact("APFragmentsanityLocation" + FragKeyword)) < FactSystem.GetFact(new Fact("APFragmentsanityQuantity")))
            {
                FactSystem.AddToFact(new Fact("APFragmentsanity" + FragKeyword), 1f);
                ApDebugLog.Instance.DisplayMessage($"Completed Fragment. Progress: {FactSystem.GetFact(new Fact("APFragmentsanity" + FragKeyword))}/{FactSystem.GetFact(new Fact("APFragmentLimit" + FragKeyword))} for Clear {Convert.ToInt32(FactSystem.GetFact(new Fact("APFragmentsanityLocation" + FragKeyword))) + 1}", isDebug: false);
                if (FactSystem.GetFact(new Fact("APFragmentsanity" + FragKeyword)) == FactSystem.GetFact(new Fact("APFragmentLimit" + FragKeyword)))
                {
                    connection.SendLocation(locationName + (Convert.ToInt32(FactSystem.GetFact(new Fact("APFragmentsanityLocation" + FragKeyword))) + 1).ToString("D2"));
                    FactSystem.AddToFact(new Fact("APFragmentsanityLocation" + FragKeyword), 1f);
                    FactSystem.SetFact(new Fact("APFragmentsanity" + FragKeyword), 0f);
                    SetFragmentLimits(FragKeyword);
                }
            }

        }
        orig(player);
    }

    public static void SetFragmentLimits(string keyword)
    {
        if (FactSystem.GetFact(new Fact("APFragmentsanity")) == 2f)
        {
            // if MODE H-TRI
            // LIMIT = floor(FRAGMENTLOCATION / 2)
            FactSystem.SetFact(new Fact($"APFragmentLimit{keyword}"), Math.Max((float)Math.Floor((FactSystem.GetFact(new Fact($"APFragmentsanityLocation{keyword}")) + 1) / 2), 1f));
        }
        else if (FactSystem.GetFact(new Fact("APFragmentsanity")) == 3f)
        {
            // if MODE BALANCED HALF-TRI
            // LIMIT = min( floor(FRAGMENTLOCATION / 2), 10
            FactSystem.SetFact(new Fact($"APFragmentLimit{keyword}"), Math.Max(Math.Min((float)Math.Floor((FactSystem.GetFact(new Fact($"APFragmentsanityLocation{keyword}")) + 1) / 2), 10f), 1f));
        }
        else if (FactSystem.GetFact(new Fact("APFragmentsanity")) == 4f)
        {
            // if MODE TRI
            // LIMIT = FRAGMENTLOCATION
            FactSystem.SetFact(new Fact($"APFragmentLimit{keyword}"), FactSystem.GetFact(new Fact($"APFragmentsanityLocation{keyword}")) + 1);
        }
    }


    private static void StaticGenerateItemHook(On.ShopItemHandler.orig_GenerateItems orig, ShopItemHandler self)
    {
        if (FactSystem.GetFact(new Fact("APShopsanitySeperate")) == 1f)
        {

            // more type reflection bs to get around private values being annoying 
            Type metaShopItemHandler = typeof(ShopItemHandler);
            FieldInfo entryRerolls = metaShopItemHandler.GetField("rerolls", BindingFlags.NonPublic | BindingFlags.Instance);
            var rerolls = entryRerolls.GetValue(self) as int?;
            FieldInfo entryItemInstances = metaShopItemHandler.GetField("itemInstances", BindingFlags.NonPublic | BindingFlags.Instance);
            var ItemInstances = entryItemInstances.GetValue(self) as List<ItemInstance>;
            FieldInfo entryItemVisuals = metaShopItemHandler.GetField("itemVisuals", BindingFlags.NonPublic | BindingFlags.Instance);
            var ItemVisuals = entryItemVisuals.GetValue(self) as List<ShopItem>;
            MethodInfo entryUnset = metaShopItemHandler.GetMethod("Unset", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, [typeof(int)], null);

            try
            {
                if (rerolls == null) ApDebugLog.Instance.DisplayMessage($"rerolls detected null");
                if (ItemInstances == null) ApDebugLog.Instance.DisplayMessage($"ItemInstances detected null");
                if (ItemVisuals == null) ApDebugLog.Instance.DisplayMessage($"ItemVisuals detected null");
                if (entryUnset == null) ApDebugLog.Instance.DisplayMessage($"entryUnset detected null");
                Player localPlayer = Player.localPlayer;
                System.Random currentLevelRandomInstance = RunHandler.GetCurrentLevelRandomInstance((int)rerolls);
                List<ItemInstance> excludedList = new List<ItemInstance>();
                foreach (ItemInstance itemInstance in ItemInstances)
                {
                    if (itemInstance)
                    {
                        excludedList.Add(itemInstance);
                    }
                }
                int num = Mathf.Max(ItemInstances.Count, ItemVisuals.Count);
                for (int i = 0; i < num; i++)
                {
                    entryUnset.Invoke(self, [i]);
                }

                for (int j = 0; j < 3; j++)
                {
                    ItemInstance? itemInstance2 = null;
                    System.Random random = new System.Random();
                    //TODO: determine if there are no AP items left in that region, then make it spawn a normal item instead
                    // unless the inX is 100, then just let them keep getting useless items I guess (maybe another dummy item)
                    int inX = (int)FactSystem.GetFact(new Fact("APShopsanitySeperateRate"));
                    if (random.Next(1, 100) <= inX)
                    {
                        itemInstance2 = ItemDatabase.instance.items.Where(x => x.itemName == "ArchipelagoShopItem").ToList()[0];
                    }
                    else
                    {
                        itemInstance2 = self.testItem ? self.testItem : ItemDatabase.GetRandomItem(localPlayer, currentLevelRandomInstance, GetRandomItemFlags.Major, TagInteraction.None, null, excludedList, RunHandler.GetShopItemRarityModifier());
                    }
                    if (itemInstance2 != null)
                    {
                        // normal items
                        if (itemInstance2.itemName != "ArchipelagoShopItem") excludedList.Add(itemInstance2);
                        ItemInstances.Add(itemInstance2);
                    }
                }
            }
            catch (Exception e)
            {
                UnityMainThreadDispatcher.Instance().log($"Error in generating shop items {e.Message},{e.InnerException},{e.StackTrace}");
                ApDebugLog.Instance.DisplayMessage($"Error in generating shop items {e.Message},{e.InnerException},{e.StackTrace}", duration: 10f, isDebug:false);
            }

            
            
        } else
        {
            orig(self);
        }
    }


    /// <summary>
    /// Hook into when a player buys and item. Used to give item locations
    /// </summary>
    private static void StaticBuyItemHook(On.ShopItemHandler.orig_BuyItem orig, ShopItemHandler self, ItemInstance item, int price)
    {
        if (connection == null)
        {
            ApDebugLog.Instance.DisplayMessage("AP On Get Item the connection is null");
            return;
        }
        var item_location = "";
        bool canSend = true;
        bool shouldParse = true;


        if (FactSystem.GetFact(new Fact("APShopsanitySeperate")) == 1f && item.itemName != "ArchipelagoShopItem")
        {
            shouldParse = false;
            canSend = false;
        }

        if (shouldParse)
        {
            if (FactSystem.GetFact(new Fact("APShopsanity")) == 1f)
            {
                var currentShard = RunHandler.RunData.shardID + 1;
                item_location = "Shard " + currentShard + " Shop Item " + (Convert.ToInt32(FactSystem.GetFact(new Fact("APShopsanityShard" + currentShard))) + 1).ToString("D2");
                FactSystem.AddToFact(new Fact("APShopsanityShard" + currentShard), 1f);
                canSend = FactSystem.GetFact(new Fact("APShopsanityShard" + currentShard)) <= FactSystem.GetFact(new Fact("APShopsanityQuantity"));
            }
            else if (FactSystem.GetFact(new Fact("APShopsanity")) == 2f)
            {
                item_location = "Global Shop Item " + Convert.ToInt32(FactSystem.GetFact(new Fact("APShopsanityGlobal")) + 1).ToString("D3");
                FactSystem.AddToFact(new Fact("APShopsanityGlobal"), 1f);
                canSend = FactSystem.GetFact(new Fact("APShopsanityGlobal")) <= FactSystem.GetFact(new Fact("APShopsanityQuantity"));
            }
            else if (FactSystem.GetFact(new Fact("APShopsanity")) == 0f)
            {
                canSend = false;
            }
        }

        if (canSend)
        {
            UnityMainThreadDispatcher.Instance().log("AP sending Item: (" + item_location + ")");
            ApDebugLog.Instance.DisplayMessage("Bought Item");

            connection.SendLocation(item_location);
        }
        // Buy the item
        orig(self, item, price);
    }


    /// <summary>
    /// Hook into when a Boss is deafeted. Used to give boss locations
    /// </summary>
    private static void StaticBossDeathHook(On.GM_API.orig_OnBossDeath orig)
    {
        if (connection == null)
        {
            ApDebugLog.Instance.DisplayMessage("AP On Boss Death the connection is null");
            return;
        }
        var currentShard = RunHandler.RunData.shardID + 1;
        FactSystem.SetFact(new Fact("APBossDefeated"), Math.Max(FactSystem.GetFact(new Fact("APBossDefeated")), currentShard));

        var boss_location = "Shard " + currentShard + " Boss";
        UnityMainThreadDispatcher.Instance().log("AP sending Boss: (" + boss_location + ")");
        ApDebugLog.Instance.DisplayMessage("Boss Defeated");

        if (currentShard == Convert.ToInt32(FactSystem.GetFact(new Fact("APShardGoal")))){
            ApDebugLog.Instance.DisplayMessage("Game Complete");
            connection.CompleteGame();
        } else
        {
            connection.SendLocation(boss_location);
        }
        orig();
    }

    /// <summary>
    /// Hook into when the end Boss is deafeted. Used release the world
    /// </summary>
    private static void StaticEndBossHook(On.GM_API.orig_OnEndBossWin orig)
    {
        if (connection == null)
        {
            ApDebugLog.Instance.DisplayMessage("AP On End Boss Win the connection is null");
            return;
        }
        if (Convert.ToInt32(FactSystem.GetFact(new Fact("APShardGoal"))) == 10)
        {
            ApDebugLog.Instance.DisplayMessage("Game Complete");
            connection.CompleteGame();
        } else
        {
            connection.SendLocation("Shard 10 Boss");
        }
        orig();
    }

    private static void StaticMetaProgressionUnlockOverloadHook(On.Landfall.Haste.AbilityUnlockScreen.orig_Unlock orig, AbilityUnlockScreen self)
    {
        // Get the type of the AbilityUnlockScreen
        Type unlockScreenType = typeof(AbilityUnlockScreen);

        // Use reflection to get the private field "Character"
        FieldInfo characterField = unlockScreenType.GetField("Character", BindingFlags.NonPublic | BindingFlags.Instance);

        if (connection == null)
        {
            ApDebugLog.Instance.DisplayMessage("AP On Progession Unlock the connection is null");
            return;
        }

        // Get the value of the "Character" field from the unlockScreen instance
        var interactionCharacter = characterField.GetValue(self) as InteractionCharacter;
        if (interactionCharacter == null)
        {
            ApDebugLog.Instance.DisplayMessage("The 'Character' field is null or not of type InteractionCharacter.");
            return;
        }
        var ability_name = Enum.GetName(typeof(AbilityKind), interactionCharacter.Ability);
        var ability_location = $"{GetAbilityName(ability_name)} Purchase";
        UnityMainThreadDispatcher.Instance().log("AP sending Ability: (" + ability_location + ")");
        ApDebugLog.Instance.DisplayMessage($"Ability Got: {ability_name}");
        var cost = MetaProgression.Instance.GetEntry(interactionCharacter.Ability).cost;
        MetaProgression.AddResource(-cost);

        connection.SendLocation(ability_location);
        // orig();

        // Close the window
        self.gameObject.SetActive(value: false);
    }

    private static void StaticMetaProgressionRowAddClickedHook(On.Landfall.Haste.MetaProgressionRowUI.orig_AddClicked orig, MetaProgressionRowUI self)
    {
        if (connection == null)
        {
            ApDebugLog.Instance.DisplayMessage("AP On Meta Progression Row UI the connection is null");
            return;
        }
        // this is basically just the decompiled original code with added jank lmao
        if (FactSystem.GetFact(new Fact("APCaptainsRewards")) == 1f)
        {
            Type metaRowType = typeof(MetaProgressionRowUI);
            // type reflection to get around private values being annoying 
            FieldInfo entryField = metaRowType.GetField("_entry", BindingFlags.NonPublic | BindingFlags.Instance);
            var entry = entryField.GetValue(self) as MetaProgression.Entry;

            FieldInfo sfxField = metaRowType.GetField("sfx", BindingFlags.NonPublic | BindingFlags.Instance);
            var sfx = sfxField.GetValue(self) as MetaProgressionSFX;

            ValueTuple<int, bool, bool> costToUpgrade = entry.GetCostToUpgrade();
            int item = costToUpgrade.Item1;
            if (!costToUpgrade.Item2)
            {
                if (sfx && sfx.noMoney)
                {
                    sfx.noMoney.Play();
                }
                return;
            }
            if (sfx)
            {
                if (sfx.press)
                {
                    sfx.press.Play();
                }
                if (sfx.increase)
                {
                    sfx.increase.sfxs[0].settings.pitch = 1f + (float)item / 2000f;
                    sfx.increase.Play();
                }
            }
            connection.SendLocation(GetCaptainUpgradeName(self.kind.ToString(), entry.CurrentLevel));
            int? nextLevel = entry.GetNextLevel(entry.CurrentLevel);
            if (nextLevel != null)
            {
                int valueOrDefault = nextLevel.GetValueOrDefault();
                entry.CurrentLevel = valueOrDefault;
            }
            else
            {
                ApDebugLog.Instance.DisplayMessage(string.Format("Error: could not get next level for {0} (currernt level = {1}). This shouldn't happen due to checks above.", entry.fact, entry.CurrentLevel));
            }
            FactSystem.AddToFact(MetaProgression.MetaProgressionResource, (float)(-(float)item));
            SaveSystem.Save();

            // shoutouts to RDS' Captain Refunder mod for the code I copied to get this type reflection hell to work
            Type MetaUIType = typeof(MetaProgressionRowUI).Assembly.GetType("Landfall.Haste.MetaProgressionUI");
            MethodInfo refreshUiInfo = MetaUIType.GetMethod("RefreshUI", BindingFlags.Instance | BindingFlags.Public);
            object metaUiInstance = GameObject.FindAnyObjectByType(MetaUIType);
            refreshUiInfo.Invoke(metaUiInstance, null);
            self.RefreshUI();

        }
        else
        {
            orig(self);
        }
    }


    private static void StaticPlayerAddStatsHook(On.PlayerStats.orig_AddStats orig, PlayerStats self, PlayerStats otherStats, float multiplier)
    {
        if (FactSystem.GetFact(new Fact("APCaptainsRewards")) == 1f)
        {
            //ApDebugLog.Instance.DisplayMessage($"incoming health: {otherStats.maxHealth.baseValue}, {self.maxHealth.baseValue}");
            //ApDebugLog.Instance.DisplayMessage($"incoming lives: {otherStats.lives.baseValue}, {self.lives.baseValue}");
            //ApDebugLog.Instance.DisplayMessage($"incoming energy: {otherStats.maxEnergy.baseValue}, {self.maxEnergy.baseValue}");
            //ApDebugLog.Instance.DisplayMessage($"incoming l sparks: {otherStats.extraLevelSparks.baseValue}, {self.extraLevelSparks.baseValue}");
            //ApDebugLog.Instance.DisplayMessage($"incoming rarity: {otherStats.itemRarity.baseValue}, {self.itemRarity.baseValue}");
            //ApDebugLog.Instance.DisplayMessage($"incoming resource: {otherStats.startingResource.baseValue}, {self.startingResource.baseValue}");
            float calcmaxHealth = 80f + (10 * FactSystem.GetFact(new Fact("APUpgradeMaxHealth")));
            float calclives = 3f + FactSystem.GetFact(new Fact("APUpgradeMaxLives"));
            float calcmaxEnergy = 0.5f + (0.2f * FactSystem.GetFact(new Fact("APUpgradeMaxEnergy")));
            // THESE next two might need some value tweaking, i think these are wrong and might need to just be 1 mults the whole way down
            float calcextraLevelSparks = (40f * FactSystem.GetFact(new Fact("APUpgradeLevelSparks")));
            float calcitemRarity = (0.25f * FactSystem.GetFact(new Fact("APUpgradeItemRarity")));
            float calcstartingResource = (float)FactSystem.GetFact(new Fact("APUpgradeStartingSparks")) switch
            {
                1f => 100f,
                2f => 250f,
                3f => 500f,
                _ => 150f * FactSystem.GetFact(new Fact("APUpgradeStartingSparks")),
            };
            //ApDebugLog.Instance.DisplayMessage($"calc health: {calcmaxHealth}");
            //ApDebugLog.Instance.DisplayMessage($"calc lives: {calclives}");
            //ApDebugLog.Instance.DisplayMessage($"calc energy: {calcmaxEnergy}");
            //ApDebugLog.Instance.DisplayMessage($"calc l sparks: {calcextraLevelSparks}");
            //ApDebugLog.Instance.DisplayMessage($"calc rarity: {calcitemRarity}");
            //ApDebugLog.Instance.DisplayMessage($"calc resource: {calcstartingResource}");
            otherStats.maxHealth.baseValue = (self.maxHealth.baseValue < calcmaxHealth) ? calcmaxHealth : otherStats.maxHealth.baseValue;
            otherStats.lives.baseValue = (self.lives.baseValue < calclives) ? calclives : otherStats.lives.baseValue;
            otherStats.maxEnergy.baseValue = (self.maxEnergy.baseValue < calcmaxEnergy) ? calcmaxEnergy : otherStats.maxEnergy.baseValue;
            otherStats.extraLevelSparks.baseValue = (self.extraLevelSparks.baseValue < 200f + calcextraLevelSparks) ? calcextraLevelSparks : otherStats.extraLevelSparks.baseValue;
            otherStats.itemRarity.baseValue = (self.itemRarity.baseValue < calcitemRarity) ? calcitemRarity : otherStats.itemRarity.baseValue;
            otherStats.startingResource.baseValue = (self.startingResource.baseValue <= calcstartingResource) ? calcstartingResource : otherStats.startingResource.baseValue;
            //ApDebugLog.Instance.DisplayMessage($"outgoing health: {otherStats.maxHealth.baseValue}");
            //ApDebugLog.Instance.DisplayMessage($"outgoing lives: {otherStats.lives.baseValue}");
            //ApDebugLog.Instance.DisplayMessage($"outgoing energy: {otherStats.maxEnergy.baseValue}");
            //ApDebugLog.Instance.DisplayMessage($"outgoing l sparks: {otherStats.extraLevelSparks.baseValue}");
            //ApDebugLog.Instance.DisplayMessage($"outgoing rarity: {otherStats.itemRarity.baseValue}");
            //ApDebugLog.Instance.DisplayMessage($"outgoing resource: {otherStats.startingResource.baseValue}");
            self.maxHealth.AddStat(otherStats.maxHealth, multiplier);
            self.lives.AddStat(otherStats.lives, multiplier);
            self.maxEnergy.AddStat(otherStats.maxEnergy, multiplier);
            self.extraLevelSparks.AddStat(otherStats.extraLevelSparks, multiplier);
            self.itemRarity.AddStat(otherStats.itemRarity, multiplier);
            self.startingResource.AddStat(otherStats.startingResource, multiplier);



            self.maxHealthMultiplier.AddStat(otherStats.maxHealthMultiplier, multiplier);
            self.runSpeed.AddStat(otherStats.runSpeed, multiplier);
            self.airSpeed.AddStat(otherStats.airSpeed, multiplier);
            self.turnSpeed.AddStat(otherStats.turnSpeed, multiplier);
            self.drag.AddStat(otherStats.drag, multiplier);
            self.gravity.AddStat(otherStats.gravity, multiplier);
            self.fastFallSpeed.AddStat(otherStats.fastFallSpeed, multiplier);
            self.fastFallLerp.AddStat(otherStats.fastFallLerp, multiplier);
            self.dashes.AddStat(otherStats.dashes, multiplier);
            self.boost.AddStat(otherStats.boost, multiplier);
            self.luck.AddStat(otherStats.luck, multiplier);
            self.startWithEnergyPercentage.AddStat(otherStats.startWithEnergyPercentage, multiplier);
            self.itemPriceMultiplier.AddStat(otherStats.itemPriceMultiplier, multiplier);
            self.sparkMultiplier.AddStat(otherStats.sparkMultiplier, multiplier);
            self.energyGain.AddStat(otherStats.energyGain, multiplier);
            self.damageMultiplier.AddStat(otherStats.damageMultiplier, multiplier);
            self.sparkPickupRange.AddStat(otherStats.sparkPickupRange, multiplier);
            self.extraLevelDifficulty.AddStat(otherStats.extraLevelDifficulty, multiplier);
            self.speedDifficultyMultiplier.AddStat(otherStats.speedDifficultyMultiplier, multiplier);
        }
        else
        {
            orig(self, otherStats, multiplier);
        }
    }

    private static void StaticPurchaseSkinHook(On.Landfall.Haste.SkinPurchasePopup.orig_PurchaseSkin orig, SkinPurchasePopup self)
    {
        if (connection == null)
        {
            ApDebugLog.Instance.DisplayMessage("AP On Skin Purchase the connection is null");
            return;
        }
        if (FactSystem.GetFact(new Fact("APFashionPurchases")) >= 1f)
        {
            connection.SendLocation($"{GetFashionPurchaseName(self.skin.Skin)} Costume Purchase");
        }
        orig(self);

    }

    private static void StaticDefaultUnlockedSkinsHook(On.Landfall.Haste.SkinManager.orig_SetDefaultUnlockedSkins orig)
    {
        if (FactSystem.GetFact(new Fact("APFashionPurchases")) < 2f)
        {
            // only set the defaults in vanilla and off
            orig();
        }
    }

    private static void StaticInteractableCharacterStartHook(On.InteractableCharacter.orig_Start orig, InteractableCharacter self)
    {
        Type interactableCharacterType = self.GetType();
        FieldInfo initField = interactableCharacterType.GetField("init", BindingFlags.NonPublic | BindingFlags.Instance);

        var ability = self.character.Ability;
        var abilityName = Enum.GetName(typeof(AbilityKind), ability);

        if (initField.GetValue(self) is not bool init)
        {
            ApDebugLog.Instance.DisplayMessage("The 'Character' field is null or not of type InteractionCharacter.");
            return;
        }

        if (self.character.name == "Captain")
        {
            self.State.SwitchState<InteractableCharacter.HasAbilityUnlockState>();

        }

        if (!init)
        {
            if (self.interactionToPlay != null)
            {
                self.State.SwitchState<InteractableCharacter.HasInteractionState>();
            }
            // If the character has an unlock interaction and The ability location is not unlocked
            else if (self.unlockInteraction != null && !connection!.IsLocationChecked($"{GetAbilityName(abilityName)} Purchase"))
            {
                // fix for stupid case where Daro never leaves the hub regardless of her corresponding fact
                if ((self.character.name == "Sage" && FactSystem.GetFact(new Fact("APSageInHub")) == 1f) || self.character.name != "Sage")
                {
                    self.State.SwitchState<InteractableCharacter.HasAbilityUnlockState>();
                } 
            }
        }
    }

    private static void StaticInteractableCharacterInteractHook(On.InteractableCharacter.orig_Interact orig, InteractableCharacter self)
    {
        var ability = self.character.Ability;
        var abilityName = Enum.GetName(typeof(AbilityKind), ability);

        if (self.character.HasAbilityUnlock && connection!= null && FactSystem.GetFact(new Fact("in_run")) == 0)
        {
            if ((self.character.name == "Sage" && FactSystem.GetFact(new Fact("APSageInHub")) == 1f) || self.character.name != "Sage")
            {
                connection.SendHintedLocation($"{GetAbilityName(abilityName)} Purchase");
            }
        }
        orig(self);
    }
}



// Settings For AP server connection
[HasteSetting]
public class ApEnabledSetting : BoolSetting, IExposedSetting
{
    public override LocalizedString OffString => new UnlocalizedString("AP off");

    public override LocalizedString OnString => new UnlocalizedString("AP on");

    public override void ApplyValue() => UnityMainThreadDispatcher.Instance().log($"AP Toggled AP to {Value}");
    protected override bool GetDefaultValue() => false;
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Toggle");
    public string GetCategory() => "AP";
}

[HasteSetting]
public class ApDebugEnabledSetting : BoolSetting, IExposedSetting
{
    public override LocalizedString OffString => new UnlocalizedString("Debug messages off");

    public override LocalizedString OnString => new UnlocalizedString("Debug messages on");

    public override void ApplyValue() => FactSystem.SetFact(new Fact("APDebugLogEnabled"), Value ? 1f : 0f);
    protected override bool GetDefaultValue() => false;
    public LocalizedString GetDisplayName() => new UnlocalizedString("Debug Toggle");
    public string GetCategory() => "AP Log";
}

[HasteSetting]
public class ApLogFilter : EnumSetting<APFilterMode>, IExposedSetting
{
    public override void ApplyValue() => FactSystem.SetFact(new Fact("APMessageFilter"), (float)Value);
    protected override APFilterMode GetDefaultValue() => APFilterMode.None;
    public LocalizedString GetDisplayName() => new UnlocalizedString("Text Client Filter");
    public string GetCategory() => "AP Log";

    public override List<LocalizedString> GetLocalizedChoices() => [new UnlocalizedString("All Messages"), new UnlocalizedString("Only Messages About Player"), new UnlocalizedString("Messages About Player + Chat")];

}

[HasteSetting]
public class ApLogFontSize : IntSetting, IExposedSetting
{
    public override void ApplyValue()
    {
        ApDebugLog.Instance.fontSize = Value;
    }
    protected override int GetDefaultValue() => 16;
    public LocalizedString GetDisplayName() => new UnlocalizedString("Text Client Font Size");
    public string GetCategory() => "AP Log";
}


[HasteSetting]
public class ApLogLineSpacing : IntSetting, IExposedSetting
{
    public override void ApplyValue() => ApDebugLog.Instance.lineSpacing = Value;
    protected override int GetDefaultValue() => 20;
    public LocalizedString GetDisplayName() => new UnlocalizedString("Text Client Line Spacing");
    public string GetCategory() => "AP Log";
}

[HasteSetting]
public class ApLogLocation : EnumSetting<APLogLocation>, IExposedSetting
{
    public override void ApplyValue() {
        ApDebugLog.Instance.windowPosition = Value;
        ApDebugLog.Instance.RecalculatePosition();
    }
    protected override APLogLocation GetDefaultValue() => APLogLocation.TopRight;
    public LocalizedString GetDisplayName() => new UnlocalizedString("Text Client Location");
    public string GetCategory() => "AP Log";

    public override List<LocalizedString> GetLocalizedChoices() => [
        new UnlocalizedString("Top Right"), 
        new UnlocalizedString("Middle Right"), 
        new UnlocalizedString("Bottom Right"), 
        new UnlocalizedString("Top Left"),
        new UnlocalizedString("Middle Left"),
        new UnlocalizedString("Bottom Left"),
        new UnlocalizedString("Custom"),
        ];

}

[HasteSetting]
public class ApLogXOffset : IntSetting, IExposedSetting
{
    public override void ApplyValue() {
        ApDebugLog.Instance.xCustomOffset = Value;
        ApDebugLog.Instance.RecalculatePosition();
    }
    protected override int GetDefaultValue() => -650;
    public LocalizedString GetDisplayName() => new UnlocalizedString("Custom X Offset");
    public string GetCategory() => "AP Log";
}

[HasteSetting]
public class ApLogYOffset : IntSetting, IExposedSetting
{
    public override void ApplyValue() { 
        ApDebugLog.Instance.yCustomOffset = Value;
        ApDebugLog.Instance.RecalculatePosition();
    }
    protected override int GetDefaultValue() => 150;
    public LocalizedString GetDisplayName() => new UnlocalizedString("Custom Y Offset");
    public string GetCategory() => "AP Log";
}

[HasteSetting]
public class ApLogAlignment : BoolSetting, IExposedSetting
{
    public override LocalizedString OffString => new UnlocalizedString("Left");
    public override LocalizedString OnString => new UnlocalizedString("Right");
    public override void ApplyValue() => ApDebugLog.Instance.customAlignmentMode = (Value) ? TMPro.TextAlignmentOptions.Left : TMPro.TextAlignmentOptions.Right;
    protected override bool GetDefaultValue() => false;
    public LocalizedString GetDisplayName() => new UnlocalizedString("Custom Text Alignment");
    public string GetCategory() => "AP Log";
}

[HasteSetting]
public class ApLogDirection : BoolSetting, IExposedSetting
{
    public override LocalizedString OffString => new UnlocalizedString("Messages go up");
    public override LocalizedString OnString => new UnlocalizedString("Messages go down");
    public override void ApplyValue() => ApDebugLog.Instance.customMessagesGoDown = Value;
    protected override bool GetDefaultValue() => true;
    public LocalizedString GetDisplayName() => new UnlocalizedString("Custom Message Direction");
    public string GetCategory() => "AP Log";
}

[HasteSetting]
public class ApLogTestMessage : ButtonSetting, IExposedSetting
{
    public LocalizedString GetDisplayName() => new UnlocalizedString("Text Client Example Message");
    public string GetCategory() => "AP Log";

    public override void OnClicked(ISettingHandler settingHandler)
    {
        ApDebugLog.Instance.DisplayMessage("This is a test message that is really long so that way you can make sure the text appears on screen correctly.", isDebug: false);
        ApDebugLog.Instance.RecalculatePosition();
    }

    public override string GetButtonText()
    {
        return "Show Example Message";
    }
}

[HasteSetting]
public class ApServerNameSetting : StringSetting, IExposedSetting
{
    public override void ApplyValue() => UnityMainThreadDispatcher.Instance().log($"New AP hostname {Value}");
    protected override string GetDefaultValue() => "localhost";
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Server name");
    public string GetCategory() => "AP";
}

[HasteSetting]
public class ApServerPortSetting : IntSetting, IExposedSetting
{
    public override void ApplyValue() => UnityMainThreadDispatcher.Instance().log($"New AP hostport {Value}");
    protected override int GetDefaultValue() => 38281;
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Server Port");
    public string GetCategory() => "AP";
}

[HasteSetting]
public class ApUsernameSetting : StringSetting, IExposedSetting
{
    public override void ApplyValue() => UnityMainThreadDispatcher.Instance().log($"New AP username {Value}");
    protected override string GetDefaultValue() => "Player1";
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Username");
    public string GetCategory() => "AP";
}
[HasteSetting]
public class ApPasswordSetting : StringSetting, IExposedSetting
{
    public override void ApplyValue() => UnityMainThreadDispatcher.Instance().log($"New AP Password {Value}");
    protected override string GetDefaultValue() => "";
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Password");
    public string GetCategory() => "AP";
}