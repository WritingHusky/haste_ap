using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Landfall.Haste;
using UnityEngine.Purchasing.MiniJSON;

namespace APConnection;

public class Connection(string hostname, int port)
{
  private string server = hostname + port.ToString();
  public string game = "Haste";

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
      UnityMainThreadDispatcher.Instance().log("AP Attempting to connect");
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
      UnityMainThreadDispatcher.Instance().log("AP Connection failed");
      UnityMainThreadDispatcher.Instance().logError(errorMessage);
      return false; // Did not connect, show the user the contents of `errorMessage`
      // TODO Add display message to signal a failure
    }

    // Successfully connected, `ArchipelagoSession` (assume statically defined as `session` from now on) can now be used to interact with the server and the returned `LoginSuccessful` contains some useful information about the initial connection (e.g. a copy of the slot data as `loginSuccess.SlotData`)
    var loginSuccess = (LoginSuccessful)result;
    UnityMainThreadDispatcher.Instance().log("AP Connection Succeded");

    dataTag = $"Haste_{loginSuccess.Team}_{loginSuccess.Slot}_";

        // Try to get the values from the slot data and set them into the fact system
        if (loginSuccess.SlotData.TryGetValue("DeathLink", out object Deathlink))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found DeathLink in slot data with value: {Deathlink}");
            FactSystem.SetFact(new Fact("APDeathlink"), Convert.ToSingle(Deathlink));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get Deathlink from slot data:" + loginSuccess.SlotData.toJson());
            // Might default the value here to make things consistant
        }
        deathLinkService = session.CreateDeathLinkService();

        if (loginSuccess.SlotData.TryGetValue("ForceReload", out object ForceReload))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found ForceReload in slot data with value: {ForceReload}");
            FactSystem.SetFact(new Fact("APForceReload"), Convert.ToSingle(ForceReload));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get ForceReaload from slot data:" + loginSuccess.SlotData.toJson());
            // Might default the value here to make things consistant
        }

        if (loginSuccess.SlotData.TryGetValue("Shopsanity", out object Shopsanity))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found Shopsanity in slot data with value: {Shopsanity}");
            FactSystem.SetFact(new Fact("APShopsanity"), Convert.ToSingle(Shopsanity));
            if (FactSystem.GetFact(new Fact("APShopsanity")) == 1f)
            {
                // this looks stupid, but its so that if you reconnect and the Facts are already there, it wont override them
                if (FactSystem.GetFact(new Fact("APShopsanityShard1")) == 0f) FactSystem.SetFact(new Fact("APShopsanityShard1"), 0f);
                if (FactSystem.GetFact(new Fact("APShopsanityShard2")) == 0f) FactSystem.SetFact(new Fact("APShopsanityShard2"), 0f);
                if (FactSystem.GetFact(new Fact("APShopsanityShard3")) == 0f) FactSystem.SetFact(new Fact("APShopsanityShard3"), 0f);
                if (FactSystem.GetFact(new Fact("APShopsanityShard4")) == 0f) FactSystem.SetFact(new Fact("APShopsanityShard4"), 0f);
                if (FactSystem.GetFact(new Fact("APShopsanityShard5")) == 0f) FactSystem.SetFact(new Fact("APShopsanityShard5"), 0f);
                if (FactSystem.GetFact(new Fact("APShopsanityShard6")) == 0f) FactSystem.SetFact(new Fact("APShopsanityShard6"), 0f);
                if (FactSystem.GetFact(new Fact("APShopsanityShard7")) == 0f) FactSystem.SetFact(new Fact("APShopsanityShard7"), 0f);
                if (FactSystem.GetFact(new Fact("APShopsanityShard8")) == 0f) FactSystem.SetFact(new Fact("APShopsanityShard8"), 0f);
                if (FactSystem.GetFact(new Fact("APShopsanityShard9")) == 0f) FactSystem.SetFact(new Fact("APShopsanityShard9"), 0f);
                if (FactSystem.GetFact(new Fact("APShopsanityShard10")) == 0f) FactSystem.SetFact(new Fact("APShopsanityShard10"), 0f);
            }
            else if (FactSystem.GetFact(new Fact("APShopsanity")) == 2f)
            {
                FactSystem.SetFact(new Fact("APShopsanityGlobal"), 0f);
            }
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get Shopsanity from slot data:" + loginSuccess.SlotData.toJson());
            // Might default the value here to make things consistant
        }

        if (loginSuccess.SlotData.TryGetValue("Shopsanity Quantity", out object ShopsanityQuantity))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found ShopsanityQuantity in slot data with value: {ShopsanityQuantity}");
            FactSystem.SetFact(new Fact("APShopsanityQuantity"), Convert.ToSingle(ShopsanityQuantity));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get ShopsanityQuantity from slot data:" + loginSuccess.SlotData.toJson());
            // Might default the value here to make things consistant
        }


        if (loginSuccess.SlotData.TryGetValue("Shard Goal", out object ShardGoal))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found ShardGoal in slot data with value: {ShardGoal}");
            FactSystem.SetFact(new Fact("APShardGoal"), Convert.ToSingle(ShardGoal));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get ShardGoal from slot data:" + loginSuccess.SlotData.toJson());
            // Might default the value here to make things consistant
        }

        if (loginSuccess.SlotData.TryGetValue("Default Outfit Body", out object DefSkinBody))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found DefaultOutfitBody in slot data with value: {DefSkinBody}");
            FactSystem.SetFact(new Fact("equipped_skin_body"), Convert.ToSingle(DefSkinBody));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get DefaultOutfitBody from slot data:" + loginSuccess.SlotData.toJson());
            // Might default the value here to make things consistant
        }

        if (loginSuccess.SlotData.TryGetValue("Default Outfit Hat", out object DefSkinHat))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found DefaultOutfitHat in slot data with value: {DefSkinHat}");
            FactSystem.SetFact(new Fact("equipped_skin_head"), Convert.ToSingle(DefSkinHat));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get DefaultOutfitHat from slot data:" + loginSuccess.SlotData.toJson());
            // Might default the value here to make things consistant
        }


        // SaveSystem.Save();

        return true;
  }

  public void SendLocation(string locationName)
  {
    UnityMainThreadDispatcher.Instance().log($"AP Sending location {locationName}");
    ApDebugLog.Instance.DisplayMessage($"Sending location {locationName}");
    long locationID = session.Locations.GetLocationIdFromName(game, locationName);
    if (locationID != -1)
    {
            UnityMainThreadDispatcher.Instance().logError($"message BEFORE CompleteLocationChecks for location ID {locationID}");
            session.Locations.CompleteLocationChecks(locationID);
            UnityMainThreadDispatcher.Instance().logError($"message AFTER CompleteLocationChecks for location ID {locationID}");
        }
    else
    {
      UnityMainThreadDispatcher.Instance().logError($"AP No locationID for name: {locationName}");
    }
  }

  public void Close()
  {
    UnityMainThreadDispatcher.Instance().log("AP Disconnecting");
    session.Socket.DisconnectAsync();
    UnityMainThreadDispatcher.Instance().log("AP Disconnected");
  }

  public void BuildItemReciver(Action<string> GiveItem)
  {

    UnityMainThreadDispatcher.Instance().log("AP Building Item Reiever");
    ApDebugLog.Instance.DisplayMessage($"Built item reciever");

    session.Items.ItemReceived += (receivedItemsHelper) =>
    {
      try
      {

        UnityMainThreadDispatcher.Instance().log("AP Item recieved trigger");
        ApDebugLog.Instance.DisplayMessage("Item Recieved");
      }
      catch (Exception e)
      {
        UnityMainThreadDispatcher.Instance().log($"Error in printing message {e.Message},{e.StackTrace}");
        ApDebugLog.Instance.DisplayMessage($"Error in printing message {e.Message},{e.StackTrace}", duration: 10f);
      }

      try
      {
        var itemReceivedInfo = receivedItemsHelper.DequeueItem();

        if (receivedItemsHelper.Index <= FactSystem.GetFact(new Fact("APExpectedIndex")))
        {
          return;
        }

        UnityMainThreadDispatcher.Instance().log($"AP Atempting to give {itemReceivedInfo.ItemName}");
        ApDebugLog.Instance.DisplayMessage($"Atempting to give {itemReceivedInfo.ItemName} with index {receivedItemsHelper.Index}");
        GiveItem(itemReceivedInfo.ItemName);
        FactSystem.AddToFact(new Fact("APExpectedIndex"), 1);
        SaveSystem.Save();



      }
      catch (Exception e)
      {
        UnityMainThreadDispatcher.Instance().log($"Error in giving item {e.Message},{e.StackTrace}");
        ApDebugLog.Instance.DisplayMessage($"Error in giving item {e.Message},{e.StackTrace}", duration: 10f);
      }
    };
  }

  public void buildMessageReciver()
  {
    session.MessageLog.OnMessageReceived += (message) =>
    {
      ApDebugLog.Instance.DisplayMessage(message.ToString(), isDebug: false);
    };
  }

  public void CompleteGame()
  {
    UnityMainThreadDispatcher.Instance().log("AP Game is completed, it should release now");
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

  public bool IsLocationChecked(string location)
  {
    return session.Locations.AllLocationsChecked.Contains(session.Locations.GetLocationIdFromName(game, location));
  }
}
