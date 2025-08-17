using APConnection;
using static Integration.Integration;
using Landfall.Haste;
using Landfall.Modding;
using UnityEngine;
using UnityEngine.Localization;
using Zorro.Settings;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using System.Reflection;

[LandfallPlugin]
public partial class Program
{
    private static Connection? connection;

    static Program()
    {
        UnityMainThreadDispatcher.Instance().log("AP Program launched");

        GameObject instance = new(nameof(ApDebugLog));
        UnityEngine.Object.DontDestroyOnLoad(instance);
        instance.AddComponent<ApDebugLog>();

        UnityMainThreadDispatcher.Instance().log("AP Log created");
        ApDebugLog.Instance.DisplayMessage("Archiplego Installed v0.2.0", isDebug: false);

        // Load everything up when the games starts from menu
        On.GameHandler.PlayFromMenu += StaticPlayFromMenuHook;
        On.GameHandler.LoadMainMenu += StaticLoadMainMenuHook;

    }

    private static void StaticPlayFromMenuHook(On.GameHandler.orig_PlayFromMenu orig)
    {


        if (connection != null)
        {
            UnityMainThreadDispatcher.Instance().logError("AP Play button clicked and connection is not null");
        }
        ApDebugLog.Instance.DisplayMessage("Begining Build");

        var settingsHandler = GameHandler.Instance.SettingsHandler;
        var enabledSetting = settingsHandler.GetSetting<ApEnabledSetting>().Value;

        if (!enabledSetting)
        {
            UnityMainThreadDispatcher.Instance().log("AP not started as it was disabled");
            ApDebugLog.Instance.DisplayMessage("AP Disabled");

            UnityMainThreadDispatcher.Instance().log("AP Transitioning to original actions");
            FactSystem.SetFact(new Fact("APFirstLoad"), 1f);
            orig();
            return;
        }

        UnityMainThreadDispatcher.Instance().log("AP enabled so begining startup");
        var serverName = settingsHandler.GetSetting<ApServerNameSetting>().Value;
        var serverPort = settingsHandler.GetSetting<ApServerPortSetting>().Value;
        var username = settingsHandler.GetSetting<ApUsernameSetting>().Value;
        var password = settingsHandler.GetSetting<ApPasswordSetting>().Value;

        if (password == "")
        {
            password = null;
        }

        connection = new(serverName, serverPort);

        // This must go BEFORE connection
        connection.BuildItemReciver(GiveItem);
        connection.buildMessageReciver();

        if (!connection.Connect(username, password))
        {
            ApDebugLog.Instance.DisplayMessage("Connection Failed");
            return;
        }
        ApDebugLog.Instance.DisplayMessage("Connected");

        Integration.Integration.connection = connection;

        FactSystem.SetFact(new Fact("APForceReloadFirstLoad"), 0f);
        SetHubState();

        FactSystem.SubscribeToFact(new Fact("current_unbeaten_shard"), (value) =>
        {
            ApDebugLog.Instance.DisplayMessage($"unbeaten shard update to (zero index){value}");
            if (connection == null)
                return;

            var shard_count = connection.GetItemCount("Progressive Shard");
            // Never allow the value to be not what I want it to be
            if (value != shard_count)
            {
                FactSystem.SetFact(new Fact("current_unbeaten_shard"), shard_count);
            }
        });


        // only need to bother with this once a savefile
        if (FactSystem.GetFact(new Fact("APFirstLoad")) == 0f)
        {
            ClearStoryFlags();
        }

        SaveSystem.Save();
        
        if (FactSystem.GetFact(new Fact("APDeathlink")) == 1f)
        {
            UnityMainThreadDispatcher.Instance().log("AP DeathLink is Enabled");
            ApDebugLog.Instance.DisplayMessage("Deathlink Enabled");

            connection.deathLinkService!.OnDeathLinkReceived += GiveDeath;

            connection.deathLinkService!.EnableDeathLink();

            On.Player.Die += StaticSendDeathOnDie;

            UnityMainThreadDispatcher.Instance().log("AP DeathLink Hooked");
        }
        else
        {
            UnityMainThreadDispatcher.Instance().log("AP Deathlink is Disabled");
        }

        // Add Game base Actions
        UnityMainThreadDispatcher.Instance().log("AP Creating Hooks");

        // Override the handling of completing of a run to 
        On.GM_API.OnRunEnd += StaticCompleteRunHook;
        On.GM_API.OnPlayerEnteredPortal += StaticFragmentComplete;

        On.GM_API.OnEndBossWin += StaticEndBossHook;

        // When a boss is defeated send the location
        On.GM_API.OnBossDeath += StaticBossDeathHook;

        On.ShopItemHandler.BuyItem += StaticBuyItemHook;

        On.SaveSystem.Load += StaticSaveLoadHook;

        On.Landfall.Haste.AbilityUnlockScreen.Unlock += StaticMetaProgressionUnlockOverloadHook;
        On.InteractableCharacter.Start += StaticInteractableCharacterStartHook;
        On.InteractableCharacter.Interact += StaticInteractableCharacterInteractHook;


        UnityMainThreadDispatcher.Instance().log("AP Hooks Complete");

        // Once the player starts in game do the loading as somethings are not setup yet
        On.GM_API.OnSpawnedInHub += StaticLoadHubHook;

        FactSystem.SetFact(new Fact("APFirstLoad"), 1f);
        UnityMainThreadDispatcher.Instance().log("AP Transitioning to original actions");
        ApDebugLog.Instance.DisplayMessage("Loading normaly now");
        orig();
    }

    private static void StaticSendDeathOnDie(On.Player.orig_Die orig, Player self)
    {
        UnityMainThreadDispatcher.Instance().log("AP Player death Hooked");
        ApDebugLog.Instance.DisplayMessage("Death Link sent");
        connection!.deathLinkService!.SendDeathLink(new DeathLink(connection.username));
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
        UnityMainThreadDispatcher.Instance().log("AP Loading into Hub");
        ApDebugLog.Instance.DisplayMessage("Loaded into hub");

        UpdateShardCount();

        // Only do these on the first load of the game
        if (FactSystem.GetFact(new Fact("APForceReloadFirstLoad")) == 0f)
        {
            FactSystem.SetFact(new Fact("APForceReloadFirstLoad"), 1f);

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
            ApDebugLog.Instance.DisplayMessage("Removed connection");
            ClearHooks();
            FactSystem.SetFact(new Fact("APForceReloadFirstLoad"), 0f);
        }

        orig();
    }


    /// <summary>
    /// Extra function to remove hooks on exiting the game, so that new hooks arent accidentally installed ontop of existing ones
    /// </summary>
    private static void ClearHooks()
    {
        On.GM_API.OnRunEnd -= StaticCompleteRunHook;
        On.GM_API.OnPlayerEnteredPortal -= StaticFragmentComplete;
        On.GM_API.OnEndBossWin -= StaticEndBossHook;
        On.GM_API.OnBossDeath -= StaticBossDeathHook;
        On.ShopItemHandler.BuyItem -= StaticBuyItemHook;
        On.SaveSystem.Load -= StaticSaveLoadHook;
        On.Landfall.Haste.AbilityUnlockScreen.Unlock -= StaticMetaProgressionUnlockOverloadHook;
        On.InteractableCharacter.Start -= StaticInteractableCharacterStartHook;
        On.InteractableCharacter.Interact -= StaticInteractableCharacterInteractHook;
        On.GM_API.OnSpawnedInHub -= StaticLoadHubHook;
        if (FactSystem.GetFact(new Fact("APDeathlink")) == 1f)
        {
            On.Player.Die -= StaticSendDeathOnDie;

        }
        UnityMainThreadDispatcher.Instance().log("AP Hooks Removed");
    }

    /// <summary>
    /// Hook into run completion. Used to control Player Progress
    /// </summary>
    private static void StaticCompleteRunHook(On.GM_API.orig_OnRunEnd orig, RunHandler.LastRunState state)
    {
        if (connection == null)
        {
            ApDebugLog.Instance.DisplayMessage("AP On Complete Run the connection is null");
            return;
        }
        ApDebugLog.Instance.DisplayMessage("Run Complete");
        UpdateShardCount();
    }

    /// <summary>
    /// Hook into when a player clears a fragment. Used to give fragment locations
    /// </summary>
    private static void StaticFragmentComplete(On.GM_API.orig_OnPlayerEnteredPortal orig, Player player)
    {
        if (connection == null)
        {
            ApDebugLog.Instance.DisplayMessage("AP On Fragment Clear the connection is null");
            return;
        }
        // make sure youre actually in a fragment, not a rest/shop/challenge
        if (FactSystem.GetFact(new Fact("APFragmentsanity")) > 0f && RunHandler.IsInLevelOrChallenge())
        {
            FactSystem.AddToFact(new Fact("APFragmentCounter"), 1f);
            ApDebugLog.Instance.DisplayMessage($"Cleared Fragment. Progress: {FactSystem.GetFact(new Fact("APFragmentCounter"))}/{FactSystem.GetFact(new Fact("APFragmentLimit"))} for Clear {Convert.ToInt32(FactSystem.GetFact(new Fact("APFragmentLocation"))) + 1}");
            if (FactSystem.GetFact(new Fact("APFragmentCounter")) == FactSystem.GetFact(new Fact("APFragmentLimit")))
            {
                connection.SendLocation("Fragment Clear " + (Convert.ToInt32(FactSystem.GetFact(new Fact("APFragmentLocation"))) + 1));
                FactSystem.AddToFact(new Fact("APFragmentLocation"), 1f);
                FactSystem.SetFact(new Fact("APFragmentCounter"), 0f);
                if (FactSystem.GetFact(new Fact("APFragmentsanity")) == 2f)
                {
                    // if MODE H-TRI
                    // LIMIT = floor(FRAGMENTLOCATION / 2)
                    FactSystem.SetFact(new Fact("APFragmentCounter"), (float)Math.Floor(FactSystem.GetFact(new Fact("APFragmentLocation"))/2));
                }
                else if (FactSystem.GetFact(new Fact("APFragmentsanity")) == 3f)
                {
                    // if MODE H-TRI-CAP
                    // LIMIT = min( floor(FRAGMENTLOCATION / 2), 10
                    FactSystem.SetFact(new Fact("APFragmentCounter"), Math.Min((float)Math.Floor(FactSystem.GetFact(new Fact("APFragmentLocation")) / 2), 10f));
                }
                else if (FactSystem.GetFact(new Fact("APFragmentsanity")) == 4f)
                {
                    // if MODE TRI
                    // LIMIT = FRAGMENTLOCATION
                    FactSystem.SetFact(new Fact("APFragmentCounter"), FactSystem.GetFact(new Fact("APFragmentLocation")));
                }

            }
        }
        orig(player);
    }

    /// <summary>
    /// Hook into when a player buys and item. Used to give item locations
    /// </summary>
    private static void StaticBuyItemHook(On.ShopItemHandler.orig_BuyItem orig, ShopItemHandler self, ItemInstance item, int price)
    {
        if (connection == null)
        {
            ApDebugLog.Instance.DisplayMessage("AP On Get Item the connection is null");
            return;
        }
        var item_location = "";
        bool canSend = true;
        if (FactSystem.GetFact(new Fact("APShopsanity")) == 1f)
        {
            var currentShard = RunHandler.RunData.shardID + 1;
            item_location = "Shard " + currentShard + " Shop Item " + (Convert.ToInt32(FactSystem.GetFact(new Fact("APShopsanityShard" + currentShard))) + 1);
            FactSystem.AddToFact(new Fact("APShopsanityShard" + currentShard), 1f);
            canSend = FactSystem.GetFact(new Fact("APShopsanityShard" + currentShard)) <= FactSystem.GetFact(new Fact("APShopsanityQuantity"));
        } else if (FactSystem.GetFact(new Fact("APShopsanity")) == 2f)
        {
            item_location = "Global Shop Item " + Convert.ToInt32(FactSystem.GetFact(new Fact("APShopsanityGlobal")) + 1);
            FactSystem.AddToFact(new Fact("APShopsanityGlobal"), 1f);
            canSend = FactSystem.GetFact(new Fact("APShopsanityGlobal")) <= FactSystem.GetFact(new Fact("APShopsanityQuantity"));
        }

        if (canSend)
        {
            UnityMainThreadDispatcher.Instance().log("AP sending Item: (" + item_location + ")");
            ApDebugLog.Instance.DisplayMessage("Bought Item");

            connection.SendLocation(item_location);
        }
        // Buy the item
        orig(self, item, price);
    }


    /// <summary>
    /// Hook into when a Boss is deafeted. Used to give boss locations
    /// </summary>
    private static void StaticBossDeathHook(On.GM_API.orig_OnBossDeath orig)
    {
        if (connection == null)
        {
            ApDebugLog.Instance.DisplayMessage("AP On Boss Death the connection is null");
            return;
        }
        var currentShard = RunHandler.RunData.shardID + 1;

        var boss_location = "Shard " + currentShard + " Boss";
        UnityMainThreadDispatcher.Instance().log("AP sending Boss: (" + boss_location + ")");
        ApDebugLog.Instance.DisplayMessage("Boss Defeated");

        // TODO add handling for numeral boss locations
        if (currentShard == Convert.ToInt32(FactSystem.GetFact(new Fact("APShardGoal")))){
            ApDebugLog.Instance.DisplayMessage("Game Complete");
            connection.CompleteGame();
        } else
        {
            connection.SendLocation(boss_location);
        }
        orig();
    }

    /// <summary>
    /// Hook into when the end Boss is deafeted. Used release the world
    /// </summary>
    private static void StaticEndBossHook(On.GM_API.orig_OnEndBossWin orig)
    {
        if (connection == null)
        {
            ApDebugLog.Instance.DisplayMessage("AP On End Boss Win the connection is null");
            return;
        }
        if (Convert.ToInt32(FactSystem.GetFact(new Fact("APShardGoal"))) == 10)
        {
            ApDebugLog.Instance.DisplayMessage("Game Complete");
            connection.CompleteGame();
        } else
        {
            connection.SendLocation("Shard 10 Boss");
        }
        orig();
    }

    private static void StaticMetaProgressionUnlockOverloadHook(On.Landfall.Haste.AbilityUnlockScreen.orig_Unlock orig, AbilityUnlockScreen self)
    {
        // Get the type of the AbilityUnlockScreen
        Type unlockScreenType = typeof(AbilityUnlockScreen);

        // Use reflection to get the private field "Character"
        FieldInfo characterField = unlockScreenType.GetField("Character", BindingFlags.NonPublic | BindingFlags.Instance);

        if (connection == null)
        {
            ApDebugLog.Instance.DisplayMessage("AP On Progession Unlock the connection is null");
            return;
        }

        // Get the value of the "Character" field from the unlockScreen instance
        var interactionCharacter = characterField.GetValue(self) as InteractionCharacter;
        if (interactionCharacter == null)
        {
            ApDebugLog.Instance.DisplayMessage("The 'Character' field is null or not of type InteractionCharacter.");
            return;
        }
        var ability_name = Enum.GetName(typeof(AbilityKind), interactionCharacter.Ability);
        var ability_location = $"Ability {ability_name}";
        UnityMainThreadDispatcher.Instance().log("AP sending Ability: (" + ability_location + ")");
        ApDebugLog.Instance.DisplayMessage($"Ability Got: {ability_name}");
        var cost = MetaProgression.Instance.GetEntry(interactionCharacter.Ability).cost;
        MetaProgression.AddResource(-cost);
        // TODO add handling for numeral boss locations

        connection.SendLocation(ability_location);
        // orig();
        // Close the window

        self.gameObject.SetActive(value: false);
    }

    private static void StaticInteractableCharacterStartHook(On.InteractableCharacter.orig_Start orig, InteractableCharacter self)
    {
        Type interactableCharacterType = self.GetType();
        FieldInfo initField = interactableCharacterType.GetField("init", BindingFlags.NonPublic | BindingFlags.Instance);

        var ability = self.character.Ability;
        var abilityName = Enum.GetName(typeof(AbilityKind), ability);

        if (initField.GetValue(self) is not bool init)
        {
            ApDebugLog.Instance.DisplayMessage("The 'Character' field is null or not of type InteractionCharacter.");
            return;
        }

        if (self.character.name == "Captain")
        {
            self.State.SwitchState<InteractableCharacter.HasAbilityUnlockState>();

        }

        if (!init)
        {
            if (self.interactionToPlay != null)
            {
                self.State.SwitchState<InteractableCharacter.HasInteractionState>();
            }
            // If the character has an unlock interaction and The ability location is not unlocked
            else if (self.unlockInteraction != null && !connection!.IsLocationChecked($"Ability {abilityName}"))
            {
                self.State.SwitchState<InteractableCharacter.HasAbilityUnlockState>();
            }
        }
    }

    private static void StaticInteractableCharacterInteractHook(On.InteractableCharacter.orig_Interact orig, InteractableCharacter self)
    {
        var ability = self.character.Ability;
        var abilityName = Enum.GetName(typeof(AbilityKind), ability);

        if (self.character.HasAbilityUnlock && connection!= null)
        {
            connection.SendHintedLocation($"Ability {abilityName}");
        }
        orig(self);
    }
}



// Settings For AP server connection
[HasteSetting]
public class ApEnabledSetting : BoolSetting, IExposedSetting
{
    public override LocalizedString OffString => new UnlocalizedString("AP off");

    public override LocalizedString OnString => new UnlocalizedString("AP on");

    public override void ApplyValue() => UnityMainThreadDispatcher.Instance().log($"AP Toggled AP to {Value}");
    protected override bool GetDefaultValue() => false;
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Toggle");
    public string GetCategory() => "AP";
}

[HasteSetting]
public class ApDebugEnabledSetting : BoolSetting, IExposedSetting
{
    public override LocalizedString OffString => new UnlocalizedString("Debug messages off");

    public override LocalizedString OnString => new UnlocalizedString("Debug messages on");

    public override void ApplyValue() => FactSystem.SetFact(new Fact("APDebugLogEnabled"), Value ? 1f : 0f);
    protected override bool GetDefaultValue() => false;
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Debug Toggle");
    public string GetCategory() => "AP";
}

[HasteSetting]
public class ApLogFilter : EnumSetting<APFilterMode>, IExposedSetting
{
    public override void ApplyValue() => FactSystem.SetFact(new Fact("APMessageFilter"), (float)Value);
    protected override APFilterMode GetDefaultValue() => APFilterMode.None;
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Text Client Filter");
    public string GetCategory() => "AP";

    public override List<LocalizedString> GetLocalizedChoices() => [new UnlocalizedString("All Messages"), new UnlocalizedString("Only Messages About Player"), new UnlocalizedString("Player Messages + Chat")];

}

[HasteSetting]
public class ApServerNameSetting : StringSetting, IExposedSetting
{
    public override void ApplyValue() => UnityMainThreadDispatcher.Instance().log($"New AP hostname {Value}");
    protected override string GetDefaultValue() => "localhost";
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Server name");
    public string GetCategory() => "AP";
}

[HasteSetting]
public class ApServerPortSetting : IntSetting, IExposedSetting
{
    public override void ApplyValue() => UnityMainThreadDispatcher.Instance().log($"New AP hostport {Value}");
    protected override int GetDefaultValue() => 38281;
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Server Port");
    public string GetCategory() => "AP";
}

[HasteSetting]
public class ApUsernameSetting : StringSetting, IExposedSetting
{
    public override void ApplyValue() => UnityMainThreadDispatcher.Instance().log($"New AP username {Value}");
    protected override string GetDefaultValue() => "Player1";
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Username");
    public string GetCategory() => "AP";
}
[HasteSetting]
public class ApPasswordSetting : StringSetting, IExposedSetting
{
    public override void ApplyValue() => UnityMainThreadDispatcher.Instance().log($"New AP Password {Value}");
    protected override string GetDefaultValue() => "";
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Password");
    public string GetCategory() => "AP";
}