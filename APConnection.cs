using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Colors;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Exceptions;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Packets;
using Landfall.Haste;
using Landfall.Haste.Music;
using UnityEngine.Purchasing.MiniJSON;
using Zorro.Settings;
using Timer = System.Timers.Timer;

namespace APConnection;

public class Connection(string hostname, int port)
{
    private string server = hostname + port.ToString();
    public string game = "Haste";

    public string dataTag = "Haste";
    public ArchipelagoSession session = ArchipelagoSessionFactory.CreateSession(hostname, port);
    private string hostname = hostname;
    private int port = port;

    public string username = "";
    public string password = "";

    public DeathLinkService? deathLinkService;

    private static Timer backgroundTimer;
    private Queue<Action> actions = new ();
    public bool WillNeedToForceReload = false;
    public bool HasDisconnected = false;
    public bool AttemptingReconnect = false;
    private List<string> LocationsToBeSent = [];

    public bool Connect(string user = "Player1", string? pass = null, Version? modVersion = null)
    {
        username = user;
        password = pass;
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
            ApDebugLog.Instance.DisplayMessage("AP Connection failed", isDebug:false);
            ApDebugLog.Instance.DisplayMessage(errorMessage, isDebug: false);
            return false;
        }

        // Successfully connected, `ArchipelagoSession` (assume statically defined as `session` from now on) can now be used to interact with the server and the returned `LoginSuccessful` contains some useful information about the initial connection (e.g. a copy of the slot data as `loginSuccess.SlotData`)
        var loginSuccess = (LoginSuccessful)result;
        UnityMainThreadDispatcher.Instance().log("AP Connection Succeeded");

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

        // workshop only gives access to the latest mod version, so there will need to be backwards compat in some places
        if (loginSuccess.SlotData.TryGetValue("Version", out object VersionNum))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found Version in slot data with value: {VersionNum}");

            var apVersion = new Version((string)VersionNum);

            if (modVersion.CompareTo(apVersion) > 0)
            {
                // mod is newer than AP
                ApDebugLog.Instance.DisplayMessage($"<color=#FFFF00>WARNING:</color> Mod version ({modVersion}) is newer than APworld version ({apVersion}).\nYou may experience some glitches due to this version mismatch.\nPlease update your APworld if possible to ensure the smoothest experience.", isDebug:false);
            }

            if (FactSystem.GetFact(new Fact("APVersionMajor")) != 0 ||
                FactSystem.GetFact(new Fact("APVersionMiddle")) != 0)
            {
                // if either of these values are pre-set it means that the savedata is already AP, and needs to be checked for a mismatch
                string storedAPworldVersion = $"{FactSystem.GetFact(new Fact("APVersionMajor"))}.{FactSystem.GetFact(new Fact("APVersionMiddle"))}.{FactSystem.GetFact(new Fact("APVersionMinor"))}";
                if (apVersion.CompareTo(new Version(storedAPworldVersion)) != 0)
                {
                    ApDebugLog.Instance.DisplayMessage($"<color=#FF0000>ERROR:</color> The APworld version of the Archipelago room ({apVersion}) does not match the version stored in the selected savefile ({storedAPworldVersion}).\nPlease switch to the correct savefile (from the 'General' settings menu) or create a new one.", isDebug:false);
                    return false;
                }
            }
            

            string[] subs = VersionNum.ToString().Split('.');
            FactSystem.SetFact(new Fact("APVersionMajor"), Convert.ToSingle(int.Parse(subs[0])));
            FactSystem.SetFact(new Fact("APVersionMiddle"), Convert.ToSingle(int.Parse(subs[1])));
            FactSystem.SetFact(new Fact("APVersionMinor"), Convert.ToSingle(int.Parse(subs[2])));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get Version from slot data:" + loginSuccess.SlotData.toJson());
            FactSystem.SetFact(new Fact("APVersionMajor"), 0f);
            FactSystem.SetFact(new Fact("APVersionMiddle"), 2f);
            FactSystem.SetFact(new Fact("APVersionMinor"), 0f);
            ApDebugLog.Instance.DisplayMessage($"<color=#FF0000>ERROR:</color> Your APworld is so out of date that it doesn't even have a version number, and almost certainly will not work properly with this mod (v{modVersion}).\nPlease update your APworld as soon as possible to ensure your game will function correctly.", isDebug: false);
        }

        if (loginSuccess.SlotData.TryGetValue("ForceReload", out object ForceReload))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found ForceReload in slot data with value: {ForceReload}");
            FactSystem.SetFact(new Fact("APForceReload"), Convert.ToSingle(ForceReload));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get ForceReaload from slot data:" + loginSuccess.SlotData.toJson());
        }

        if (loginSuccess.SlotData.TryGetValue("Shard Unlock Order", out object ShardUnlockOrder))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found ShardUnlockOrder in slot data with value: {ShardUnlockOrder}");
            FactSystem.SetFact(new Fact("APShardUnlockOrder"), Convert.ToSingle(ShardUnlockOrder));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get ShardUnlockOrder from slot data:" + loginSuccess.SlotData.toJson());
            FactSystem.SetFact(new Fact("APShardUnlockOrder"), 0f);
        }

        if (loginSuccess.SlotData.TryGetValue("Remove Post-Victory Locaitons", out object RemovePVL))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found RemovePVL in slot data with value: {RemovePVL}");
            FactSystem.SetFact(new Fact("APRemovePVL"), Convert.ToSingle(RemovePVL));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get RemovePVL from slot data:" + loginSuccess.SlotData.toJson());
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

        if (loginSuccess.SlotData.TryGetValue("Shopsanity Seperate", out object ShopsanitySeperate))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found ShopsanitySeperate in slot data with value: {ShopsanitySeperate}");
            FactSystem.SetFact(new Fact("APShopsanitySeperate"), Convert.ToSingle(ShopsanitySeperate));
            if (loginSuccess.SlotData.TryGetValue("Shopsanity Seperate Rate", out object ShopsanitySeperateRate))
            {
                UnityMainThreadDispatcher.Instance().log($"AP found ShopsanitySeperate in slot data with value: {ShopsanitySeperateRate}");
                FactSystem.SetFact(new Fact("APShopsanitySeperateRate"), Convert.ToSingle(ShopsanitySeperateRate));
            }
            else
            {
                UnityMainThreadDispatcher.Instance().logError("AP Failed to get ShopsanitySeperateRate from slot data:" + loginSuccess.SlotData.toJson());
                FactSystem.SetFact(new Fact("APShopsanitySeperateRate"), 2f);
            }
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get ShopsanitySeperate from slot data:" + loginSuccess.SlotData.toJson());
            FactSystem.SetFact(new Fact("APShopsanitySeperate"), 0f);
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
                if (FactSystem.GetFact(new Fact("APFirstLoad")) == 0f)
                {
                    loginSuccess.SlotData.TryGetValue("Linear Fragmentsanity Rate", out object LFR);
                    FactSystem.SetFact(new Fact("APFragmentLimitShard1"), Convert.ToSingle(LFR));
                    FactSystem.SetFact(new Fact("APFragmentLimitShard2"), Convert.ToSingle(LFR));
                    FactSystem.SetFact(new Fact("APFragmentLimitShard3"), Convert.ToSingle(LFR));
                    FactSystem.SetFact(new Fact("APFragmentLimitShard4"), Convert.ToSingle(LFR));
                    FactSystem.SetFact(new Fact("APFragmentLimitShard5"), Convert.ToSingle(LFR));
                    FactSystem.SetFact(new Fact("APFragmentLimitShard6"), Convert.ToSingle(LFR));
                    FactSystem.SetFact(new Fact("APFragmentLimitShard7"), Convert.ToSingle(LFR));
                    FactSystem.SetFact(new Fact("APFragmentLimitShard8"), Convert.ToSingle(LFR));
                    FactSystem.SetFact(new Fact("APFragmentLimitShard9"), Convert.ToSingle(LFR));
                    FactSystem.SetFact(new Fact("APFragmentLimitShard10"), Convert.ToSingle(LFR));
                    FactSystem.SetFact(new Fact("APFragmentLimitGlobal"), Convert.ToSingle(LFR));
                }
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
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get SpeedUpgrades from slot data:" + loginSuccess.SlotData.toJson());
            FactSystem.SetFact(new Fact("APSpeedUpgrades"), 0f);
        }

        if (loginSuccess.SlotData.TryGetValue("Fashion Weeboh's Purchases", out object FashionPurchases))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found FashionPurchases in slot data with value: {FashionPurchases}");
            FactSystem.SetFact(new Fact("APFashionPurchases"), Convert.ToSingle(FashionPurchases));
            // only do this upon init
            if (FactSystem.GetFact(new Fact("APFirstLoad")) == 0f)
            {
                if (Convert.ToSingle(FashionPurchases) == 2f)
                {
                    // Unlocks Cripsy and Twisted and locks Green and Blue
                    SkinManager.UnlockSkin(SkinManager.Skin.Crispy);
                    SkinManager.UnlockSkin(SkinManager.Skin.DarkClown);
                    SkinManager.UnpurchaseSkin(SkinManager.Skin.Green);
                    SkinManager.UnpurchaseSkin(SkinManager.Skin.Blue);
                    SkinManager.UnlockSkin(SkinManager.Skin.Green);
                    SkinManager.UnlockSkin(SkinManager.Skin.Blue);
                    // manually unlocks Zoe64 and Shadow since the default unlocker is bypassed
                    SkinManager.UnlockSkin(SkinManager.Skin.Zoe64);
                    SkinManager.UnlockSkin(SkinManager.Skin.Shadow);
                }
                else if (Convert.ToSingle(FashionPurchases) == 3f)
                {
                    // Unlocks all
                    SkinManager.UnlockAllSkins();
                    // weird guys
                    SkinManager.UnpurchaseSkin(SkinManager.Skin.Green);
                    SkinManager.UnpurchaseSkin(SkinManager.Skin.Blue);
                    SkinManager.UnlockSkin(SkinManager.Skin.Green);
                    SkinManager.UnlockSkin(SkinManager.Skin.Blue);
                }
            }
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get FashionPurchases from slot data:" + loginSuccess.SlotData.toJson());
        }

        if (loginSuccess.SlotData.TryGetValue("Starting Ability", out object StartingAbility))
        {

            UnityMainThreadDispatcher.Instance().log($"AP found StartingAbility in slot data with value: {StartingAbility}");
            if (FactSystem.GetFact(new Fact("APFirstLoad")) == 0f)
            {
                if (Convert.ToSingle(StartingAbility) == 0f){
                    FactSystem.SetFact(new Fact("APNoAbility"), 1f);
                } else
                {
                    FactSystem.SetFact(new Fact("active_ability"), Convert.ToSingle(StartingAbility) - 1f);
                }
            }
            FactSystem.SetFact(new Fact("APStartingAbility"), Convert.ToSingle(StartingAbility));
            switch (Convert.ToSingle(StartingAbility))
            {
                case 1f:
                    // the new unlock function (that handles board) isnt set by this point so i gotta set the fact manually here lmao
                    FactSystem.SetFact(new Fact("APBoostUnlocked"), 1f);
                    break;
                case 2f:
                    MetaProgression.Unlock(AbilityKind.Slomo);
                    break;
                case 3f:
                    MetaProgression.Unlock(AbilityKind.Grapple);
                    break;
                case 4f:
                    MetaProgression.Unlock(AbilityKind.Fly);
                    break;

            }
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get StartingAbility from slot data:" + loginSuccess.SlotData.toJson());
            // just in case
            FactSystem.SetFact(new Fact("APBoostUnlocked"), 1f);
        }

        if (loginSuccess.SlotData.TryGetValue("Persistent Items", out object PermItems))
        {

            UnityMainThreadDispatcher.Instance().log($"AP found PermItems in slot data with value: {PermItems}");
            FactSystem.SetFact(new Fact("APPersistentItems"), Convert.ToSingle(PermItems));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get PermItems from slot data:" + loginSuccess.SlotData.toJson());
        }

        if (loginSuccess.SlotData.TryGetValue("S-Rank Bonus", out object SRankBonus))
        {

            UnityMainThreadDispatcher.Instance().log($"AP found SRankBonus in slot data with value: {SRankBonus}");
            FactSystem.SetFact(new Fact("APSRankBonus"), Convert.ToSingle(SRankBonus));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get SRankBonus from slot data:" + loginSuccess.SlotData.toJson());
        }

        if (loginSuccess.SlotData.TryGetValue("Default Outfit Body", out object DefSkinBody))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found DefaultOutfitBody in slot data with value: {DefSkinBody}");
            FactSystem.SetFact(new Fact("equipped_skin_body"), Convert.ToSingle(DefSkinBody));
            if (FactSystem.GetFact(new Fact("APFirstLoad")) == 0f)
            {
                //SkinManager.PurchaseSkin((SkinManager.Skin)Convert.ToInt32(DefSkinBody));
            }
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get DefaultOutfitBody from slot data:" + loginSuccess.SlotData.toJson());
        }

        if (loginSuccess.SlotData.TryGetValue("Default Outfit Hat", out object DefSkinHat))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found DefaultOutfitHat in slot data with value: {DefSkinHat}");
            FactSystem.SetFact(new Fact("equipped_skin_head"), Convert.ToSingle(DefSkinHat));
            if (FactSystem.GetFact(new Fact("APFirstLoad")) == 0f)
            {
                //SkinManager.PurchaseSkin((SkinManager.Skin)Convert.ToInt32(DefSkinHat));
            }
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get DefaultOutfitHat from slot data:" + loginSuccess.SlotData.toJson());
        }

        if (loginSuccess.SlotData.TryGetValue("Captain's Upgrades", out object CaptainsUpgrades))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found CaptainsUpgrades in slot data with value: {CaptainsUpgrades}");
            FactSystem.SetFact(new Fact("APCaptainsRewards"), Convert.ToSingle(CaptainsUpgrades));
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get CaptainsUpgrades from slot data:" + loginSuccess.SlotData.toJson());
        }

        if (loginSuccess.SlotData.TryGetValue("Unlock All Items", out object UnlockAllItems))
        {
            UnityMainThreadDispatcher.Instance().log($"AP found UnlockAllItems in slot data with value: {UnlockAllItems}");
            if (FactSystem.GetFact(new Fact("APFirstLoad")) == 0f && Convert.ToSingle(UnlockAllItems) == 1)
            {
                //only unlock once
                ItemDatabase.UnlockAndShowAll();
            }
        }
        else
        {
            UnityMainThreadDispatcher.Instance().logError("AP Failed to get UnlockAllItems from slot data:" + loginSuccess.SlotData.toJson());
        }




        // get the AP Debug Log and Deathlink preference settings.
        // normally this value gets set when you toggle the settings from the menu, however the initial value from previous sessions needs to be loaded manually at the start for it to take effect
        var settingsHandler = GameHandler.Instance.SettingsHandler;
        FactSystem.SetFact(new Fact("APDebugLogEnabled"), settingsHandler.GetSetting<ApDebugEnabledSetting>().Value ? 1f : 0f);
        FactSystem.SetFact(new Fact("APMessageFilter"), (float)settingsHandler.GetSetting<ApLogFilter>().Value);
        
        FactSystem.SetFact(new Fact("APDeathSendMode"), (float)settingsHandler.GetSetting<ApSendDeathModeSetting>().Value);
        FactSystem.SetFact(new Fact("APDeathReceiveMode"), (float)settingsHandler.GetSetting<ApReceiveDeathModeSetting>().Value);


        SaveSystem.Save();

        return true;
    }

    public void SendDeath()
    {
        if (FactSystem.GetFact(new Fact("APDoubleKillStopper")) == 1f)
        {
            // this kill came from deathlink, so just kill the player but avoid the re-sending of another deathlink
            FactSystem.SetFact(new Fact("APDoubleKillStopper"), 0f);
            return;
        }
        ApDebugLog.Instance.DisplayMessage("Death Link sent");
        deathLinkService!.SendDeathLink(new DeathLink(username));
        
    }

    public void SendLocation(string locationName)
    {
        if (HasDisconnected)
        {
            HasteAP.PrintBothDebug($"Could not send location {locationName} due to connection problems. Once the connection is restored the location will be sent.\nDo NOT close the game until the location has been sent, otherwise location progress may be lost.", isDebug:false);
            LocationsToBeSent.Add(locationName);
            return;
        }
        try
        {
            long locationID = session.Locations.GetLocationIdFromName(game, locationName);
            if (locationID != -1)
            {
                HasteAP.PrintBothDebug($"Sending location {locationName}");
                session.Locations.CompleteLocationChecks(locationID);

            }
            else
            {
                HasteAP.PrintBothDebug($"No locationID for name: {locationName}", isError:true, isDebug:false, duration: 15f);
            }
        }
        catch (Exception e)
        {
            HasteAP.PrintBothDebug($"Error in sending location {locationName}\n{e.Message}", isError:true);
            // this is just assuming its a connection issue that hasn't been noticed, and it'll fix itself with the other functions
            // i will likely regret this implementation within a week
            LocationsToBeSent.Add(locationName);
        }
    }

    public void SendHintedLocation(string locationName)
    {
        if (!PermittedToScout()) return;
        if (HasDisconnected)
        {
            HasteAP.PrintBothDebug($"Could not scout location {locationName} due to connection problems. Please try again once the connection is restored.", isDebug:false);
            return;
        }

        HasteAP.PrintBothDebug($"AP Scouting location {locationName}");
        long locationID = session.Locations.GetLocationIdFromName(game, locationName);
        if (locationID != -1)
        {
            session.Locations.ScoutLocationsAsync(hintCreationPolicy: HintCreationPolicy.CreateAndAnnounceOnce, locationID);
        }
        else
        {
            HasteAP.PrintBothDebug($"No locationID for name: {locationName}", isError:true, isDebug:false, duration: 15f);
        }
    }

    public (string, string) RetrieveLocationData(string locationName)
    {
        if (HasDisconnected)
        {
            HasteAP.PrintBothDebug($"Could not get location {locationName} due to connection problems. Please try again once the connection is restored.", isDebug:false);
            return ("CONNECTION ERROR", "CONNECTION ERROR");
        }
        HasteAP.PrintBothDebug($"AP Retrieving location {locationName}");
        long locationID = session.Locations.GetLocationIdFromName(game, locationName);
        if (locationID != -1)
        {
            var locationInfo = session.Locations.ScoutLocationsAsync(hintCreationPolicy: HintCreationPolicy.None, locationID);
            return (locationInfo.Result[locationID].ItemName, locationInfo.Result[locationID].Player.ToString());
        }
        else
        {
            HasteAP.PrintBothDebug($"No locationID for hint location: {locationName}", isError:true, isDebug:false, duration: 15f);
            return ($"ERROR: Could not find location {locationName}", "UNKNOWN");
        }
    }

    public void Close()
    {
        UnityMainThreadDispatcher.Instance().log("AP Disconnecting");
        session.Socket.DisconnectAsync();
        UnityMainThreadDispatcher.Instance().log("AP Disconnected");
        ApDebugLog.Instance.DisplayMessage($"AP Disconnected", isDebug:false);
    }

    public void SetupHandlers()
    {
        HasteAP.PrintBothDebug("Building socket handlers.");
        session.Items.ItemReceived += BuildItemReceiver;
        session.MessageLog.OnMessageReceived += BuildMessageReceiver;
        session.Socket.ErrorReceived += BuildErrorHandler;
        session.Socket.PacketReceived += BuildPacketHandler;
    }

    public void RemoveHandlers()
    {
        HasteAP.PrintBothDebug("Removing socket handlers.");
        session.Items.ItemReceived -= BuildItemReceiver;
        session.MessageLog.OnMessageReceived -= BuildMessageReceiver;
        session.Socket.ErrorReceived -= BuildErrorHandler;
        session.Socket.PacketReceived -= BuildPacketHandler;
    }
    
    public void BuildItemReceiver(ReceivedItemsHelper rih)
    {
        UnityMainThreadDispatcher.Instance().log("Building item receiver");

        try
        {

            UnityMainThreadDispatcher.Instance().log("AP Item recieved trigger");
            //ApDebugLog.Instance.DisplayMessage("Item Recieved");
        }
        catch (Exception e)
        {
            UnityMainThreadDispatcher.Instance().logError($"Error in printing message {e.Message},{e.StackTrace}");
            ApDebugLog.Instance.DisplayMessage($"<color=#FF0000>ERROR:</color> Error in printing message {e.Message},{e.StackTrace}", duration: 10f, false);
        }
        
        if (IsItSafe())
        {
            int copy = rih.Index;
            UnityMainThreadDispatcher.Instance().log($"Item received of index {copy} and safe to go.");
            ProcessItem(rih, copy, false);
        } else
        {
            // lambda-capture lookin-ass workaround i swear to god
            int copy = rih.Index;
            UnityMainThreadDispatcher.Instance().log($"Enqueuing Item of index {copy} to Actions Queue because its not safe.");
            ApDebugLog.Instance.DisplayMessage($"Enqueuing Item of index {copy} to Actions Queue because its not safe.");
            actions.Enqueue(() => ProcessItem(rih, copy, true));
        }
    }

    private void ProcessItem(ReceivedItemsHelper rih, int index, bool isAsync)
    {
        try
        {
            var itemReceivedInfo = rih.DequeueItem();

            if (index <= FactSystem.GetFact(new Fact("APExpectedIndex")))
            {
                HasteAP.PrintBothDebug($"AP Item {itemReceivedInfo.ItemName} has index {index}, which is not greater than the expected index {FactSystem.GetFact(new Fact("APExpectedIndex"))} and has been ignored");
                return;
            }

            HasteAP.PrintBothDebug($"AP Atempting to give {itemReceivedInfo.ItemName} from {itemReceivedInfo.Player.Name} with index {index}");
            Integration.Integration.GiveItem(itemReceivedInfo.ItemName, itemReceivedInfo.Player.Name);
            FactSystem.AddToFact(new Fact("APExpectedIndex"), 1f);
            if (!isAsync) SaveSystem.Save();



        }
        catch (Exception e)
        {
            if (e.GetType() != typeof(NullReferenceException))
            {
                HasteAP.PrintBothDebug($"Error in giving item {e.Message},{e.StackTrace}", duration: 10f, isError: true);
            }
        }
    }

    public void SendTaglinkPacket(int currentAbility)
    {
         // TODO: actually test that this sends
        BouncePacket packet = new()
        {
            Tags = ["TagLink"],
            Data = new()
            {
                { "time", (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds },
                { "source", session.ConnectionInfo.Slot },
                { "tag", false },
                { "kong", currentAbility }
            }
        };

        session.Socket.SendPacket(packet);
    }

    public void BuildMessageReceiver(LogMessage message)
    {
        ApDebugLog.Instance.DisplayMessage(message, isDebug: false);
    }
    
    
    public void BuildErrorHandler(Exception exception, string message)
    {
        HasteAP.PrintBothDebug($"Connection Error Received: {exception.Message}");

        if (exception.Message.Contains("closed the WebSocket connection") || exception is ArchipelagoSocketClosedException)
        {
            // stop things and try to recover
            ResetSession();
            PrepareReconnect();
        }
    }

    public void BuildPacketHandler(ArchipelagoPacketBase packet)
    {
        if (packet is ConnectedPacket)
        {
            HasteAP.PrintBothDebug("got a packet, its all good now");
            AttemptingReconnect = false;
            HasDisconnected = false;
        }
    }

    public void ResetSession()
    {
        session = null;
        RemoveHandlers();
        session = ArchipelagoSessionFactory.CreateSession(hostname, port);
        SetupHandlers();
        deathLinkService = session.CreateDeathLinkService();
        if (FactSystem.GetFact(new Fact("APDeathlink")) == 1f){

            deathLinkService!.OnDeathLinkReceived += Integration.Integration.GiveDeath;
            deathLinkService!.EnableDeathLink();
        }

        HasDisconnected = true;
    }

    public void PrepareReconnect()
    {
        Thread.Sleep(5000);
        TryReconnect();
    }

    public void TryReconnect()
    {
        if (AttemptingReconnect) return;
        AttemptingReconnect = true;
        //startsession()? mostly just socket functions I dont care about?
        var result = session.TryConnectAndLogin(game, username, ItemsHandlingFlags.AllItems, password: password);
        if (!result.Successful)
        {
            LoginFailure failure = (LoginFailure)result;
            string errorMessage = $"Failed to reconnect to {server} as {username}:";
            foreach (string error in failure.Errors)
            {
                errorMessage += $"\n    {error}";
            }
            foreach (ConnectionRefusedError error in failure.ErrorCodes)
            {
                errorMessage += $"\n    {error}";
            }
            HasteAP.PrintBothDebug(errorMessage);
            HasteAP.PrintBothDebug("Reconnection failed, reattempting...", isDebug:false);
            AttemptingReconnect = false;
            PrepareReconnect();
        }
        else
        {
            HasDisconnected = false;
            AttemptingReconnect = false;
        }
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

    public bool PermittedToScout()
    {
        var setting = GameHandler.Instance.SettingsHandler.GetSetting<ApHubScoutSetting>().Value;
        switch (setting)
        {
            // case HubScoutMode.Yaml:
            //     //TODO: get the info from slotdata or something
            //     return false;
            //     break;
            case HubScoutMode.Disabled:
                return false;
            case HubScoutMode.Enabled:
            default:
                return true;
        }
    }
    
    public bool IsItSafe()
    {
        // NOT loading/looking at any other UI screen
        if (RunHandler.InRun && RunHandler.RunData.currentNodeStatus != NGOPlayer.PlayerNodeStatus.Running)
        {
            HasteAP.PrintBothDebug("Safety Check Fail: In Run and Loading");
            return false;
        }

        // loading
        if (UI_TransitionHandler.IsTransitioning)
        {
            HasteAP.PrintBothDebug("Safety Check Fail: Is Transitioning");
            return false;
        }

        // any UI screen is open
        if (!HasteInputSystem.CanTakeInput() || InteractionHandler.IsPlayingInteraction)
        {
            HasteAP.PrintBothDebug("Safety Check Fail: Player lacks control");
            return false;
        }

        try
        {
            // time is stopped
            if (TimeHandler.instance.stopTime || TimeHandler.instance.collisionTime < 0.999f)
            {
                HasteAP.PrintBothDebug("Safety Check Fail: Time is stopped");
                return false;
            }
        }
        catch {}
        // apparently the Time (tm) object doesn't exist until sometime after loading a savefile, so this breaks everything during the initial load
        
        // in hub and can actually move
        if (FactSystem.GetFact(new Fact("in_run")) == 0f)
        {
            if (FactSystem.GetFact(new Fact("APSafelyInHub")) == 0f)
            {
                HasteAP.PrintBothDebug($"Safety Check Fail: Hub not done yet {FactSystem.GetFact(new Fact("in_run"))} {FactSystem.GetFact(new Fact("APSafelyInHub"))}");
                return false;
            }
        }
        
        if (FactSystem.GetFact(new Fact("played_StartGame01")) == 0f)
        {
            HasteAP.PrintBothDebug("Safety Check Fail: Haven't had first conversation with Daro");
            return false;
        }

        return true;
    }

    public void SetTimer()
    {
        UnityMainThreadDispatcher.Instance().log("AP Starting ItemTimer");
        backgroundTimer = new Timer(1000);
        backgroundTimer.Elapsed += (sender, args) =>
        {
            if (session.Items.Any() && actions.Count > 0 && IsItSafe())
            {
                while (actions.Count > 0)
                {
                    var ac = actions.Dequeue();
                    ac.Invoke();
                }

            }
            if (WillNeedToForceReload) Integration.Integration.ForceReload();
            
            if (!HasDisconnected && LocationsToBeSent.Count != 0)
            {
                foreach (string? item in LocationsToBeSent)
                {
                    SendLocation(item);
                }
                LocationsToBeSent.Clear();
            }
        };
        backgroundTimer.AutoReset = true;
        backgroundTimer.Enabled = true;
    }

    public void CloseTimer()
    {
        UnityMainThreadDispatcher.Instance().log("AP Closing ItemTimer");
        backgroundTimer.Enabled = false;
    }

}
