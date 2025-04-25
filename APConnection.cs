using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Landfall.Haste;
using pworld.Scripts.Extensions;
using UnityEngine;
using UnityEngine.Purchasing.MiniJSON;

namespace APConnection;

public class Connection(string hostname, int port)
{
  private string server = hostname + port.ToString();
  private string game = "Haste";

  public string dataTag = "Haste";
  public ArchipelagoSession session = ArchipelagoSessionFactory.CreateSession(hostname, port);

  public string username = "";

  public DeathLinkService? deathLinkService;

  public bool Connect(string user = "Player1", string? pass = null)
  {
    username = user;
    LoginResult result;
    try
    {
      Debug.Log("AP Attempting to connect");
      // handle TryConnectAndLogin attempt here and save the returned object to `result`
      result = session.TryConnectAndLogin(game, user, ItemsHandlingFlags.AllItems, password: pass);

    }
    catch (Exception e)
    {
      result = new LoginFailure(e.GetBaseException().Message);
    }

    // If it fails
    if (!result.Successful)
    {
      LoginFailure failure = (LoginFailure)result;
      string errorMessage = $"Failed to Connect to {server} as {user}:";
      foreach (string error in failure.Errors)
      {
        errorMessage += $"\n    {error}";
      }
      foreach (ConnectionRefusedError error in failure.ErrorCodes)
      {
        errorMessage += $"\n    {error}";
      }
      Debug.Log("AP Connection failed");
      Debug.LogError(errorMessage);
      return false; // Did not connect, show the user the contents of `errorMessage`
      // TODO Add display message to signal a failure
    }

    // Successfully connected, `ArchipelagoSession` (assume statically defined as `session` from now on) can now be used to interact with the server and the returned `LoginSuccessful` contains some useful information about the initial connection (e.g. a copy of the slot data as `loginSuccess.SlotData`)
    var loginSuccess = (LoginSuccessful)result;
    Debug.Log("AP Connection Succeded");

    dataTag = $"Haste_{loginSuccess.Team}_{loginSuccess.Slot}_";

    // Try to get the values from the slot data and set them into the fact system
    object Deathlink;
    if (loginSuccess.SlotData.TryGetValue("DeathLink", out Deathlink))
    {
      Debug.Log($"AP found DeathLink in slot data with value: {Deathlink}");
      FactSystem.SetFact(new Fact("APDeathlink"), Convert.ToSingle(Deathlink));
    }
    else
    {
      Debug.Log("AP Failed to get Deathlink from slot data:" + loginSuccess.SlotData.toJson());
      // Might default the value here to make things consistant
    }
    deathLinkService = session.CreateDeathLinkService();

    object ForceReload;
    if (loginSuccess.SlotData.TryGetValue("ForceReload", out ForceReload))
    {
      Debug.Log($"AP found ForceReload in slot data with value: {ForceReload}");
      FactSystem.SetFact(new Fact("APForceReload"), Convert.ToSingle(ForceReload));
    }
    else
    {
      Debug.Log("AP Failed to get ForceReaload from slot data:" + loginSuccess.SlotData.toJson());
      // Might default the value here to make things consistant
    }

    // SaveSystem.Save();

    return true;
  }

  public void SendLocation(string locationName)
  {
    Debug.Log($"AP Sending location {locationName}");
    ApDebugLog.Instance.DisplayMessage($"Sending location {locationName}");
    long locationID = session.Locations.GetLocationIdFromName(game, locationName);
    if (locationID != -1)
    {
      session.Locations.CompleteLocationChecks(locationID);
    }
    else
    {
      Debug.LogError($"AP No locationID for name: {locationName}");
    }
  }

  public void Close()
  {
    Debug.Log("AP Disconnecting");
    session.Socket.DisconnectAsync();
    Debug.Log("AP Disconnected");
  }

  public void BuildItemReciver(Action<string> GiveItem)
  {

    Debug.Log("AP Building Item Reiever");
    ApDebugLog.Instance.DisplayMessage($"Built item reciever");

    session.Items.ItemReceived += (receivedItemsHelper) =>
    {
      try
      {

        Debug.Log("AP Item recieved trigger");
        ApDebugLog.Instance.DisplayMessage("Item Recieved");
        var itemReceivedInfo = receivedItemsHelper.PeekItem();

        if (receivedItemsHelper.Index > FactSystem.GetFact(new Fact("APExpectedIndex")))
        {
          Debug.Log($"AP Atempting to give {itemReceivedInfo.ItemName}");
          ApDebugLog.Instance.DisplayMessage($"Atempting to give {itemReceivedInfo.ItemName} with index {receivedItemsHelper.Index}");
          GiveItem(itemReceivedInfo.ItemName);
          FactSystem.SetFact(new Fact("APExpectedIndex"), (float)receivedItemsHelper.Index);
          SaveSystem.Save();
        }


        receivedItemsHelper.DequeueItem();
      }
      catch (Exception e)
      {
        ApDebugLog.Instance.DisplayMessage($"Error in giving an item {e.Message}");
      }
    };
  }

  public void CompleteGame()
  {
    Debug.Log("AP Game is completed, it should release now");
    session.SetGoalAchieved();
  }

  public int GetItemCount(string item_name)
  {
    var count = 0;
    foreach (var item in session.Items.AllItemsReceived)
    {
      if (item.ItemName == item_name)
        count++;
    }
    return count;
  }
}
