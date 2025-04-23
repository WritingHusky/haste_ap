using UnityEngine;
using Landfall.Haste;
using UnityEngine.SceneManagement;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;

namespace Integration
{
  public static class Integration
  {
    private static bool _enabled = false;

    private static int _shardCount = -1;

    public static APConnection.Connection? connection;

    public static void UpdateShardCount(int count)
    {
      _shardCount = count;
      Debug.Log("AP unlocked to shard: " + _shardCount);
      PlayerProgress.UnlockToShard(_shardCount);
      SaveSystem.Save();
      // If the player is in the hub and force reload is set reload the hub
      connection!.UpdateDataShardCount(count);
    }

    public static void GiveItem(string itemName)
    {
      // TODO Figure out why debug not printing here
      Debug.Log("AP Trying to give (" + itemName + ")");
      Debug.Assert(_enabled, "Should not try to give item when AP is disabled");
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
        default:
          throw new NotImplementedException("Item :" + itemName + " has no handling");
      }
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
      Player.localPlayer.character.GetType().GetMethod("KillPlayer").Invoke(Player.localPlayer.character, new object[] { cause, true });
    }

    // Maybe put this in the SaveSystem.load()
    public static void SetDefeaultState()
    {
      FactSystem.SetFact(new Fact("researcher_in_hub"), 1f);
    }

  }
}