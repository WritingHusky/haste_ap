using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Landfall.Haste;
using System.Reflection;
using UnityEngine.SceneManagement;
using Zorro.Core;
using Zorro.Core.CLI;
using Zorro.Settings;
using UnityEngine;
using TMPro;
using System.Collections;
using IL.UnityEngine.UIElements;
using System.Runtime.CompilerServices;

namespace Integration
{
    public static class Integration
    {

        public static APConnection.Connection? connection;

        public static void UpdateShardCount()
        {
            var _shardCount = connection!.GetItemCount("Progressive Shard");
            if (FactSystem.GetFact(new Fact("APShardUnlockOrder")) == 1f)
            {
                // if unlock-order is Boss-locked, then only unlock up until the lowest of either bossbeated or shards
                _shardCount = Math.Min(_shardCount, (int)FactSystem.GetFact(new Fact("APBossDefeated")));
                UnityMainThreadDispatcher.Instance().log("AP ShardUnlock is set to bosses, new count: " + _shardCount);
            } 
            UnityMainThreadDispatcher.Instance().log("AP unlocked to shard: " + _shardCount);
            // Change the connection data first as this value is checked on factsystem update
            PlayerProgress.UnlockToShard(_shardCount);
            SaveSystem.Save();
        }

        public static void GiveItem(string itemName, string givingPlayerName)
        {
            UnityMainThreadDispatcher.Instance().log("AP Trying to give (" + itemName + ")");
            ApDebugLog.Instance.DisplayMessage($"Giving item: {itemName}");
            bool worthMentioning = false;
            int? quantity = null;
            NotificationType type = NotificationType.Progression;
            
            // TODO: figure out a better way of handling calling Vanilla functions from this code, because they may not be hooked in time when called from an async

            try
            {

                switch (itemName)
                {
                    case "A New Future":
                        // wincon item, ignore
                        break;
                    case "Progressive Shard":
                        UnityMainThreadDispatcher.Instance().log("AP Got a Shard!");
                        ApDebugLog.Instance.DisplayMessage("Got Shard");
                        // Increase the number of shards that the player can use
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Progressive Shard");
                        UpdateShardCount();
                        ForceReload();
                        break;
                    case "Shard Shop Filler Item":
                        // 80% sure this item can't actually spawn but I'll keep it here anyway
                        UnityMainThreadDispatcher.Instance().log("AP Got a filler item");
                        ApDebugLog.Instance.DisplayMessage("Got Filler");
                        break;
                    case "Wraith's Hourglass":
                        UnityMainThreadDispatcher.Instance().log("AP Got Ability Slomo");
                        ApDebugLog.Instance.DisplayMessage("Got Wraith's Hourglass");
                        HasteAP.SecretAbilityUnlock(AbilityKind.Slomo);
                        SaveSystem.Save();
                        worthMentioning = true;
                        break;
                    case "Heir's Javelin":
                        UnityMainThreadDispatcher.Instance().log("AP Got Ability Grapple");
                        ApDebugLog.Instance.DisplayMessage("Got Heir's Javelin");
                        HasteAP.SecretAbilityUnlock(AbilityKind.Grapple);
                        SaveSystem.Save();
                        worthMentioning = true;
                        break;
                    case "Sage's Cowl":
                        UnityMainThreadDispatcher.Instance().log("AP Got Ability Fly");
                        ApDebugLog.Instance.DisplayMessage("Got Sage's Cowl");
                        HasteAP.SecretAbilityUnlock(AbilityKind.Fly);
                        SaveSystem.Save();
                        worthMentioning = true;
                        break;
                    case "Courier's Board":
                        UnityMainThreadDispatcher.Instance().log("AP Got Ability Boost");
                        ApDebugLog.Instance.DisplayMessage("Got Courier's Board");
                        HasteAP.SecretAbilityUnlock(AbilityKind.BoardBoost);
                        SaveSystem.Save();
                        worthMentioning = true;
                        break;
                    case "Wraith":
                        UnityMainThreadDispatcher.Instance().log("AP Got Wraith");
                        ApDebugLog.Instance.DisplayMessage("Got Wraith in hub");
                        FactSystem.SetFact(new Fact("APWraithInHub"), 1f);
                        worthMentioning = true;
                        ForceReload();
                        break;
                    case "Niada":
                        UnityMainThreadDispatcher.Instance().log("AP Got Niada");
                        ApDebugLog.Instance.DisplayMessage("Got Niada in hub");
                        FactSystem.SetFact(new Fact("APHeirInHub"), 1f);
                        worthMentioning = true;
                        ForceReload();
                        break;
                    case "Daro":
                        UnityMainThreadDispatcher.Instance().log("AP Got Daro");
                        ApDebugLog.Instance.DisplayMessage("Got Daro in hub");
                        FactSystem.SetFact(new Fact("APSageInHub"), 1f);
                        worthMentioning = true;
                        ForceReload();
                        break;
                    case "The Captain":
                        UnityMainThreadDispatcher.Instance().log("AP Got The Captain");
                        ApDebugLog.Instance.DisplayMessage("Got The Captain");
                        FactSystem.SetFact(new Fact("APCaptainInHub"), 1f);
                        worthMentioning = true;
                        ForceReload();
                        break;
                    case "Fashion Weeboh":
                        UnityMainThreadDispatcher.Instance().log("AP Got Fashion Weeboh");
                        ApDebugLog.Instance.DisplayMessage("Got Fashion Weeboh");
                        FactSystem.SetFact(new Fact("APFashionInHub"), 1f);
                        worthMentioning = true;
                        ForceReload();
                        break;
                    case "Progressive Speed Upgrade":
                        UnityMainThreadDispatcher.Instance().log("AP Got Progressive Speed Upgrade");
                        ApDebugLog.Instance.DisplayMessage("Got Progressive Speed Upgrade");
                        FactSystem.AddToFact(new Fact("APSpeedUpgradesCollected"), 1f);
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Progressive Speed Upgrade");
                        break;
                    case "Max Health Upgrade":
                        UnityMainThreadDispatcher.Instance().log("AP Got Max Health Upgrade");
                        ApDebugLog.Instance.DisplayMessage("Got Max Health Upgrade");
                        FactSystem.AddToFact(new Fact("APUpgradeMaxHealth"), 1f);
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Max Health Upgrade");
                        Player.localPlayer.ResetStats();
                        break;
                    case "Max Lives Upgrade":
                        UnityMainThreadDispatcher.Instance().log("AP Got Extra Lives Upgrade");
                        ApDebugLog.Instance.DisplayMessage("Got Extra Lives Upgrade");
                        // extra lives upgrade on first get because its coded really weirdly
                        if (FactSystem.GetFact(new Fact("APUpgradeMaxLives")) == 0f) FactSystem.AddToFact(new Fact("APUpgradeMaxLives"), 1f);
                        FactSystem.AddToFact(new Fact("APUpgradeMaxLives"), 1f);
                        worthMentioning = true;
                        Player.localPlayer.ResetStats();
                        break;
                    case "Max Energy Upgrade":
                        UnityMainThreadDispatcher.Instance().log("AP Got Max Energy Upgrade");
                        ApDebugLog.Instance.DisplayMessage("Got Max Energy Upgrade");
                        FactSystem.AddToFact(new Fact("APUpgradeMaxEnergy"), 1f);
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Max Energy Upgrade");
                        Player.localPlayer.ResetStats();
                        break;
                    case "Sparks in Fragments Upgrade":
                        UnityMainThreadDispatcher.Instance().log("AP Got Sparks in Fragments Upgrade");
                        ApDebugLog.Instance.DisplayMessage("Got Sparks in Fragments Upgrade");
                        FactSystem.AddToFact(new Fact("APUpgradeLevelSparks"), 1f);
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Sparks in Fragments Upgrade");
                        Player.localPlayer.ResetStats();
                        break;
                    case "Item Rarity Upgrade":
                        UnityMainThreadDispatcher.Instance().log("AP Got Item Rarity Upgrade");
                        ApDebugLog.Instance.DisplayMessage("Got Item Rarity Upgrade");
                        FactSystem.AddToFact(new Fact("APUpgradeItemRarity"), 1f);
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Item Rarity Upgrade");
                        Player.localPlayer.ResetStats();
                        break;
                    case "Starting Sparks Upgrade":
                        UnityMainThreadDispatcher.Instance().log("AP Got Starting Sparks Upgrade");
                        ApDebugLog.Instance.DisplayMessage("Got Starting Sparks Upgrade");
                        FactSystem.AddToFact(new Fact("APUpgradeStartingSparks"), 1f);
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Starting Sparks Upgrade");
                        Player.localPlayer.ResetStats();
                        break;
                    case "Persistent Common Speed Item":
                        UnityMainThreadDispatcher.Instance().log("AP Got Persistent Common Speed Item");
                        ApDebugLog.Instance.DisplayMessage("Got Persistent Common Speed Item");
                        FactSystem.AddToFact(new Fact("APCommonSpeedItems"), 1f);
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Persistent Common Speed Item");
                        ForcePermItem(Rarity.Common, APItemCategory.Speed);
                        break;
                    case "Persistent Common Support Item":
                        UnityMainThreadDispatcher.Instance().log("AP Got Persistent Common Support Item");
                        ApDebugLog.Instance.DisplayMessage("Got Persistent Common Support Item");
                        FactSystem.AddToFact(new Fact("APCommonSupportItems"), 1f);
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Persistent Common Support Item");
                        ForcePermItem(Rarity.Common, APItemCategory.Support);
                        break;
                    case "Persistent Common Health Item":
                        UnityMainThreadDispatcher.Instance().log("AP Got Persistent Common Health Item");
                        ApDebugLog.Instance.DisplayMessage("Got Persistent Common Health Item");
                        FactSystem.AddToFact(new Fact("APCommonHealthItems"), 1f);
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Persistent Common Health Item");
                        ForcePermItem(Rarity.Common, APItemCategory.Health);
                        break;
                    case "Persistent Rare Speed Item":
                        UnityMainThreadDispatcher.Instance().log("AP Got Persistent Rare Speed Item");
                        ApDebugLog.Instance.DisplayMessage("Got Persistent Rare Speed Item");
                        FactSystem.AddToFact(new Fact("APRareSpeedItems"), 1f);
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Persistent Rare Speed Item");
                        ForcePermItem(Rarity.Rare, APItemCategory.Speed);
                        break;
                    case "Persistent Rare Support Item":
                        UnityMainThreadDispatcher.Instance().log("AP Got Persistent Rare Support Item");
                        ApDebugLog.Instance.DisplayMessage("Got Persistent Rare Support Item");
                        FactSystem.AddToFact(new Fact("APRareSupportItems"), 1f);
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Persistent Rare Support Item");
                        ForcePermItem(Rarity.Rare, APItemCategory.Support);
                        break;
                    case "Persistent Rare Health Item":
                        UnityMainThreadDispatcher.Instance().log("AP Got Persistent Rare Health Item");
                        ApDebugLog.Instance.DisplayMessage("Got Persistent Rare Health Item");
                        FactSystem.AddToFact(new Fact("APRareHealthItems"), 1f);
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Persistent Rare Health Item");
                        ForcePermItem(Rarity.Rare, APItemCategory.Health);
                        break;
                    case "Persistent Epic Speed Item":
                        UnityMainThreadDispatcher.Instance().log("AP Got Persistent Epic Speed Item");
                        ApDebugLog.Instance.DisplayMessage("Got Persistent Epic Speed Item");
                        FactSystem.AddToFact(new Fact("APEpicSpeedItems"), 1f);
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Persistent Epic Speed Item");
                        ForcePermItem(Rarity.Epic, APItemCategory.Speed);
                        break;
                    case "Persistent Epic Support Item":
                        UnityMainThreadDispatcher.Instance().log("AP Got Persistent Epic Support Item");
                        ApDebugLog.Instance.DisplayMessage("Got Persistent Epic Support Item");
                        FactSystem.AddToFact(new Fact("APEpicSupportItems"), 1f);
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Persistent Epic Support Item");
                        ForcePermItem(Rarity.Epic, APItemCategory.Support);
                        break;
                    case "Persistent Epic Health Item":
                        UnityMainThreadDispatcher.Instance().log("AP Got Persistent Epic Health Item");
                        ApDebugLog.Instance.DisplayMessage("Got Persistent Epic Health Item");
                        FactSystem.AddToFact(new Fact("APEpicHealthItems"), 1f);
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Persistent Epic Health Item");
                        ForcePermItem(Rarity.Epic, APItemCategory.Health);
                        break;
                    case "Persistent Legendary Item":
                        UnityMainThreadDispatcher.Instance().log("AP Got Persistent Legendary Item");
                        ApDebugLog.Instance.DisplayMessage("Got Persistent Legendary Item");
                        FactSystem.AddToFact(new Fact("APLegendaryItems"), 1f);
                        worthMentioning = true;
                        quantity = connection!.GetItemCount("Persistent Legendary Item");
                        ForcePermItem(Rarity.Legendary, APItemCategory.Legendary);
                        break;
                    case "Anti-Spark 10 bundle":
                        UnityMainThreadDispatcher.Instance().log("AP Got Anti-Spark 10 bundle");
                        ApDebugLog.Instance.DisplayMessage("Got Anti-spark 10");
                        FactSystem.AddToFact(new Fact("meta_progression_resource"), 10f);
                        break;
                    case "Anti-Spark 100 bundle":
                        UnityMainThreadDispatcher.Instance().log("AP Got Anti-Spark 100 bundle");
                        ApDebugLog.Instance.DisplayMessage("Got Anti-spark 100");
                        FactSystem.AddToFact(new Fact("meta_progression_resource"), 100f);
                        break;
                    case "Anti-Spark 250 bundle":
                        UnityMainThreadDispatcher.Instance().log("AP Got Anti-Spark 250 bundle");
                        ApDebugLog.Instance.DisplayMessage("Got Anti-spark 250");
                        FactSystem.AddToFact(new Fact("meta_progression_resource"), 250f);
                        break;
                    case "Anti-Spark 500 bundle":
                        UnityMainThreadDispatcher.Instance().log("AP Got Anti-Spark 500 bundle");
                        ApDebugLog.Instance.DisplayMessage("Got Anti-spark 500");
                        FactSystem.AddToFact(new Fact("meta_progression_resource"), 500f);
                        break;
                    case "Anti-Spark 750 bundle":
                        UnityMainThreadDispatcher.Instance().log("AP Got Anti-Spark 750 bundle");
                        ApDebugLog.Instance.DisplayMessage("Got Anti-spark 750");
                        FactSystem.AddToFact(new Fact("meta_progression_resource"), 750f);
                        break;
                    case "Anti-Spark 1k bundle":
                        UnityMainThreadDispatcher.Instance().log("AP Got Anti-Spark 1000 bundle");
                        ApDebugLog.Instance.DisplayMessage("Got Anti-spark 1000");
                        FactSystem.AddToFact(new Fact("meta_progression_resource"), 1000f);
                        break;
                    case "Disaster Trap":
                        UnityMainThreadDispatcher.Instance().log("AP Got Disaster Trap");
                        ApDebugLog.Instance.DisplayMessage("Got Disaster Trap");
                        FactSystem.AddToFact(new Fact("APQueuedDisasterTraps"), 1f);
                        worthMentioning = true;
                        type = NotificationType.Trap;
                        break;
                    case "Landing Downgrade Trap":
                        UnityMainThreadDispatcher.Instance().log("AP Got Landing Downgrade Trap");
                        ApDebugLog.Instance.DisplayMessage("Got Landing Downgrade Trap");
                        FactSystem.AddToFact(new Fact("APQueuedLandingTraps"), 1f);
                        worthMentioning = true;
                        type = NotificationType.Trap;
                        break;
                    default:
                        UnityMainThreadDispatcher.Instance().logError("Item '" + itemName + "' has no handling");
                        ApDebugLog.Instance.DisplayMessage($"<color=#FF0000>ERROR:</color> Item '{itemName}' has no handling.\nPlease screenshot this error and send it to the developer of this mod so it can be fixed.", isDebug:false, duration:20f);
                        break;
                }
            }
            catch (Exception e)
            {
                if (e.GetType() != typeof(NullReferenceException))
                {
                    UnityMainThreadDispatcher.Instance().log($"Error within give item {e.Message},{e.StackTrace}");
                    ApDebugLog.Instance.DisplayMessage($"Error within give item {e.Message},{e.StackTrace}", duration: 10f);
                }
            }
            if (worthMentioning) Player.localPlayer.StartCoroutine(ItemPopup(itemName, givingPlayerName, quantity, type));

            //SaveSystem.Save();
        }

        [ConsoleCommand]
        public static void ForceReload()
        {
            if (FactSystem.GetFact(new Fact("in_run")) == 0f && FactSystem.GetFact(new Fact("APForceReload")) == 1f)
            {
                UnityMainThreadDispatcher.Instance().log("AP Forcing a Reload");
                ApDebugLog.Instance.DisplayMessage($"Hub reload has been forced. Hold tight.", isDebug:false);
                try
                {
                    // close UI that might not exist
                    Type MetaUIType = typeof(MetaProgressionRowUI).Assembly.GetType("Landfall.Haste.MetaProgressionUI");
                    MethodInfo closeUIInfo = MetaUIType.GetMethod("CloseUI", BindingFlags.Instance | BindingFlags.Public);
                    object metaUiInstance = GameObject.FindAnyObjectByType(MetaUIType);
                    closeUIInfo.Invoke(metaUiInstance, null);
                } catch (Exception) { }

                try
                {
                    // close UI that might not exist
                    Type MetaSkinUIType = typeof(MetaProgressionRowUI).Assembly.GetType("Landfall.Haste.SkinSelectionUI");
                    MethodInfo closeUIInfo2 = MetaSkinUIType.GetMethod("CloseUI", BindingFlags.Instance | BindingFlags.Public);
                    object metaSkinUiInstance = GameObject.FindAnyObjectByType(MetaSkinUIType);
                    closeUIInfo2.Invoke(metaSkinUiInstance, null);
                }
                catch (Exception) { }

                // tell the game that the 2 other hub UIs are closed
                AlltimeStatsScreen.IsOpen = false;
                UI_UnlockedItemsScreen.IsOpen = false;
                // see how much easier this is when the class is public and you don't need to go through type reflection?

                TimeHandler.instance.ResetTime();
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        public static void ForcePermItem(Rarity rarity, APItemCategory category)
        {
            if (FactSystem.GetFact(new Fact("in_run")) == 1f)
            {
                HasteAP.GenerateRandomStartingItems(rarity, category, 1f);
            }
        }

        public static void AbilityTutorial()
        {
            Singleton<TutorialPopUpHandler>.Instance.TriggerPopUp(TutorialType.Abilities);
        }

        [ConsoleCommand]
        public static void ForceItemPopup()
        {
            Player.localPlayer.StartCoroutine(ItemPopup("LONG DEBUGGING ITEM NAME", "Command Line", 2));
        }

        public static IEnumerator ItemPopup(string itemName, string givingPlayer, int? quantity, NotificationType type=NotificationType.Progression)
        {
            yield return null;
            //NotificationHandler.Instance
            try
            {
                UnityMainThreadDispatcher.Instance().log($"Attempting to spawn popup for {itemName}");
                var inst = Singleton<NotificationHandler>.Instance;
                if (!inst) yield break;
                GameObject notificationPrefab = inst.notificationPrefabs.Find((GameObject n) => n.GetComponent<NotificationMessage>() is FragmentModifierNotification);
                FragmentModifierNotification fragmentModifierNotification = inst.SpawnNotification(notificationPrefab) as FragmentModifierNotification;
                fragmentModifierNotification.EffectDescription.text = $"{itemName}";
                if (quantity != null) fragmentModifierNotification.EffectDescription.text += $" <style=+s>#{quantity}</style>";
                fragmentModifierNotification.EffectDescription.rectTransform.localPosition = new Vector3(0f, fragmentModifierNotification.EffectDescription.rectTransform.localPosition.y, fragmentModifierNotification.EffectDescription.rectTransform.localPosition.z);
                var headerpart = fragmentModifierNotification.gameObject.transform.Find("INFO_AREA").Find("Header");
                headerpart.gameObject.GetComponent<TextMeshProUGUI>().text = $"Item from {givingPlayer}";
                headerpart.gameObject.GetComponent<RectTransform>().localPosition = new Vector3(0f, headerpart.gameObject.GetComponent<RectTransform>().localPosition.y, headerpart.gameObject.GetComponent<RectTransform>().localPosition.z);
                fragmentModifierNotification.gameObject.transform.Find("INFO_AREA").Find("Divider").gameObject.SetActive(false);
                fragmentModifierNotification.gameObject.transform.Find("INFO_AREA").Find("IconBackground").gameObject.SetActive(false);
                fragmentModifierNotification.IconImage.gameObject.SetActive(false);

                if (type == NotificationType.Trap)
                {
                    headerpart.gameObject.GetComponent<TextMeshProUGUI>().color = new Color(1f, 0.2924f, 0.3207f);
                    fragmentModifierNotification.gameObject.transform.Find("INFO_AREA").Find("Background").gameObject.GetComponent<UnityEngine.UI.Image>().color = new Color(1f, 0.2924f, 0.3207f);
                }

                fragmentModifierNotification.animator.Play("NotificationMessageIn");
                fragmentModifierNotification.animator.SetBool("Play", true);

            }
            catch (Exception e)
            {
                UnityMainThreadDispatcher.Instance().log($"Error within itempopup {e.Message},{e.StackTrace}");
                ApDebugLog.Instance.DisplayMessage($"Error within itempopup {e.Message},{e.StackTrace}", duration: 10f);
            }
        }

        public static void GiveDeath(DeathLink death)
        {
            UnityMainThreadDispatcher.Instance().log("AP DeathLink received");
            ApDebugLog.Instance.DisplayMessage("Death Received", isDebug:false);
            var cause = "Unknown";
            if (death.Cause != null)
            {
                cause = death.Cause;
            }
            UnityMainThreadDispatcher.Instance().log("AP Lets try to Kill the player!");
            FactSystem.SetFact(new Fact("APDoubleKillStopper"), 1f);
            GM_Run.instance.FallOut(PlayerCharacter.localPlayer);
        }

        // Sets NPCs in overworld
        public static void SetHubState()
        {
            if (FactSystem.GetFact(new Fact("APNPCShuffle")) == 1f)
            {
                FactSystem.SetFact(new Fact("captain_in_hub"), FactSystem.GetFact(new Fact("APCaptainInHub")));
                FactSystem.SetFact(new Fact("heir_in_hub"), FactSystem.GetFact(new Fact("APHeirInHub")));
                FactSystem.SetFact(new Fact("wraith_in_hub"), FactSystem.GetFact(new Fact("APWraithInHub")));
                FactSystem.SetFact(new Fact("sage_in_hub"), FactSystem.GetFact(new Fact("APSageInHub")));
                if (FactSystem.GetFact(new Fact("APFashionInHub")) == 1f)
                {
                    FactSystem.SetFact(new Fact("fashion_weeboh_in_hub"), 1f);
                    SkinManager.UnlockFashionableWeeboh();
                }
                
                
            }
            else
            {
                FactSystem.SetFact(new Fact("captain_in_hub"), 1f);
                FactSystem.SetFact(new Fact("heir_in_hub"), 1f);
                FactSystem.SetFact(new Fact("wraith_in_hub"), 1f);
                FactSystem.SetFact(new Fact("fashion_weeboh_in_hub"), 1f);
                SkinManager.UnlockFashionableWeeboh();
                FactSystem.SetFact(new Fact("sage_in_hub"), 1f);
            }
            FactSystem.SetFact(new Fact("researcher_in_hub"), 1f);
            FactSystem.SetFact(new Fact("riza_in_hub"), 1f);
            
        }


        public static void ClearStoryFlags()
        {
            foreach (string s in StoryFlags.StoryFlags.AllFlags)
            {
                FactSystem.SetFact(new Fact(s), 1f);
            }
            // still not sure this works but we'll try it anyway
            FactSystem.SetFact(new Fact("main_story_progress"), 6f);
        }

        public static string GetAbilityName(string internalname)
        {
            return internalname switch
            {
                "Slomo" => "Wraith's Hourglass",
                "Grapple" => "Heir's Javelin",
                "Fly" => "Sage's Cowl",
                _ => "NOT FOUND",
            };
        }

        public static string GetCaptainUpgradeName(string internalname, int currentlevel)
        {
            return internalname switch
            {
                "MaxHealth" => $"Captain's Max Health Upgrade Purchase {currentlevel + 1}",
                "Lives" => $"Captain's Max Lives Upgrade Purchase",
                "MaxEnergy" => $"Captain's Max Energy Upgrade Purchase {currentlevel + 1}",
                "ItemRarity" => $"Captain's Item Rarity Upgrade Purchase {currentlevel + 1}",
                "LevelSparks" => $"Captain's Sparks in Fragments Upgrade Purchase {currentlevel + 1}",
                "StartingResource" => $"Captain's Starting Sparks Upgrade Purchase {currentlevel + 1}",
                _ => "NOT FOUND",
            };
        }

        public static string ConvertCaptainsInternalName(string fact)
        {
            return fact switch
            {
                "meta_progression_max_health" => "APUpgradeMaxHealth",
                "meta_progression_max_energy" => "APUpgradeMaxEnergy",
                "meta_progression_lives" => "APUpgradeMaxLives",
                "meta_progression_item_rarity" => "APUpgradeItemRarity",
                "meta_progression_level_sparks" => "APUpgradeLevelSparks",
                "meta_progression_starting_resource" => "APUpgradeStartingSparks",
                _ => "NOT FOUND",
            };
        }

        public static string GetFashionPurchaseName(SkinManager.Skin skin)
        {
            return skin switch
            {
                SkinManager.Skin.Crispy => "Crispy",
                SkinManager.Skin.Green => "Little Sister",
                SkinManager.Skin.Blue => "Supersonic Zoe",
                SkinManager.Skin.Shadow => "Zoe the Shadow",
                SkinManager.Skin.Wobbler => "Totally Accurate Zoe",
                SkinManager.Skin.Clown => "Flopsy",
                SkinManager.Skin.DarkClown => "Twisted Flopsy",
                SkinManager.Skin.Weeboh => "Weeboh",
                SkinManager.Skin.Zoe64 => "Zoe 64",
                _ => "NOT FOUND",
            };
        }
    }
}