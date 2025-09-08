using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Colors;
using Archipelago.MultiClient.Net.Enums;
using Landfall.Haste;
using UnityEngine.Purchasing.MiniJSON;
using Zorro.Settings;

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

        // workshop only gives access to the latest mod version, so i'll need to have backwards compat eventually
        if (loginSuccess.SlotData.TryGetValue("Version", out object VersionNum))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found Version in slot data with value: {VersionNum}");
            string[] subs = VersionNum.ToString().Split('.');
            FactSystem.SetFact(new Fact("APVersionMajor"), Convert.ToSingle(int.Parse(subs[0])));
            FactSystem.SetFact(new Fact("APVersionMiddle"), Convert.ToSingle(int.Parse(subs[1])));
            FactSystem.SetFact(new Fact("APVersionMinor"), Convert.ToSingle(int.Parse(subs[2])));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get Version from slot data:" + loginSuccess.SlotData.toJson());
            FactSystem.SetFact(new Fact("APVersionMajor"), Convert.ToSingle(0));
            FactSystem.SetFact(new Fact("APVersionMiddle"), Convert.ToSingle(2));
            FactSystem.SetFact(new Fact("APVersionMinor"), Convert.ToSingle(0));
        }

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
                if (loginSuccess.SlotData.TryGetValue("Per-Shard Shopsanity Quantity", out object PSShopsanityQuantity))
                {
                    UnityMainThreadDispatcher.Instance().log($"AP found ShopsanityQuantity in slot data with value: {PSShopsanityQuantity}");
                    FactSystem.SetFact(new Fact("APShopsanityQuantity"), Convert.ToSingle(PSShopsanityQuantity));
                }
                else
                {
                    UnityMainThreadDispatcher.Instance().logError("AP Failed to get ShopsanityQuantity from slot data:" + loginSuccess.SlotData.toJson());
                }
            }
            else if (FactSystem.GetFact(new Fact("APShopsanity")) == 2f)
            {

                if (loginSuccess.SlotData.TryGetValue("Global Shopsanity Quantity", out object GlobalShopsanityQuantity))
                {
                    UnityMainThreadDispatcher.Instance().log($"AP found ShopsanityQuantity in slot data with value: {GlobalShopsanityQuantity}");
                    FactSystem.SetFact(new Fact("APShopsanityQuantity"), Convert.ToSingle(GlobalShopsanityQuantity));
                }
                else
                {
                    UnityMainThreadDispatcher.Instance().logError("AP Failed to get ShopsanityQuantity from slot data:" + loginSuccess.SlotData.toJson());
                }
            }
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get Shopsanity from slot data:" + loginSuccess.SlotData.toJson());
            // Might default the value here to make things consistant
        }


        if (loginSuccess.SlotData.TryGetValue("NPC Shuffle", out object NPCShuffle))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found NPC Shuffle in slot data with value: {NPCShuffle}");
            FactSystem.SetFact(new Fact("APNPCShuffle"), Convert.ToSingle(NPCShuffle));
            if (Convert.ToSingle(NPCShuffle) == 1)
            {
                if (FactSystem.GetFact(new Fact("APCaptainInHub")) == 0f) FactSystem.SetFact(new Fact("APCaptainInHub"), 0f);
                if (FactSystem.GetFact(new Fact("APHeirInHub")) == 0f) FactSystem.SetFact(new Fact("APHeirInHub"), 0f);
                if (FactSystem.GetFact(new Fact("APWraithInHub")) == 0f) FactSystem.SetFact(new Fact("APWraithInHub"), 0f);
                if (FactSystem.GetFact(new Fact("APFashionInHub")) == 0f) FactSystem.SetFact(new Fact("APFashionInHub"), 0f);
                if (FactSystem.GetFact(new Fact("APSageInHub")) == 0f) FactSystem.SetFact(new Fact("APSageInHub"), 0f);
            } else
            {
                // rather safe than sorry
                FactSystem.SetFact(new Fact("APCaptainInHub"), 1f);
                FactSystem.SetFact(new Fact("APHeirInHub"), 1f);
                FactSystem.SetFact(new Fact("APWraithInHub"), 1f);
                FactSystem.SetFact(new Fact("APFashionInHub"), 1f);
                FactSystem.SetFact(new Fact("APSageInHub"), 1f);
            }
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get NPCShuffle from slot data:" + loginSuccess.SlotData.toJson());
            FactSystem.SetFact(new Fact("APCaptainInHub"), 1f);
            FactSystem.SetFact(new Fact("APHeirInHub"), 1f);
            FactSystem.SetFact(new Fact("APWraithInHub"), 1f);
            FactSystem.SetFact(new Fact("APFashionInHub"), 1f);
            FactSystem.SetFact(new Fact("APSageInHub"), 1f);
        }


        if (loginSuccess.SlotData.TryGetValue("Shard Goal", out object ShardGoal))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found ShardGoal in slot data with value: {ShardGoal}");
            FactSystem.SetFact(new Fact("APShardGoal"), Convert.ToSingle(ShardGoal));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get ShardGoal from slot data:" + loginSuccess.SlotData.toJson());
            FactSystem.SetFact(new Fact("APShardGoal"), 10f);
        }


        if (loginSuccess.SlotData.TryGetValue("Fragmentsanity", out object Fragmentsanity))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found Fragmentsanity in slot data with value: {Fragmentsanity}");
            FactSystem.SetFact(new Fact("APFragmentsanity"), Convert.ToSingle(Fragmentsanity));

            if (FactSystem.GetFact(new Fact("APFragmentsanity")) == 1f)
            {
                loginSuccess.SlotData.TryGetValue("Per-Shard Fragmentsanity Quantity", out object FQ);
                FactSystem.SetFact(new Fact("APFragmentsanityQuantity"), Convert.ToSingle(FQ));

            } else if (FactSystem.GetFact(new Fact("APFragmentsanity")) == 2f)
            {
                if (FactSystem.GetFact(new Fact("APFragmentsanityGlobal")) == 0f) FactSystem.SetFact(new Fact("APFragmentsanityGlobal"), 0f);

                loginSuccess.SlotData.TryGetValue("Global Fragmentsanity Quantity", out object FQ);
                FactSystem.SetFact(new Fact("APFragmentsanityQuantity"), Convert.ToSingle(FQ));

            }

            loginSuccess.SlotData.TryGetValue("Fragmentsanity Distribution", out object FragmentsanityDist);
            UnityMainThreadDispatcher.Instance().log($"AP found Fragmentsanity Distribution in slot data with value: {FragmentsanityDist}");
            FactSystem.SetFact(new Fact("APFragmentsanityDistribution"), Convert.ToSingle(FragmentsanityDist));
            if (Convert.ToSingle(FragmentsanityDist) == 1f)
            {
                loginSuccess.SlotData.TryGetValue("Linear Fragmentsanity Rate", out object LFR);
                if (FactSystem.GetFact(new Fact("APFragmentLimitShard1")) == 0f) FactSystem.SetFact(new Fact("APFragmentLimitShard1"), Convert.ToSingle(LFR));
                if (FactSystem.GetFact(new Fact("APFragmentLimitShard2")) == 0f) FactSystem.SetFact(new Fact("APFragmentLimitShard2"), Convert.ToSingle(LFR));
                if (FactSystem.GetFact(new Fact("APFragmentLimitShard3")) == 0f) FactSystem.SetFact(new Fact("APFragmentLimitShard3"), Convert.ToSingle(LFR));
                if (FactSystem.GetFact(new Fact("APFragmentLimitShard4")) == 0f) FactSystem.SetFact(new Fact("APFragmentLimitShard4"), Convert.ToSingle(LFR));
                if (FactSystem.GetFact(new Fact("APFragmentLimitShard5")) == 0f) FactSystem.SetFact(new Fact("APFragmentLimitShard5"), Convert.ToSingle(LFR));
                if (FactSystem.GetFact(new Fact("APFragmentLimitShard6")) == 0f) FactSystem.SetFact(new Fact("APFragmentLimitShard6"), Convert.ToSingle(LFR));
                if (FactSystem.GetFact(new Fact("APFragmentLimitShard7")) == 0f) FactSystem.SetFact(new Fact("APFragmentLimitShard7"), Convert.ToSingle(LFR));
                if (FactSystem.GetFact(new Fact("APFragmentLimitShard8")) == 0f) FactSystem.SetFact(new Fact("APFragmentLimitShard8"), Convert.ToSingle(LFR));
                if (FactSystem.GetFact(new Fact("APFragmentLimitShard9")) == 0f) FactSystem.SetFact(new Fact("APFragmentLimitShard9"), Convert.ToSingle(LFR));
                if (FactSystem.GetFact(new Fact("APFragmentLimitShard10")) == 0f) FactSystem.SetFact(new Fact("APFragmentLimitShard10"), Convert.ToSingle(LFR));
                if (FactSystem.GetFact(new Fact("APFragmentLimitGlobal")) == 0f) FactSystem.SetFact(new Fact("APFragmentLimitGlobal"), Convert.ToSingle(LFR));
            }
            
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get Fragmentsanity from slot data:" + loginSuccess.SlotData.toJson());
            FactSystem.SetFact(new Fact("APFragmentsanity"), 0f);
            FactSystem.SetFact(new Fact("APFragmentsanityDistribution"), 0f);
            FactSystem.SetFact(new Fact("APFragmentsanityQuantity"), 0f);
        }

        if (loginSuccess.SlotData.TryGetValue("Speed Upgrades", out object SpeedUpgrades))
        {

            UnityMainThreadDispatcher.Instance().log($"AP found SpeedUpgrades in slot data with value: {SpeedUpgrades}");
            FactSystem.SetFact(new Fact("APSpeedUpgrades"), Convert.ToSingle(SpeedUpgrades));
            if (FactSystem.GetFact(new Fact("APSpeedUpgrades")) == 1f)
            {
                if (FactSystem.GetFact(new Fact("APSpeedUpgradesCollected")) == 0f) FactSystem.SetFact(new Fact("APSpeedUpgradesCollected"), 0f);
            }
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get SpeedUpgrades from slot data:" + loginSuccess.SlotData.toJson());
            FactSystem.SetFact(new Fact("APSpeedUpgrades"), 0f);
        }

        if (loginSuccess.SlotData.TryGetValue("Default Outfit Body", out object DefSkinBody))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found DefaultOutfitBody in slot data with value: {DefSkinBody}");
            FactSystem.SetFact(new Fact("equipped_skin_body"), Convert.ToSingle(DefSkinBody));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get DefaultOutfitBody from slot data:" + loginSuccess.SlotData.toJson());
        }

        if (loginSuccess.SlotData.TryGetValue("Default Outfit Hat", out object DefSkinHat))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found DefaultOutfitHat in slot data with value: {DefSkinHat}");
            FactSystem.SetFact(new Fact("equipped_skin_head"), Convert.ToSingle(DefSkinHat));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get DefaultOutfitHat from slot data:" + loginSuccess.SlotData.toJson());
        }

        // get the AP Debug Log settings.
        // normally this value gets set when you toggle the settings from the menu, however the initial value from previous sessions needs to be loaded manually at the start for it to take effect
        var settingsHandler = GameHandler.Instance.SettingsHandler;
        FactSystem.SetFact(new Fact("APDebugLogEnabled"), settingsHandler.GetSetting<ApDebugEnabledSetting>().Value ? 1f : 0f);
        FactSystem.SetFact(new Fact("APMessageFilter"), (float)settingsHandler.GetSetting<ApLogFilter>().Value);


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
            session.Locations.CompleteLocationChecks(locationID);
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError($"AP No locationID for name: {locationName}");
        }
    }

    public void SendHintedLocation(string locationName)
    {
        UnityMainThreadDispatcher.Instance().log($"AP Scouting location {locationName}");
        ApDebugLog.Instance.DisplayMessage($"Scouting location {locationName}");
        long locationID = session.Locations.GetLocationIdFromName(game, locationName);
        if (locationID != -1)
        {
            session.Locations.ScoutLocationsAsync(hintCreationPolicy: HintCreationPolicy.CreateAndAnnounceOnce, locationID);
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
        ApDebugLog.Instance.DisplayMessage($"AP Disconnected", isDebug:false);
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
                if (e.GetType() != typeof(NullReferenceException))
                {
                    UnityMainThreadDispatcher.Instance().log($"Error in giving item {e.Message},{e.StackTrace}");
                    ApDebugLog.Instance.DisplayMessage($"Error in giving item {e.Message},{e.StackTrace}", duration: 10f);
                }
            }
        };
    }

    public void buildMessageReciver()
    {
        session.MessageLog.OnMessageReceived += (message) =>
        {
            ApDebugLog.Instance.DisplayMessage(message, isDebug: false);
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
