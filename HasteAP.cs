using APConnection;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Landfall.Haste;
using Landfall.Modding;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using Zorro.Core.CLI;
using Zorro.Settings;
using static Integration.Integration;

[LandfallPlugin]
public partial class HasteAP
{
    private static Connection? connection;

    private static readonly Version version = new(0, 4, 0);

    static HasteAP()
    {
        UnityMainThreadDispatcher.Instance().log("AP Program launched");

        GameObject instance = new(nameof(ApDebugLog));
        UnityEngine.Object.DontDestroyOnLoad(instance);
        instance.AddComponent<ApDebugLog>();
        ApDebugLog.Instance.BuildFont();

        UnityMainThreadDispatcher.Instance().log("AP Log created");
        ApDebugLog.Instance.DisplayMessage($"Archipelago Installed v{version}", isDebug: false);

        // Load everything up when the game starts from menu
        On.GameHandler.PlayFromMenu += StaticPlayFromMenuHook;
        On.GameHandler.LoadMainMenu += StaticLoadMainMenuHook;

    }
    
    private static void StaticPlayFromMenuHook(On.GameHandler.orig_PlayFromMenu orig)
    {


        if (connection != null)
        {
            UnityMainThreadDispatcher.Instance().logError("AP Play button clicked and connection is not null, nuking everything anyway");
            ClearHooks();
            connection.Close();
            connection = null;
            ApDebugLog.Instance.DisplayMessage("Removed connection");
            FactSystem.SetFact(new Fact("APForceReloadFirstLoad"), 0f);
        }
        ApDebugLog.Instance.DisplayMessage("Beginning Build");

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


        if (FactSystem.GetFact(new Fact("APFirstLoad")) != FactSystem.GetFact(new Fact("tutorial_finished")))
        {
            UnityMainThreadDispatcher.Instance().log("AP save check mismatched. Likely a vanilla save. Aborting.");
            ApDebugLog.Instance.DisplayMessage("<color=#FF0000>Non-Archipelago savefile detected.</color>\nThis mod relies on changing many vanilla savedata behaviours and therefore cannot be played on a normal savefile. Please switch to a fresh savefile (from the 'General' settings menu) or a save that already has AP data.\nIf you believe this is in error, please contact the developer of this mod.", isDebug: false, duration: 10f);
            return;
        }

        UnityMainThreadDispatcher.Instance().log("AP enabled so beginning startup");
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
        connection.BuildMessageReciver();

        if (!connection.Connect(username, password, version))
        {
            ApDebugLog.Instance.DisplayMessage("Connection Failed");
            return;
        }
        if (FactSystem.GetFact(new Fact("APVersionMiddle")) > 3f)
        {
            // takes only the first 8 chars for the seed because floats only store so much and surely this is enough to stop conflicts
            float seedpart = Convert.ToSingle(connection.session.RoomState.Seed[..8]);
            if (FactSystem.GetFact(new Fact("APFirstLoad")) > 0f)
            {
                if (FactSystem.GetFact(new Fact("APRoomSeed")) == 0f)
                {
                    // set. it shouldnt get to this part but idk man
                    FactSystem.SetFact(new Fact("APRoomSeed"), seedpart);
                }
                else
                {
                    // compare
                    if (FactSystem.GetFact(new Fact("APRoomSeed")) != seedpart)
                    {
                        ApDebugLog.Instance.DisplayMessage($"<color=#FF0000>ERROR:</color> The seed of the Archipelago room ({seedpart}) does not match the seed stored in the save data ({FactSystem.GetFact(new Fact("APRoomSeed"))}).\nPlease select the correct save file, or report this to the mod's developer if you believe this is in error.", isDebug: false, duration: 20f);
                        connection.Close();
                        connection = null;
                        return;
                    }
                    
                }

            } else
            {
                // set
                FactSystem.SetFact(new Fact("APRoomSeed"), seedpart);
            }

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

        FactSystem.SubscribeToFact(new Fact("current_unbeaten_shard"), UnbeatedShardHandler);

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
        On.GM_API.OnStartNewRun += StaticStartofRunHook;
        On.RunHandler.PlayLevel += StaticStartOfFragmentHook;
        On.Player.Die += StaticRemoveTrapsOnDeath;
        On.PlayerCharacter.AddEnergy += StaticOnEnergyGain;
        On.Player.SetEnergy += StaticOnSetEnergy;

        On.GM_API.OnEndBossWin += StaticEndBossHook;
        On.EndBoss.GoToMainMenu += StaticEndBossForceDC;

        // When a boss is defeated send the location
        On.GM_API.OnBossDeath += StaticBossDeathHook;

        On.ShopItemHandler.BuyItem += StaticBuyItemHook;
        On.ShopItemHandler.DoClientBuyingItem += StaticBuyingItemFixHook;
        On.ShopItemHandler.GenerateItems += StaticGenerateItemHook;

        On.Landfall.Haste.SkinPurchasePopup.PurchaseSkin += StaticPurchaseSkinHook;
        On.Landfall.Haste.SkinPurchasePopup.OpenPopup += StaticPurchaseSkinOpenHook;
        On.Landfall.Haste.SkinManager.SetDefaultUnlockedSkins += StaticDefaultUnlockedSkinsHook;
        On.Landfall.Haste.SkinManager.SetFullOutfit += StaticSetFullOutfitHook;
        On.Landfall.Haste.SkinManager.HubWorldUnlockSkins += StaticHubWorldUnlockHook;

        On.SaveSystem.Load += StaticSaveLoadHook;

        On.Landfall.Haste.MetaProgressionRowUI.AddClicked += StaticMetaProgressionRowAddClickedHook;
        On.Landfall.Haste.MetaProgressionRowUI.RefreshUI += StaticMetaProgressionRefreshUIHook;
        On.Landfall.Haste.MetaProgression.GetCurrentLevel += StaticMetaProgressionGetCurrentLevelHook;
        On.Landfall.Haste.MetaProgression.IsUnlocked += StaticMetaProgressionIsUnlockedHook;
        On.Landfall.Haste.MetaProgression.Unlock += StaticMetaProgressionUnlockHook;

        On.Landfall.Haste.AbilityUnlockScreen.Unlock += StaticMetaProgressionUnlockOverloadHook;
        On.InteractableCharacter.Start += StaticInteractableCharacterStartHook;
        On.InteractableCharacter.Interact += StaticInteractableCharacterInteractHook;

        On.PlayerCharacter.RestartPlayer_Launch_Transform_float += StaticSetSpeed;
        On.PlayerCharacter.RestartPlayer_Still_Transform += StaticSetSpeed;
        On.PlayerMovement.Land += StaticLandingHook;


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
        On.GM_API.OnStartNewRun -= StaticStartofRunHook;
        On.RunHandler.PlayLevel -= StaticStartOfFragmentHook;
        On.Player.Die -= StaticRemoveTrapsOnDeath;
        On.PlayerCharacter.AddEnergy -= StaticOnEnergyGain;
        On.Player.SetEnergy -= StaticOnSetEnergy;
        On.GM_API.OnEndBossWin -= StaticEndBossHook;
        On.GM_API.OnBossDeath -= StaticBossDeathHook;
        On.EndBoss.GoToMainMenu -= StaticEndBossForceDC;
        On.ShopItemHandler.BuyItem -= StaticBuyItemHook;
        On.ShopItemHandler.DoClientBuyingItem -= StaticBuyingItemFixHook;
        On.ShopItemHandler.GenerateItems -= StaticGenerateItemHook;
        On.Landfall.Haste.SkinPurchasePopup.PurchaseSkin -= StaticPurchaseSkinHook;
        On.Landfall.Haste.SkinPurchasePopup.OpenPopup -= StaticPurchaseSkinOpenHook;
        On.Landfall.Haste.SkinManager.SetDefaultUnlockedSkins -= StaticDefaultUnlockedSkinsHook;
        On.Landfall.Haste.SkinManager.SetFullOutfit -= StaticSetFullOutfitHook;
        On.Landfall.Haste.SkinManager.HubWorldUnlockSkins -= StaticHubWorldUnlockHook;
        On.SaveSystem.Load -= StaticSaveLoadHook;
        On.Landfall.Haste.AbilityUnlockScreen.Unlock -= StaticMetaProgressionUnlockOverloadHook;
        On.InteractableCharacter.Start -= StaticInteractableCharacterStartHook;
        On.InteractableCharacter.Interact -= StaticInteractableCharacterInteractHook;
        On.GM_API.OnSpawnedInHub -= StaticLoadHubHook;
        On.PlayerCharacter.RestartPlayer_Launch_Transform_float -= StaticSetSpeed;
        On.PlayerCharacter.RestartPlayer_Still_Transform -= StaticSetSpeed;
        On.PlayerMovement.Land -= StaticLandingHook;
        On.Landfall.Haste.MetaProgressionRowUI.AddClicked -= StaticMetaProgressionRowAddClickedHook;
        On.Landfall.Haste.MetaProgressionRowUI.RefreshUI -= StaticMetaProgressionRefreshUIHook;
        On.Landfall.Haste.MetaProgression.GetCurrentLevel -= StaticMetaProgressionGetCurrentLevelHook;
        On.Landfall.Haste.MetaProgression.IsUnlocked -= StaticMetaProgressionIsUnlockedHook;
        On.Landfall.Haste.MetaProgression.Unlock -= StaticMetaProgressionUnlockHook;
        if (FactSystem.GetFact(new Fact("APDeathlink")) == 1f)
        {
            On.Player.Die -= StaticSendDeathOnDie;
            connection.deathLinkService!.OnDeathLinkReceived -= GiveDeath;
            connection.deathLinkService!.DisableDeathLink();

        }
        FactSystem.UnsubscribeFromFact(new Fact("current_unbeaten_shard"), UnbeatedShardHandler);
        UnityMainThreadDispatcher.Instance().log("AP Hooks Removed");
        ApDebugLog.Instance.DisplayMessage("AP Hooks Removed");
    }

    private static void UnbeatedShardHandler(float value)
    {
        ApDebugLog.Instance.DisplayMessage($"unbeaten shard update to (zero index){value}");
        if (connection == null)
            return;

        var shard_count = connection.GetItemCount("Progressive Shard");
        // make sure to cap the current_unbeaten to the correct value so that the extra Prog Shards dont make a bug happen with an invalid state
        var shard_cap = FactSystem.GetFact(new Fact("APRemovePVL")) == 0f ? 9 : FactSystem.GetFact(new Fact("APShardGoal")) - 1f;
        // Never allow the value to be not what I want it to be
        if (FactSystem.GetFact(new Fact("APShardUnlockOrder")) == 1f)
        {
            var newvalue = Math.Min(shard_count, (int)FactSystem.GetFact(new Fact("APBossDefeated")));
            // if unlock-order is Boss-locked, then only unlock up until the lowest of either bossbeated or shards
            if (value != newvalue) FactSystem.SetFact(new Fact("current_unbeaten_shard"), Math.Min(newvalue, shard_cap));
        }
        else
        {
            if (value != shard_count) FactSystem.SetFact(new Fact("current_unbeaten_shard"), Math.Min(shard_count, shard_cap));
        }
    }


    private static void StaticLandingHook(On.PlayerMovement.orig_Land orig, PlayerMovement self, object landing)
    {
        if (FactSystem.GetFact(new Fact("APLandingTrapIsActive")) >= 1f)
        {
            PlayerMovement.Landing? ll = landing as PlayerMovement.Landing;
            float difficultyLandingMod = Mathf.Lerp(-0.05f, 0f, GameDifficulty.currentDif.landingPresicion);

            if (ll.landingScore >= difficultyLandingMod + 0.95f)
            {
                // downgrade perfect to good
                UnityMainThreadDispatcher.Instance().log("Attempting landing correction to Good");
                ll.landingScore =  difficultyLandingMod + 0.94f;
            } else if (ll.landingScore >= difficultyLandingMod + 0.9f)
            {
                // downgrade good to ok
                UnityMainThreadDispatcher.Instance().log("Attempting landing correction to Okay");
                ll.landingScore =  difficultyLandingMod + 0.89f;
            } else
            {
                //downgrade ok to bad
                UnityMainThreadDispatcher.Instance().log("Attempting landing correction to Bad");
                ll.landingScore =  difficultyLandingMod + 0.79f;
            }
        }
        orig(self, landing);
    }

    private static void StaticOnEnergyGain(On.PlayerCharacter.orig_AddEnergy orig, PlayerCharacter self, float added, EffectSource source)
    {
        // ideally, these just permanently keep the energy at 0 unless you have an ability
        if (FactSystem.GetFact(new Fact("APNoAbility")) == 1f)
        {
            // backup function to unbork some things
            if (!MetaProgression.IsUnlocked(AbilityKind.BoardBoost) && !MetaProgression.IsUnlocked(AbilityKind.Fly) &&
                !MetaProgression.IsUnlocked(AbilityKind.Grapple) &&
                !MetaProgression.IsUnlocked(AbilityKind.Slomo)) return;
            FactSystem.SetFact(new Fact("APNoAbility"), 0f);
            orig(self, added, source);
        } else
        {
            orig(self, added, source);
        }
    }

    private static void StaticOnSetEnergy(On.Player.orig_SetEnergy orig, Player self, float amount)
    {
        // ideally, these just permanently keep the energy at 0 unless you have an ability
        if (FactSystem.GetFact(new Fact("APNoAbility")) == 1f)
        {
            // backup function to unbork some things
            if (!MetaProgression.IsUnlocked(AbilityKind.BoardBoost) && !MetaProgression.IsUnlocked(AbilityKind.Fly) &&
                !MetaProgression.IsUnlocked(AbilityKind.Grapple) &&
                !MetaProgression.IsUnlocked(AbilityKind.Slomo)) return;
            FactSystem.SetFact(new Fact("APNoAbility"), 0f);
            orig(self, amount);
        }
        else
        {
            orig(self, amount);
        }
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
        if (FactSystem.GetFact(new Fact("APDeathSendMode")) == (float)DeathLinkMode.OnDeath)
        {
            connection!.SendDeath();
        }
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
        if (FactSystem.GetFact(new Fact("APDeathlink")) == 1f &&
            FactSystem.GetFact(new Fact("APDeathSendMode")) == (float) DeathLinkMode.OnShardFail &&
            state is RunHandler.LastRunState.Lose or RunHandler.LastRunState.LoseBad)
        {
            ApDebugLog.Instance.DisplayMessage("Shard Failed with state "+state);
            connection!.SendDeath();
        }

        UpdateShardCount();
        orig(state);
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
        // TODO: find better way to handle this because RunHandler isnt reliable in the post-fragment
        if (FactSystem.GetFact(new Fact("APFragmentsanity")) > 0f && RunHandler.IsInLevelOrChallenge())
        {
            string? locationName; // location to send
            string? FragKeyword; // suffix to put on everything
            if (FactSystem.GetFact(new Fact("APFragmentsanity")) == 1f)
            {
                var currentShard = RunHandler.RunData.shardID + 1;
                locationName = $"Shard {currentShard} Fragment Clear ";
                FragKeyword = "Shard" + currentShard;
            }
            else
            {
                locationName = "Global Fragment Clear ";
                FragKeyword = "Global";
            }

            if (FactSystem.GetFact(new Fact("APFragmentsanityLocation" + FragKeyword)) < FactSystem.GetFact(new Fact("APFragmentsanityQuantity")))
            {
                FactSystem.AddToFact(new Fact("APFragmentsanity" + FragKeyword), 1f);
                // bonus point for S-Rank
                //ApDebugLog.Instance.DisplayMessage($"Fragment cleared with rank {RunHandler.currentTier}");
                //if (FactSystem.GetFact(new Fact("APSRankBonus")) == 1f && RunHandler.currentTier == 0)
                //{
                //    FactSystem.AddToFact(new Fact("APFragmentsanity" + FragKeyword), 1f);
                //}
                ApDebugLog.Instance.DisplayMessage($"Completed Fragment. Progress: {FactSystem.GetFact(new Fact("APFragmentsanity" + FragKeyword))}/{FactSystem.GetFact(new Fact("APFragmentLimit" + FragKeyword))} for Clear {Convert.ToInt32(FactSystem.GetFact(new Fact("APFragmentsanityLocation" + FragKeyword))) + 1}", isDebug: false);
                if (FactSystem.GetFact(new Fact("APFragmentsanity" + FragKeyword)) >= FactSystem.GetFact(new Fact("APFragmentLimit" + FragKeyword)))
                {
                    connection.SendLocation(locationName + (Convert.ToInt32(FactSystem.GetFact(new Fact("APFragmentsanityLocation" + FragKeyword))) + 1).ToString("D2"));
                    FactSystem.AddToFact(new Fact("APFragmentsanityLocation" + FragKeyword), 1f);
                    FactSystem.SetFact(new Fact("APFragmentsanity" + FragKeyword), 0f);
                    SetFragmentLimits(FragKeyword);
                }
            }

        }
        DecrementTraps();
        orig(player);
    }


    private static void StaticRemoveTrapsOnDeath(On.Player.orig_Die orig, Player self)
    {
        UnityMainThreadDispatcher.Instance().log($"Attempting to remove traps on death");
        DecrementTraps();
        orig(self);
    }


    private static void StaticStartOfFragmentHook(On.RunHandler.orig_PlayLevel orig)
    {
        orig();
        // eval
        UnityMainThreadDispatcher.Instance().log($"Start of fragment, evaluating traps");
        if (FactSystem.GetFact(new Fact("APQueuedDisasterTraps")) == 0f && FactSystem.GetFact(new Fact("APDisasterTrapIsActive")) == 1f && FactSystem.GetFact(new Fact("APDisasterTrapActive")) == 0f)
        {
            RemoveTrap(TrapsList.Disaster);
        }
        if (FactSystem.GetFact(new Fact("APQueuedDisasterTraps")) >= 1f && FactSystem.GetFact(new Fact("APDisasterTrapActive")) == 0f && FactSystem.GetFact(new Fact("APDisasterTrapIsActive")) == 0f)
        {
            ActivateTrap(TrapsList.Disaster);
        }
        if (FactSystem.GetFact(new Fact("APQueuedDisasterTraps")) >= 1f)
        {
            FactSystem.AddToFact(new Fact("APQueuedDisasterTraps"), -1f);
        }


        if (FactSystem.GetFact(new Fact("APQueuedLandingTraps")) == 0f && FactSystem.GetFact(new Fact("APLandingTrapIsActive")) == 1f && FactSystem.GetFact(new Fact("APLandingTrapActive")) == 0f)
        {
            RemoveTrap(TrapsList.Landing);
        }
        if (FactSystem.GetFact(new Fact("APQueuedLandingTraps")) >= 1f && FactSystem.GetFact(new Fact("APLandingTrapActive")) == 0f && FactSystem.GetFact(new Fact("APLandingTrapIsActive")) == 0f)
        {
            ActivateTrap(TrapsList.Landing);
        }
        if (FactSystem.GetFact(new Fact("APQueuedLandingTraps")) >= 1f)
        {
            FactSystem.AddToFact(new Fact("APQueuedLandingTraps"), -1f);
        }
    }

    private static void ActivateTrap(TrapsList traptype)
    {
        UnityMainThreadDispatcher.Instance().log($"Attempting to activate trap {traptype}");
        switch (traptype)
        {
            case TrapsList.Disaster:
                // i betcha there'll be some dumb shit i'm gonna need to fix when I eventually add the disaster shards as goals
                if (FactSystem.GetFact(new Fact("APDisasterTrapActive")) == 0f)
                {
                    UnityMainThreadDispatcher.Instance().log($"Attempting to add disaster trap");
                    FactSystem.SetFact(new Fact("APDisasterTrapActive"), 1f);
                    FactSystem.SetFact(new Fact("APDisasterTrapIsActive"), 1f);
                    ItemInstance DisasterItem = ItemDatabase.instance.items.Where(x => x.itemName == "MinorItem_Ascension_Level1").ToList()[0];
                    Player.localPlayer.AddItem(DisasterItem);
                }
                break;
            case TrapsList.Landing:
                if (FactSystem.GetFact(new Fact("APLandingTrapActive")) == 0f)
                {
                    UnityMainThreadDispatcher.Instance().log($"Attempting to add landing trap");
                    FactSystem.SetFact(new Fact("APLandingTrapActive"), 1f);
                    FactSystem.SetFact(new Fact("APLandingTrapIsActive"), 1f);
                }
                break;
            default:
                break;
        }
    }

    private static void DecrementTraps()
    {
        UnityMainThreadDispatcher.Instance().log($"Decrementing traps");
        if (FactSystem.GetFact(new Fact("APDisasterTrapActive")) >= 1f)
        {
            FactSystem.AddToFact(new Fact("APDisasterTrapActive"), -1f);
        }
        if (FactSystem.GetFact(new Fact("APLandingTrapActive")) >= 1f)
        {
            FactSystem.AddToFact(new Fact("APLandingTrapActive"), -1f);
        }
    }

    private static void RemoveTrap(TrapsList traptype)
    {
        UnityMainThreadDispatcher.Instance().log($"Attempting to remove trap {traptype}");
        try
        {
            switch (traptype)
            {
                case TrapsList.Disaster:
                    FactSystem.SetFact(new Fact("APDisasterTrapIsActive"), 0f);
                    ItemInstance DisasterItem = Player.localPlayer.items.Where(x => x.itemName == "MinorItem_Ascension_Level1").ToList()[0];
                    Player.localPlayer.RemoveItem(DisasterItem);
                    break;
                case TrapsList.Landing:
                    FactSystem.SetFact(new Fact("APLandingTrapIsActive"), 0f);
                    break;
                default:
                    break;
            }
        }
        catch (Exception e)
        {
            UnityMainThreadDispatcher.Instance().log($"Error in removing traps: {e.Message},{e.InnerException},{e.StackTrace}");
            ApDebugLog.Instance.DisplayMessage($"Error in removing traps:\n {e.Message},{e.InnerException},{e.StackTrace}", duration: 10f, isDebug:false);
        }
    }

    private static void StaticStartofRunHook(On.GM_API.orig_OnStartNewRun orig)
    {
        // my base instincts tell me I should block these off if you dont have the setting enabled, but my third eye foresees someone having them as starting items regardless and asking why they dont work
        GenerateRandomStartingItems(Rarity.Common, APItemCategory.Speed, FactSystem.GetFact(new Fact("APCommonSpeedItems")));
        GenerateRandomStartingItems(Rarity.Common, APItemCategory.Support, FactSystem.GetFact(new Fact("APCommonSupportItems")));
        GenerateRandomStartingItems(Rarity.Common, APItemCategory.Health, FactSystem.GetFact(new Fact("APCommonHealthItems")));
        GenerateRandomStartingItems(Rarity.Rare, APItemCategory.Speed, FactSystem.GetFact(new Fact("APRareSpeedItems")));
        GenerateRandomStartingItems(Rarity.Rare, APItemCategory.Support, FactSystem.GetFact(new Fact("APRareSupportItems")));
        GenerateRandomStartingItems(Rarity.Rare, APItemCategory.Health, FactSystem.GetFact(new Fact("APRareHealthItems")));
        GenerateRandomStartingItems(Rarity.Epic, APItemCategory.Speed, FactSystem.GetFact(new Fact("APEpicSpeedItems")));
        GenerateRandomStartingItems(Rarity.Epic, APItemCategory.Support, FactSystem.GetFact(new Fact("APEpicSupportItems")));
        GenerateRandomStartingItems(Rarity.Epic, APItemCategory.Health, FactSystem.GetFact(new Fact("APEpicHealthItems")));
        GenerateRandomStartingItems(Rarity.Legendary, APItemCategory.Legendary, FactSystem.GetFact(new Fact("APLegendaryItems")));
        orig();
    }

    public static void GenerateRandomStartingItems(Rarity rarity, APItemCategory category, float q)
    {
        if (q == 0f) { return; }
        UnityMainThreadDispatcher.Instance().log($"Attempting to generate {q} {rarity} {category}");
        List<ItemInstance> itemlist = [];
        List<string> referenceTable = [];
        switch (category)
        {
            case APItemCategory.Speed:
                referenceTable = ItemCategories.ItemCategories.SpeedItems;
                break;
            case APItemCategory.Support:
                referenceTable = ItemCategories.ItemCategories.SupportItems;
                break;
            case APItemCategory.Health:
                referenceTable = ItemCategories.ItemCategories.HealthItems;
                break;
            default:
                break;
        }

        foreach (ItemInstance ii in ItemDatabase.instance.items)
        {
            // legendary items dont have an exclusion table, and just get them all
            if (category != APItemCategory.Legendary) {
                if (!referenceTable.Contains(ii.itemName)) { continue; }
            }
            if (ii.triggerType == ItemTriggerType.Active && FactSystem.GetFact(new Fact("APPersistentItems")) == 2f) { continue; }
            if (ItemDatabase.ItemIsAcceptable(ii, TagInteraction.None, null, GetRandomItemFlags.Major, null))
            {
                itemlist.Add(ii);
            }
        }

        if (itemlist.Count == 0)
        {
            ApDebugLog.Instance.DisplayMessage($"<color=#FFFF00>WARNING:</color> Could not generate {rarity} {((category != APItemCategory.Legendary) ? category : string.Empty)} item before run start due to there being no available items in that pool unlocked. Skipping.", duration: 10f, isDebug: false);
            return;
        }

        // loop for q, select q items and add to player
        System.Random random = new();
        for (int i = 0; i < (int)q; i++)
        {
            ItemInstance newitem = ItemDatabase.SelectItemWithRarity(itemlist, rarity, random);
            if (newitem != null)
            {
                FactSystem.SetFact(new Fact($"{newitem.itemName}_ShowItem"), 1f);
                Player.localPlayer.AddItem(newitem);
            } else
            {
                ApDebugLog.Instance.DisplayMessage($"<color=#FF0000>ERROR:</color> Could not generate {rarity} {category} item before run start for a reason unrelated to item pool size.\nYou should probably report this to the developer of the mod.", duration: 10f, isDebug: false);
            }
        }
    }

    public static void SetFragmentLimits(string keyword)
    {
        if (FactSystem.GetFact(new Fact("APFragmentsanityDistribution")) == 2f)
        {
            // if MODE H-TRI
            // LIMIT = floor(FRAGMENTLOCATION / 2)
            FactSystem.SetFact(new Fact($"APFragmentLimit{keyword}"), Math.Max((float)Math.Floor((FactSystem.GetFact(new Fact($"APFragmentsanityLocation{keyword}")) + 1) / 2), 1f));
        }
        else if (FactSystem.GetFact(new Fact("APFragmentsanityDistribution")) == 3f)
        {
            // if MODE BALANCED HALF-TRI
            // LIMIT = min( floor(FRAGMENTLOCATION / 2), 10
            FactSystem.SetFact(new Fact($"APFragmentLimit{keyword}"), Math.Max(Math.Min((float)Math.Floor((FactSystem.GetFact(new Fact($"APFragmentsanityLocation{keyword}")) + 1) / 2), 10f), 1f));
        }
        else if (FactSystem.GetFact(new Fact("APFragmentsanityDistribution")) == 4f)
        {
            // if MODE TRI
            // LIMIT = FRAGMENTLOCATION
            FactSystem.SetFact(new Fact($"APFragmentLimit{keyword}"), FactSystem.GetFact(new Fact($"APFragmentsanityLocation{keyword}")) + 1);
        }

        // failsafe
        if (FactSystem.GetFact(new Fact($"APFragmentLimit{keyword}")) == 0f)
        {
            FactSystem.SetFact(new Fact($"APFragmentLimit{keyword}"), 1f);
        }
    }


    private static void StaticGenerateItemHook(On.ShopItemHandler.orig_GenerateItems orig, ShopItemHandler self)
    {
        if (FactSystem.GetFact(new Fact("APShopsanitySeperate")) == 1f)
        {
            ApDebugLog.Instance.DisplayMessage($"Static Generate Item Hook");
            try
            {
                Player localPlayer = Player.localPlayer;
                System.Random currentLevelRandomInstance = RunHandler.GetCurrentLevelRandomInstance(self.rerolls);
                List<ItemInstance> excludedList = new List<ItemInstance>();
                foreach (ItemInstance itemInstance in self.itemInstances)
                {
                    if (itemInstance)
                    {
                        excludedList.Add(itemInstance);
                    }
                }
                int num = Mathf.Max(self.itemInstances.Count, self.itemVisuals.Count);
                for (int i = 0; i < num; i++)
                {
                    self.Unset(i);
                }

                for (int j = 0; j < 3; j++)
                {
                    ItemInstance? itemToAddToShop = null;
                    System.Random random = new System.Random();
                    //TODO: determine if there are no AP items left in that region, then make it spawn a normal item instead
                    // unless the inX is 100, then just let them keep getting useless items I guess (maybe another dummy item)
                    int inX = (int)FactSystem.GetFact(new Fact("APShopsanitySeperateRate"));
                    if (random.Next(1, 100) <= inX)
                    {
                        var ItemsThatShouldBeAPShop = ItemDatabase.instance.items.Where(x => x.itemName == "ArchipelagoShopItem").ToList();
                        if (ItemsThatShouldBeAPShop.Count > 0)
                        {
                            itemToAddToShop = ItemDatabase.instance.items.Where(x => x.itemName == "ArchipelagoShopItem").ToList()[0];
                        }
                        else
                        {
                            ApDebugLog.Instance.DisplayMessage($"<color=#FF0000>ERROR:</color> Could not find APShopItem in ItemDatabase, wtf is happening here.");
                        }
                    }
                    else
                    {
                        itemToAddToShop = self.testItem ? self.testItem : ItemDatabase.GetRandomItem(localPlayer, currentLevelRandomInstance, GetRandomItemFlags.Major, TagInteraction.None, null, excludedList, RunHandler.GetShopItemRarityModifier());
                    }
                    if (itemToAddToShop != null)
                    {
                        // normal items
                        if (itemToAddToShop.itemName != "ArchipelagoShopItem") excludedList.Add(itemToAddToShop);
                        self.itemInstances.Add(itemToAddToShop);
                    }
                }
            }
            catch (Exception e)
            {
                UnityMainThreadDispatcher.Instance().log($"Error in generating shop items {e.Message},{e.InnerException},{e.StackTrace}");
                ApDebugLog.Instance.DisplayMessage($"Error in generating shop items:\n {e.Message},{e.InnerException},{e.StackTrace}", duration: 10f, isDebug:false);
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

        // manually calculating the index of the item selected so it doesnt buy a different item with the same name later in the buying process
        int selIndex = -1;
        for (int i = 0; i < self.itemInstances.Count; i++)
        {
            if ((bool)self.itemInstances[i] && self.itemInstances[i].itemName == item.itemName && self.itemVisuals[i].CheckSelected())
            {
                selIndex = i;
            }
        }
        FactSystem.SetFact(new Fact("APTempShopIndex"), (float)selIndex);
        // Buy the item
        orig(self, item, price);
    }


    private static bool StaticBuyingItemFixHook(On.ShopItemHandler.orig_DoClientBuyingItem orig, ShopItemHandler self, string itemName)
    {
        // duplication fix only matters for AP items
        if (itemName == "ArchipelagoShopItem")
        {
            if (FactSystem.GetFact(new Fact("APTempShopIndex")) == -1f){ return false; }
            self.Unset((int)(FactSystem.GetFact(new Fact("APTempShopIndex"))));
            FactSystem.SetFact(new Fact("APTempShopIndex"), 0f);
            return true;
        } else
        {
            return orig(self, itemName);
        }
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
        SaveSystem.Save();

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

    private static void StaticEndBossForceDC(On.EndBoss.orig_GoToMainMenu orig, EndBoss self)
    {
        if (connection != null)
        {
            ClearHooks();
            connection.Close();
            connection = null;
            ApDebugLog.Instance.DisplayMessage("Removed connection");
            FactSystem.SetFact(new Fact("APForceReloadFirstLoad"), 0f);
        }
        orig(self);
    }

    private static void StaticMetaProgressionUnlockOverloadHook(On.Landfall.Haste.AbilityUnlockScreen.orig_Unlock orig, AbilityUnlockScreen self)
    {

        if (connection == null)
        {
            ApDebugLog.Instance.DisplayMessage("AP On Progession Unlock the connection is null");
            return;
        }

        if (self.Character == null)
        {
            ApDebugLog.Instance.DisplayMessage("The 'Character' field is null or not of type InteractionCharacter.");
            return;
        }
        var ability_name = Enum.GetName(typeof(AbilityKind), self.Character.Ability);
        var ability_location = $"{GetAbilityName(ability_name)} Purchase";
        UnityMainThreadDispatcher.Instance().log("AP sending Ability: (" + ability_location + ")");
        ApDebugLog.Instance.DisplayMessage($"Ability Got: {ability_name}");
        var cost = MetaProgression.Instance.GetEntry(self.Character.Ability).cost;
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

            ValueTuple<int, bool, bool> costToUpgrade = self._entry.GetCostToUpgrade();
            int item = costToUpgrade.Item1;
            if (!costToUpgrade.Item2)
            {
                if (self.sfx && self.sfx.noMoney)
                {
                    self.sfx.noMoney.Play();
                }
                return;
            }
            if (self.sfx)
            {
                if (self.sfx.press)
                {
                    self.sfx.press.Play();
                }
                if (self.sfx.increase)
                {
                    self.sfx.increase.sfxs[0].settings.pitch = 1f + (float)item / 2000f;
                    self.sfx.increase.Play();
                }
            }
            connection.SendLocation(GetCaptainUpgradeName(self.kind.ToString(), self._entry.CurrentLevel));
            int? nextLevel = self._entry.GetNextLevel(self._entry.CurrentLevel);
            if (nextLevel != null)
            {
                int valueOrDefault = nextLevel.GetValueOrDefault();
                self._entry.CurrentLevel = valueOrDefault;
            }
            else
            {
                ApDebugLog.Instance.DisplayMessage(string.Format("Error: could not get next level for {0} (currernt level = {1}). This shouldn't happen due to checks above.", self._entry.fact, self._entry.CurrentLevel));
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


    private static MetaProgression.EntryLevel StaticMetaProgressionGetCurrentLevelHook(On.Landfall.Haste.MetaProgression.orig_GetCurrentLevel orig, MetaProgression.Entry entry)
    {
        if (FactSystem.GetFact(new Fact("APCaptainsRewards")) == 1f)
        {
            return entry.levels[Math.Min((int)FactSystem.GetFact(new Fact(ConvertCaptainsInternalName(entry.fact))), entry.levels.Length-1)];
        }
        else
        {
            return orig(entry);
        }

    }

    private static bool StaticMetaProgressionIsUnlockedHook(On.Landfall.Haste.MetaProgression.orig_IsUnlocked orig, AbilityKind abilityKind)
    {
        if (abilityKind == AbilityKind.BoardBoost)
        {
            return FactSystem.GetFact(new Fact("APBoostUnlocked")) > 0f;
        }
        return orig(abilityKind);
    }

    private static void StaticMetaProgressionUnlockHook(On.Landfall.Haste.MetaProgression.orig_Unlock orig, AbilityKind abilityKind)
    {
        // this is so the mod hook can still call this function, but the Integration file can call the """Secret""" function before the hooks are set up
        SecretAbilityUnlock(abilityKind);
    }

    public static void SecretAbilityUnlock(AbilityKind abilityKind)
    {
        if (FactSystem.GetFact(new Fact("APNoAbility")) == 1f)
        {
            FactSystem.SetFact(new Fact("active_ability"), Convert.ToSingle(abilityKind));
            FactSystem.SetFact(new Fact("APNoAbility"), 0f);
        }
        switch (abilityKind)
        {
            case AbilityKind.BoardBoost:
                FactSystem.SetFact(new Fact("APBoostUnlocked"), 1f);
                break;
            case AbilityKind.Slomo:
                FactSystem.SetFact(MetaProgression.SlomoUnlocked, 1f);
                break;
            case AbilityKind.Grapple:
                FactSystem.SetFact(MetaProgression.GrappleUnlocked, 1f);
                break;
            case AbilityKind.Fly:
                FactSystem.SetFact(MetaProgression.FlyUnlocked, 1f);
                break;
            default:
                throw new ArgumentOutOfRangeException("abilityKind", abilityKind, null);
        }
        if (MetaProgression.IsUnlocked(AbilityKind.BoardBoost) && MetaProgression.IsUnlocked(AbilityKind.Fly) && MetaProgression.IsUnlocked(AbilityKind.Grapple) && MetaProgression.IsUnlocked(AbilityKind.Slomo))
        {
            SkinManager.UnlockSkin(SkinManager.Skin.Wobbler, true);
        }
    }

    private static void StaticHubWorldUnlockHook(On.Landfall.Haste.SkinManager.orig_HubWorldUnlockSkins orig)
    {
        SkinManager.CompleteRunUnlockSkins((int)FactSystem.GetFact(new Fact("current_unbeaten_shard")) - 1);
        if (FactSystem.GetFact(new Fact("Has_Won_Game")) > 0f)
        {
            SkinManager.UnlockSkin(SkinManager.Skin.Crispy, true);
        }
        if (MetaProgression.IsUnlocked(AbilityKind.BoardBoost) && MetaProgression.IsUnlocked(AbilityKind.Fly) && MetaProgression.IsUnlocked(AbilityKind.Grapple) && MetaProgression.IsUnlocked(AbilityKind.Slomo))
        {
            SkinManager.UnlockSkin(SkinManager.Skin.Wobbler, true);
        }
        if (FactSystem.GetFact(new Fact("Current_UnlockedAscensionLevel")) >= 2f)
        {
            SkinManager.UnlockSkin(SkinManager.Skin.DarkClown, true);
        }
    }

    private static void StaticMetaProgressionRefreshUIHook(On.Landfall.Haste.MetaProgressionRowUI.orig_RefreshUI orig, MetaProgressionRowUI self)
    {
        orig(self);
        if (FactSystem.GetFact(new Fact("APCaptainsRewards")) == 1f)
        {
            // grab StatType text from parent gameobject
            Transform StatType = self.gameObject.transform.Find("StatType");
            // hide old text
            Transform Amount = self.gameObject.transform.Find("Amount");
            Amount.gameObject.SetActive(false);

            var rowText = StatType.gameObject.GetComponent<TextMeshProUGUI>();
            //ApDebugLog.Instance.DisplayMessage($"chaning data for row {rowText.text} of obj {rowText.GetInstanceID()}");


            var locationData = connection.RetrieiveLocationData(GetCaptainUpgradeName(self.kind.ToString(), Math.Min(self._entry.CurrentLevel, self._entry.levels.Length-2)));
            rowText.text = $"{locationData.Item1} for {locationData.Item2}";
            rowText.rectTransform.offsetMax = new Vector2(-248f, rowText.rectTransform.offsetMax.y);
            //ApDebugLog.Instance.DisplayMessage($"changes complete for {rowText.text} of obj {rowText.GetInstanceID()}");
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
            connection.SendLocation($"Costume Purchase: {GetFashionPurchaseName(self.skin.Skin)}");
        }
        orig(self);
    }

    private static void StaticPurchaseSkinOpenHook(On.Landfall.Haste.SkinPurchasePopup.orig_OpenPopup orig, SkinPurchasePopup self, SkinDatabaseEntry skin)
    {
        orig(self, skin);
        if (FactSystem.GetFact(new Fact("APFashionPurchases")) >= 1f)
        {
            if (connection == null)
            {
                ApDebugLog.Instance.DisplayMessage("AP On Skin Popup Open the connection is null");
                return;
            };
            var locationData = connection.RetrieiveLocationData($"Costume Purchase: {GetFashionPurchaseName(self.skin.Skin)}");
            self.headerLabel.text = $"{locationData.Item1} for {locationData.Item2}";
        }
    }


    private static void StaticSetFullOutfitHook(On.Landfall.Haste.SkinManager.orig_SetFullOutfit orig, SkinManager.Skin skin)
    {
        if (FactSystem.GetFact(new Fact("APFashionPurchases")) == 0f)
        {
            // dont set costume when purchasing a check (so players can keep their YAML defaults)
            orig(skin);
        }
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
        if (!connection!.IsLocationChecked($"{GetAbilityName(abilityName)} Purchase") || self.character.name == "Captain") orig(self);
    }



    [ConsoleCommand]
    public static void ResetFragmentsanity(int shard = 0)
    {
        if (FactSystem.GetFact(new Fact("APFragmentsanity")) == 2f){
            //reset globals
            if (FactSystem.GetFact(new Fact("APFragmentsanityDistribution")) != 1f) { FactSystem.SetFact(new Fact($"APFragmentLimitGlobal"), 1f); }
            FactSystem.SetFact(new Fact($"APFragmentsanityGlobal"), 0f);
            FactSystem.SetFact(new Fact($"APFragmentsanityLocationGlobal"), 0f);
        } else
        {
            if (shard is < 0 or > 10) { UnityMainThreadDispatcher.Instance().logError($"Given value {shard} is not between 1 and 10"); return; }
            // only reset limit for non-linear dist
            if (FactSystem.GetFact(new Fact("APFragmentsanityDistribution")) != 1f) { FactSystem.SetFact(new Fact($"APFragmentLimitShard{shard}"), 1f); }
            FactSystem.SetFact(new Fact($"APFragmentsanityShard{shard}"), 0f);
            FactSystem.SetFact(new Fact($"APFragmentsanityLocationShard{shard}"), 0f);
        }
    }

    [ConsoleCommand]
    public static void ResetShopsanity(int shard = 0)
    {
        if (FactSystem.GetFact(new Fact("APShopsanity")) == 2f)
        {
            //reset globals
            FactSystem.SetFact(new Fact($"APShopsanityGlobal"), 0f);
        }
        else
        {
            if (shard is < 0 or > 10) { UnityMainThreadDispatcher.Instance().logError($"Given value {shard} is not between 1 and 10"); return; }
            FactSystem.SetFact(new Fact($"APShopsanityShard{shard}"), 0f);
        }
    }

    [ConsoleCommand]
    public static void JacobAsked()
    {
        string thedump = FactSystem.GetSerializedFacts().Aggregate("", (current, fact) => current + $"{fact.Key} :: {fact.Value}\n");
        // dumps into haste directory on steam
        File.WriteAllText($"JacobAskedOn_{DateTime.Now:MM-dd-yyyy_hh-mm-ss}.txt", thedump, new UTF8Encoding());
        UnityMainThreadDispatcher.Instance().log($"Savedata information has been saved to your Haste directory. Go send that file to Jacob, who asked for this.\nTo find this directory go to Haste in your Steam Library, Right Click -> Manage -> Browse Local Files");
    }

    //[ConsoleCommand]
    //public static void DumpItemData()
    //{
    //    string thedump = "";
    //    foreach (ItemInstance item in ItemDatabase.instance.items)
    //    {
    //        thedump += $"{item.title.GetLocalizedString()}, {item.itemName}, {item.triggerType}\n";
    //    }
    //    // dumps into haste directory on steam
    //    File.WriteAllText("hastedump.txt", thedump);
    //}

    #if DEBUG
    // my debug commands, not yours
    [ConsoleCommand]
    public static void SendLocation(string loc)
    {
        connection.SendLocation(loc);
    }
    
    
    [ConsoleCommand]
    public static void ImportSaveData(string path)
    {
        // reads all lines from input and sets them as my own savedata
        var lines = File.ReadLines(path, new UTF8Encoding());
        foreach (var line in lines)
        {
            var splits =  line.Split(" :: ");
            FactSystem.SetFact(new Fact(splits[0]), float.Parse(splits[1]));
        }
    }
    #endif
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
    protected override APLogLocation GetDefaultValue() => APLogLocation.TopLeft;
    public LocalizedString GetDisplayName() => new UnlocalizedString("Text Client Location");
    public string GetCategory() => "AP Log";

    public override List<LocalizedString> GetLocalizedChoices() => [
        new UnlocalizedString("Top Left"), 
        new UnlocalizedString("Middle Left"), 
        new UnlocalizedString("Bottom Left"), 
        new UnlocalizedString("Top Right"),
        new UnlocalizedString("Middle Right"),
        new UnlocalizedString("Bottom Right"),
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
    protected override string GetDefaultValue() => "archipelago.gg";
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Server Name");
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

[HasteSetting]
public class ApSendDeathModeSetting : EnumSetting<DeathLinkMode>, IExposedSetting
{
    public override void ApplyValue()
    {
        UnityMainThreadDispatcher.Instance().log($"AP Toggled Deathlink Send Mode to {Value}");
        FactSystem.SetFact(new Fact("APDeathSendMode"), (float)Value);
    }

    protected override DeathLinkMode GetDefaultValue() => DeathLinkMode.OnDeath;
    public override List<LocalizedString> GetLocalizedChoices() => [
        new UnlocalizedString("Shard Fail"), 
        new UnlocalizedString("Death"), 
    ];
    public LocalizedString GetDisplayName() => new UnlocalizedString("When to Send Deathlinks");
    public string GetCategory() => "AP";
}

[HasteSetting]
public class ApReceiveDeathModeSetting : EnumSetting<DeathLinkMode>, IExposedSetting
{
    public override void ApplyValue()
    {
        UnityMainThreadDispatcher.Instance().log($"AP Toggled Deathlink Receive Mode to {Value}");
        FactSystem.SetFact(new Fact("APDeathReceiveMode"), (float)Value);
    }

    protected override DeathLinkMode GetDefaultValue() => DeathLinkMode.OnDeath;
    public override List<LocalizedString> GetLocalizedChoices() => [
        new UnlocalizedString("Shard Fail"), 
        new UnlocalizedString("Death"), 
    ];
    public LocalizedString GetDisplayName() => new UnlocalizedString("How to Receive Deathlinks");
    public string GetCategory() => "AP";
}
public enum DeathLinkMode
{
    OnShardFail, OnDeath
}