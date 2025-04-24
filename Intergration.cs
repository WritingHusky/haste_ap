using UnityEngine;
using Landfall.Haste;
using UnityEngine.SceneManagement;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using System.Reflection;

namespace Integration
{
  public static class Integration
  {
    private static bool _enabled = false;

    private static int _shardCount = -1;

    public static APConnection.Connection? connection;

    public static void UpdateShardCount(int count)
    {
      Debug.Log("AP unlocked to shard: " + _shardCount);
      _shardCount = count;
      // Change the connection data first as this value is checked on factsystem update
      connection!.UpdateDataShardCount(_shardCount);
      PlayerProgress.UnlockToShard(_shardCount);
      SaveSystem.Save();
    }

    public static void GiveItem(string itemName)
    {
      // TODO Figure out why debug not printing here
      Debug.Log("AP Trying to give (" + itemName + ")");
      // Debug.Assert(_enabled, "Should not try to give item when AP is disabled");
      // Implementation here
      switch (itemName)
      {
        case "Progressive Shard":
          Debug.Log("AP Got a Shard!");
          // Increase the number of shards that the player can use
          UpdateShardCount(_shardCount + 1);
          if (GM_Hub.isInHub && FactSystem.GetFact(new Fact("APForceReload")) == 1f)
          {
            Debug.Log("AP Forcing a Reload");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
          }
          break;
        case "Shard Shop Filler Item":
          // This is a filler item for now
          Debug.Log("AP Got a filler item");
          break;
        case "Slomo":
          Debug.Log("AP Got Abilty Slomo");
          FactSystem.SetFact(new Fact("ability_slomo_unlocked"), 1f);
          break;
        case "Grapple":
          Debug.Log("AP Got Abilty Grapple");
          FactSystem.SetFact(new Fact("ability_grapple_unlocked"), 1f);
          break;
        case "Fly":
          Debug.Log("AP Got Abilty Fly");
          FactSystem.SetFact(new Fact("ability_fly_unlocked"), 1f);
          break;
        case "Anti-Spark 100 bundle":
          Debug.Log("AP Got Anti-Spark 100 bundle");
          FactSystem.AddToFact(new Fact("meta_progression_resource"), 100f);
          break;
        case "Anti-Spark 250 bundle":
          Debug.Log("AP Got Anti-Spark 250 bundle");
          FactSystem.AddToFact(new Fact("meta_progression_resource"), 250f);
          break;
        case "Anti-Spark 500 bundle":
          Debug.Log("AP Got Anti-Spark 500 bundle");
          FactSystem.AddToFact(new Fact("meta_progression_resource"), 500f);
          break;
        default:
          throw new NotImplementedException("Item :" + itemName + " has no handling");
      }
      SaveSystem.Save();
    }

    public static void GiveDeath(DeathLink death)
    {
      // TODO Figure out why debug not printing here
      Debug.Log("AP DeathLink recieved");
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