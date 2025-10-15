using Landfall.Haste;
using UnityEngine.SceneManagement;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using System.Reflection;
using Zorro.Core;

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
                // if unlock-order is Boss-locked, then only uplock up until the lowest of either bossbeated or shards
                _shardCount = Math.Min(_shardCount, (int)FactSystem.GetFact(new Fact("APBossDefeated")));
                UnityMainThreadDispatcher.Instance().log("AP ShardUnlock is set to bosses, new count: " + _shardCount);
            } 
            UnityMainThreadDispatcher.Instance().log("AP unlocked to shard: " + _shardCount);
            // Change the connection data first as this value is checked on factsystem update
            PlayerProgress.UnlockToShard(_shardCount);
            SaveSystem.Save();
        }

        public static void GiveItem(string itemName)
        {
            UnityMainThreadDispatcher.Instance().log("AP Trying to give (" + itemName + ")");
            ApDebugLog.Instance.DisplayMessage($"Giving item: {itemName}");

            try
            {

                switch (itemName)
                {
                    case "Progressive Shard":
                        UnityMainThreadDispatcher.Instance().log("AP Got a Shard!");
                        ApDebugLog.Instance.DisplayMessage("Got Shard");
                        // Increase the number of shards that the player can use
                        UpdateShardCount();
                        if (FactSystem.GetFact(new Fact("in_run")) == 0f && FactSystem.GetFact(new Fact("APForceReload")) == 1f)
                        {
                            UnityMainThreadDispatcher.Instance().log("AP Forcing a Reload");
                            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                        }
                        break;
                    case "Shard Shop Filler Item":
                        // This is a filler item for now
                        UnityMainThreadDispatcher.Instance().log("AP Got a filler item");
                        ApDebugLog.Instance.DisplayMessage("Got Filler");
                        break;
                    case "Wraith's Hourglass":
                        UnityMainThreadDispatcher.Instance().log("AP Got Abilty Slomo");
                        ApDebugLog.Instance.DisplayMessage("Got Wraith's Hourglass");
                        FactSystem.SetFact(MetaProgression.SlomoUnlocked, 1f);
                        MonoFunctions.DelayCall(AbilityTutorial, 0.5f);
                        SaveSystem.Save();
                        break;
                    case "Heir's Javelin":
                        UnityMainThreadDispatcher.Instance().log("AP Got Abilty Grapple");
                        ApDebugLog.Instance.DisplayMessage("Got Heir's Javelin");
                        FactSystem.SetFact(MetaProgression.GrappleUnlocked, 1f);
                        MonoFunctions.DelayCall(AbilityTutorial, 0.5f);
                        SaveSystem.Save();
                        break;
                    case "Sage's Cowl":
                        UnityMainThreadDispatcher.Instance().log("AP Got Abilty Fly");
                        ApDebugLog.Instance.DisplayMessage("Got Sage's Cowl");
                        FactSystem.SetFact(MetaProgression.FlyUnlocked, 1f);
                        MonoFunctions.DelayCall(AbilityTutorial, 0.5f);
                        SaveSystem.Save();
                        break;
                    case "Wraith":
                        UnityMainThreadDispatcher.Instance().log("AP Got Wraith");
                        ApDebugLog.Instance.DisplayMessage("Got Wraith in hub");
                        FactSystem.SetFact(new Fact("APWraithInHub"), 1f);
                        if (FactSystem.GetFact(new Fact("in_run")) == 0f && FactSystem.GetFact(new Fact("APForceReload")) == 1f)
                        {
                            UnityMainThreadDispatcher.Instance().log("AP Forcing a Reload");
                            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                        }
                        break;
                    case "Niada":
                        UnityMainThreadDispatcher.Instance().log("AP Got Niada");
                        ApDebugLog.Instance.DisplayMessage("Got Niada in hub");
                        FactSystem.SetFact(new Fact("APHeirInHub"), 1f);
                        if (FactSystem.GetFact(new Fact("in_run")) == 0f && FactSystem.GetFact(new Fact("APForceReload")) == 1f)
                        {
                            UnityMainThreadDispatcher.Instance().log("AP Forcing a Reload");
                            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                        }
                        break;
                    case "Daro":
                        UnityMainThreadDispatcher.Instance().log("AP Got Daro");
                        ApDebugLog.Instance.DisplayMessage("Got Daro in hub");
                        FactSystem.SetFact(new Fact("APSageInHub"), 1f);
                        if (FactSystem.GetFact(new Fact("in_run")) == 0f && FactSystem.GetFact(new Fact("APForceReload")) == 1f)
                        {
                            UnityMainThreadDispatcher.Instance().log("AP Forcing a Reload");
                            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                        }
                        break;
                    case "The Captain":
                        UnityMainThreadDispatcher.Instance().log("AP Got The Captain");
                        ApDebugLog.Instance.DisplayMessage("Got The Captain");
                        FactSystem.SetFact(new Fact("APCaptainInHub"), 1f);
                        if (FactSystem.GetFact(new Fact("in_run")) == 0f && FactSystem.GetFact(new Fact("APForceReload")) == 1f)
                        {
                            UnityMainThreadDispatcher.Instance().log("AP Forcing a Reload");
                            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                        }
                        break;
                    case "Fashion Weeboh":
                        UnityMainThreadDispatcher.Instance().log("AP Got Fashion Weeboh");
                        ApDebugLog.Instance.DisplayMessage("Got Fashion Weeboh");
                        FactSystem.SetFact(new Fact("APFashionInHub"), 1f);
                        if (FactSystem.GetFact(new Fact("in_run")) == 0f && FactSystem.GetFact(new Fact("APForceReload")) == 1f)
                        {
                            UnityMainThreadDispatcher.Instance().log("AP Forcing a Reload");
                            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                        }
                        break;
                    case "Progressive Speed Upgrade":
                        UnityMainThreadDispatcher.Instance().log("AP Got Progressive Speed Upgrade");
                        ApDebugLog.Instance.DisplayMessage("Got Progressive Speed Upgrade");
                        FactSystem.AddToFact(new Fact("APSpeedUpgradesCollected"), 1f);
                        //TODO: reset stats here and 5 other places below lol
                        break;
                    case "Max Health Upgrade":
                        UnityMainThreadDispatcher.Instance().log("AP Got Max Health Upgrade");
                        ApDebugLog.Instance.DisplayMessage("Got Max Health Upgrade");
                        FactSystem.AddToFact(new Fact("APUpgradeMaxHealth"), 1f);
                        break;
                    case "Max Lives Upgrade":
                        UnityMainThreadDispatcher.Instance().log("AP Got Extra Lives Upgrade");
                        ApDebugLog.Instance.DisplayMessage("Got Extra Lives Upgrade");
                        FactSystem.AddToFact(new Fact("APUpgradeMaxLives"), 1f);
                        break;
                    case "Max Energy Upgrade":
                        UnityMainThreadDispatcher.Instance().log("AP Got Max Energy Upgrade");
                        ApDebugLog.Instance.DisplayMessage("Got Max Energy Upgrade");
                        FactSystem.AddToFact(new Fact("APUpgradeMaxEnergy"), 1f);
                        break;
                    case "Sparks in Shard Upgrade":
                        UnityMainThreadDispatcher.Instance().log("AP Got Sparks in Shard Upgrade");
                        ApDebugLog.Instance.DisplayMessage("Got Sparks in Shard Upgrade");
                        FactSystem.AddToFact(new Fact("APUpgradeLevelSparks"), 1f);
                        break;
                    case "Item Rarity Upgrade":
                        UnityMainThreadDispatcher.Instance().log("AP Got Item Rarity Upgrade");
                        ApDebugLog.Instance.DisplayMessage("Got Item Rarity Upgrade");
                        FactSystem.AddToFact(new Fact("APUpgradeItemRarity"), 1f);
                        break;
                    case "Starting Sparks Upgrade":
                        UnityMainThreadDispatcher.Instance().log("AP Got Starting Sparks Upgrade");
                        ApDebugLog.Instance.DisplayMessage("Got Starting Sparks Upgrade");
                        FactSystem.AddToFact(new Fact("APUpgradeStartingSparks"), 1f);
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
                    default:
                        UnityMainThreadDispatcher.Instance().logError("Item :" + itemName + " has no handling");
                        ApDebugLog.Instance.DisplayMessage($"Item {itemName} has no handling", isDebug:false);
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
            //SaveSystem.Save();
        }

        public static void AbilityTutorial()
        {
            Singleton<TutorialPopUpHandler>.Instance.TriggerPopUp(TutorialType.Abilities);
        }

        public static void GiveDeath(DeathLink death)
        {
            UnityMainThreadDispatcher.Instance().log("AP DeathLink recieved");
            ApDebugLog.Instance.DisplayMessage("Death Recieved", isDebug:false);
            var cause = "Unkown";
            if (death.Cause != null)
            {
                cause = death.Cause;
            }
            UnityMainThreadDispatcher.Instance().log("AP Lets try to Kill the player!");
            var gm_runType = GM_Run.instance.GetType();
            var FallOutMethod = gm_runType.GetMethod("FallOut", BindingFlags.Instance | BindingFlags.NonPublic);

            if (FallOutMethod != null)
            {
                // Invoke the method
                FallOutMethod.Invoke(GM_Run.instance, new object[] { });
            }
            else
            {
                UnityMainThreadDispatcher.Instance().logError("Fallout method not found on GM_RUN.");
            }
        }

        // Sets NPCs in overworld
        public static void SetHubState()
        {
            if (FactSystem.GetFact(new Fact("APNPCShuffle")) == 1f)
            {
                FactSystem.SetFact(new Fact("captain_in_hub"), FactSystem.GetFact(new Fact("APCaptainInHub")));
                FactSystem.SetFact(new Fact("heir_in_hub"), FactSystem.GetFact(new Fact("APHeirInHub")));
                FactSystem.SetFact(new Fact("wraith_in_hub"), FactSystem.GetFact(new Fact("APWraithInHub")));
                FactSystem.SetFact(new Fact("fashion_weeboh_in_hub"), FactSystem.GetFact(new Fact("APFashionInHub")));
                FactSystem.SetFact(new Fact("sage_in_hub"), FactSystem.GetFact(new Fact("APSageInHub")));
            }
            else
            {
                FactSystem.SetFact(new Fact("captain_in_hub"), 1f);
                FactSystem.SetFact(new Fact("heir_in_hub"), 1f);
                FactSystem.SetFact(new Fact("wraith_in_hub"), 1f);
                FactSystem.SetFact(new Fact("fashion_weeboh_in_hub"), 1f);
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
            switch (internalname)
            {
                case "Slomo":
                    return "Wraith's Hourglass";
                case "Grapple":
                    return "Heir's Javelin";
                case "Fly":
                    return "Sage's Cowl";
                default:
                    return "NOT FOUND";

            }
        }

        public static string GetCaptainUpgradeName(string internalname, int currentlevel)
        {
            switch (internalname)
            {
                case "MaxHealth":
                    return $"Captain's Max Health Upgrade Purchase {currentlevel + 1}";
                case "Lives":
                    return $"Captain's Max Lives Upgrade Purchase";
                case "MaxEnergy":
                    return $"Captain's Max Energy Upgrade Purchase {currentlevel + 1}";
                case "ItemRarity":
                    return $"Captain's Item Rarity Upgrade Purchase {currentlevel + 1}";
                case "LevelSparks":
                    return $"Captain's Sparks in Shard Upgrade Purchase {currentlevel + 1}";
                case "StartingResource":
                    return $"Captain's Starting Sparks Upgrade Purchase {currentlevel + 1}";
                default:
                    return "NOT FOUND";
            }
        }
    }
}