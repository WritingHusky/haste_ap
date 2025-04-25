using UnityEngine;
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
      Debug.Log("AP unlocked to shard: " + _shardCount);
      // Change the connection data first as this value is checked on factsystem update
      PlayerProgress.UnlockToShard(_shardCount);
      SaveSystem.Save();
    }

    public static void GiveItem(string itemName)
    {
      Debug.Log("AP Trying to give (" + itemName + ")");
      ApDebugLog.Instance.DisplayMessage($"Giving item: {itemName}");

      switch (itemName)
      {
        case "Progressive Shard":
          Debug.Log("AP Got a Shard!");
          ApDebugLog.Instance.DisplayMessage("Got Shard");
          // Increase the number of shards that the player can use
          UpdateShardCount();
          if (GM_Hub.isInHub && FactSystem.GetFact(new Fact("APForceReload")) == 1f)
          {
            Debug.Log("AP Forcing a Reload");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
          }
          break;
        case "Shard Shop Filler Item":
          // This is a filler item for now
          Debug.Log("AP Got a filler item");
          ApDebugLog.Instance.DisplayMessage("Got Filler");
          break;
        case "Slomo":
          Debug.Log("AP Got Abilty Slomo");
          ApDebugLog.Instance.DisplayMessage("Got Slomo");
          FactSystem.SetFact(MetaProgression.SlomoUnlocked, 1f);
          MonoFunctions.DelayCall(AbilityTutorial, 0.5f);
          SaveSystem.Save();
          break;
        case "Grapple":
          Debug.Log("AP Got Abilty Grapple");
          ApDebugLog.Instance.DisplayMessage("Got Grapple");
          FactSystem.SetFact(MetaProgression.GrappleUnlocked, 1f);
          MonoFunctions.DelayCall(AbilityTutorial, 0.5f);
          SaveSystem.Save();
          break;
        case "Fly":
          Debug.Log("AP Got Abilty Fly");
          ApDebugLog.Instance.DisplayMessage("Got Fly");
          FactSystem.SetFact(MetaProgression.FlyUnlocked, 1f);
          MonoFunctions.DelayCall(AbilityTutorial, 0.5f);
          SaveSystem.Save();
          break;
        case "Anti-Spark 100 bundle":
          Debug.Log("AP Got Anti-Spark 100 bundle");
          ApDebugLog.Instance.DisplayMessage("Got Anti-spark 100");
          FactSystem.AddToFact(new Fact("meta_progression_resource"), 100f);
          break;
        case "Anti-Spark 250 bundle":
          Debug.Log("AP Got Anti-Spark 250 bundle");
          ApDebugLog.Instance.DisplayMessage("Got Anti-spark 250");
          FactSystem.AddToFact(new Fact("meta_progression_resource"), 250f);
          break;
        case "Anti-Spark 500 bundle":
          Debug.Log("AP Got Anti-Spark 500 bundle");
          ApDebugLog.Instance.DisplayMessage("Got Anti-spark 500");
          FactSystem.AddToFact(new Fact("meta_progression_resource"), 500f);
          break;
        default:
          Debug.LogError("Item :" + itemName + " has no handling");
          ApDebugLog.Instance.DisplayMessage($"Item {itemName} has no handling");
          break;
      }
      SaveSystem.Save();
    }

    public static void AbilityTutorial()
    {
      Singleton<TutorialPopUpHandler>.Instance.TriggerPopUp(TutorialType.Abilities);
    }

    public static void GiveDeath(DeathLink death)
    {
      // TODO Figure out why debug not printing here
      Debug.Log("AP DeathLink recieved");
      ApDebugLog.Instance.DisplayMessage("Death Recieved");
      var cause = "Unkown";
      if (death.Cause != null)
      {
        cause = death.Cause;
      }
      Debug.Log("AP Lets try to Kill the player!");
      var gm_runType = GM_Run.instance.GetType();
      var FallOutMethod = gm_runType.GetMethod("FallOut", BindingFlags.Instance | BindingFlags.NonPublic);

      if (FallOutMethod != null)
      {
        // Invoke the method
        FallOutMethod.Invoke(GM_Run.instance, new object[] { });
      }
      else
      {
        Debug.LogError("Fallout method not found on GM_RUN.");
      }
    }

    // Maybe put this in the SaveSystem.load()
    public static void SetDefeaultState()
    {
      // Make sure everyone is in the hub
      FactSystem.SetFact(new Fact("captain_in_hub"), 1f);
      FactSystem.SetFact(new Fact("heir_in_hub"), 1f);
      FactSystem.SetFact(new Fact("wraith_in_hub"), 1f);
      FactSystem.SetFact(new Fact("researcher_in_hub"), 1f);
      // Skip Tutorials
      FactSystem.SetFact(new Fact("played_Tutorial01"), 1f);
      FactSystem.SetFact(new Fact("played_Tutorial02"), 1f);
      FactSystem.SetFact(new Fact("tutorial_finished"), 1f);
      FactSystem.SetFact(new Fact("FirstTimeShop"), 1f);
    }

  }
}