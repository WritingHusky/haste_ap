using APConnection;
using static Integration.Integration;
using Landfall.Haste;
using Landfall.Modding;
using UnityEngine;
using UnityEngine.Localization;
using Zorro.Settings;

namespace HelloWorld;

[LandfallPlugin]
public class Program
{
    private static Connection? connection;
    static Program()
    {
        Debug.Log("AP Program launched");

        // Load everything up when the games starts from menu
        On.GameHandler.PlayFromMenu += StaticPlayFromMenuHook;
        On.GameHandler.LoadMainMenu += StaticLoadMainMenuHook;
        On.Landfall.Haste.MetaProgression.Unlock += StaticMetaProgressionUnlockOverloadHook;
    }

    private static void StaticPlayFromMenuHook(On.GameHandler.orig_PlayFromMenu orig)
    {
        FactSystem.SetFact(new Fact("APForceReloadFirstLoad"), 0f);

        FactSystem.SubscribeToFact(new Fact("current_unbeaten_shard"), (value) =>
        {
            if (connection == null)
                return;

            // Never allow the value to be not what I want it to be
            if (value != connection.shardCount)
            {
                FactSystem.SetFact(new Fact("current_unbeaten_shard"), connection.shardCount);
            }
        });

        SaveSystem.Save();

        if (connection != null)
        {
            Debug.LogError("AP Play button clicked and connection is not null");
        }

        var settingsHandler = GameHandler.Instance.SettingsHandler;
        var enabledSetting = settingsHandler.GetSetting<ApEnabledSetting>().Value;

        if (!enabledSetting)
        {
            Debug.Log("AP not started as it was disabled");

            Debug.Log("AP Transitioning to original actions");
            orig();
            return;
        }

        Debug.Log("AP enabled so begining startup");
        var serverName = settingsHandler.GetSetting<ApServerNameSetting>().Value;
        var serverPort = settingsHandler.GetSetting<ApServerPortSetting>().Value;
        var username = settingsHandler.GetSetting<ApUsernameSetting>().Value;
        var password = settingsHandler.GetSetting<ApPasswordSetting>().Value;

        if (password == "")
        {
            password = null;
        }

        connection = new(serverName, serverPort);
        connection.Connect(username, password);

        Integration.Integration.connection = connection;


        // Setup default savestate

        // Add AP Server Connection based actions

        // Connects the onItemRecive to the Give item
        // This will happen for every item including  
        connection.BuildItemReciver(GiveItem);

        // Add Game base Actions
        Debug.Log("AP Creating Hooks");

        // Override the handling of completing of a run to 
        On.GM_API.OnRunEnd += StaticCompleteRunHook;

        On.GM_API.OnEndBossWin += StaticEndBossHook;

        // When a boss is defeated send the location
        On.GM_API.OnBossDeath += StaticBossDeathHook;

        On.ShopItemHandler.BuyItem += StaticBuyItemHook;

        On.SaveSystem.Load += StaticSaveLoadHook;


        Debug.Log("AP Hooks Complete");


        if (FactSystem.GetFact(new Fact("APDeathlink")) == 1f)
        {
            Debug.Log("AP DeathLink is Enabled");

            connection.deathLinkService!.OnDeathLinkReceived += GiveDeath;

            connection.deathLinkService!.EnableDeathLink();

            On.Player.Die += StaticSendDeathOnDie;

            Debug.Log("AP DeathLink Hooked");
        }
        else
        {
            Debug.Log("AP Deathlink is Disabled");
        }

        // Once the player starts in game do the loading as somethings are not setup yet
        On.GM_API.OnSpawnedInHub += StaticLoadHubHook;

        Debug.Log("AP Transitioning to original actions");
        orig();
    }


    private static void StaticSendDeathOnDie(On.Player.orig_Die orig, Player self)
    {
        Debug.Log("AP Player death Hooked");
        connection!.deathLinkService!.SendDeathLink(new Archipelago.MultiClient.Net.BounceFeatures.DeathLink.DeathLink(connection.username));
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
        Debug.Log("AP Loading into Hub");

        UpdateShardCount(connection!.shardCount);

        // Only do these on the first load of the game
        if (FactSystem.GetFact(new Fact("APForceReloadFirstLoad")) == 0f)
        {
            FactSystem.SetFact(new Fact("APForceReloadFirstLoad"), 1f);

            SetDefeaultState();

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
            connection.Close();
            connection = null;

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
            throw new Exception("AP On Complete Run the connection is null");
        }

        PlayerProgress.SetShardComplete(0);
    }

    /// <summary>
    /// Hook into when a player buys and item. Used to give item locations
    /// </summary>
    private static void StaticBuyItemHook(On.ShopItemHandler.orig_BuyItem orig, ShopItemHandler self, ItemInstance item, int price, ShopItem shopItem)
    {
        if (connection == null)
        {
            throw new Exception("AP On Get Item the connection is null");
        }
        var currentShard = RunHandler.RunData.shardID;
        var item_location = "Shard " + currentShard + " Shop Item";
        Debug.Log("AP sending Item: (" + item_location + ")");

        // TODO add handling for numeral item locations

        connection.SendLocation(item_location);
        // Buy the item
        orig(self, item, price, shopItem);
    }

    /// <summary>
    /// Hook into when a Boss is deafeted. Used to give boss locations
    /// </summary>
    private static void StaticBossDeathHook(On.GM_API.orig_OnBossDeath orig)
    {
        if (connection == null)
        {
            throw new Exception("AP On Boss Death the connection is null");
        }
        var currentShard = RunHandler.RunData.shardID;
        var boss_location = "Shard " + currentShard + " Boss";
        Debug.Log("AP sending Boss: (" + boss_location + ")");

        // TODO add handling for numeral boss locations

        connection.SendLocation(boss_location);
        orig();
    }

    /// <summary>
    /// Hook into when the end Boss is deafeted. Used release the world
    /// </summary>
    private static void StaticEndBossHook(On.GM_API.orig_OnEndBossWin orig)
    {
        if (connection == null)
        {
            throw new Exception("AP On End Boss Win the connection is null");
        }
        connection.CompleteGame();
        orig();
    }

    private static void StaticMetaProgressionUnlockOverloadHook(On.Landfall.Haste.MetaProgression.orig_Unlock orig, AbilityKind ability)
    {
        if (connection == null)
        {
            throw new Exception("AP On Progession Unlock the connection is null");
        }
        var boss_location = "Ability " + Enum.GetName(typeof(AbilityKind), ability);
        Debug.Log("AP sending Ability: (" + boss_location + ")");

        // TODO add handling for numeral boss locations

        connection.SendLocation(boss_location);
        // orig();
    }
}



// Settings For AP server connection
[HasteSetting]
public class ApEnabledSetting : BoolSetting, IExposedSetting
{
    public override LocalizedString OffString => new UnlocalizedString("AP off");

    public override LocalizedString OnString => new UnlocalizedString("AP on");

    public override void ApplyValue() => Debug.Log($"AP Toggled AP to {Value}");
    protected override bool GetDefaultValue() => false;
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Toggle");
    public string GetCategory() => SettingCategory.General;
}

[HasteSetting]
public class ApServerNameSetting : StringSetting, IExposedSetting
{
    public override void ApplyValue() => Debug.Log($"New AP hostname {Value}");
    protected override string GetDefaultValue() => "localhost";
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Server name");
    public string GetCategory() => SettingCategory.General;
}

[HasteSetting]
public class ApServerPortSetting : IntSetting, IExposedSetting
{
    public override void ApplyValue() => Debug.Log($"New AP hostport {Value}");
    protected override int GetDefaultValue() => 38281;
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Server Port");
    public string GetCategory() => SettingCategory.General;
}

[HasteSetting]
public class ApUsernameSetting : StringSetting, IExposedSetting
{
    public override void ApplyValue() => Debug.Log($"New AP username {Value}");
    protected override string GetDefaultValue() => "Player1";
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Username");
    public string GetCategory() => SettingCategory.General;
}
[HasteSetting]
public class ApPasswordSetting : StringSetting, IExposedSetting
{
    public override void ApplyValue() => Debug.Log($"New AP Password {Value}");
    protected override string GetDefaultValue() => "";
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Password");
    public string GetCategory() => SettingCategory.General;
}