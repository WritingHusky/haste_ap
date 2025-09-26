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
        ApDebugLog.Instance.BuildFont();

        UnityMainThreadDispatcher.Instance().log("AP Log created");
        ApDebugLog.Instance.DisplayMessage("Archiplego Installed v0.2.1", isDebug: false);

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
        for (int i = 1; i <= 10; i++)
        {
            SetFragmentLimits("Shard"+i);
        }
        SetFragmentLimits("Global");

        FactSystem.SubscribeToFact(new Fact("current_unbeaten_shard"), (value) =>
        {
            ApDebugLog.Instance.DisplayMessage($"unbeaten shard update to (zero index){value}");
            if (connection == null)
                return;

            var shard_count = connection.GetItemCount("Progressive Shard");
            // Never allow the value to be not what I want it to be
            if (FactSystem.GetFact(new Fact("APShardUnlockOrder")) == 1f)
            {
                var newvalue = Math.Min(shard_count, (int)FactSystem.GetFact(new Fact("APBossDefeated")));
                // if unlock-order is Boss-locked, then only uplock up until the lowest of either bossbeated or shards
                if (value != newvalue) FactSystem.SetFact(new Fact("current_unbeaten_shard"), newvalue);
            } else
            {
                if (value != shard_count) FactSystem.SetFact(new Fact("current_unbeaten_shard"), shard_count);
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

        On.PlayerCharacter.RestartPlayer_Launch_Transform_float += StaticSetSpeed;
        On.PlayerCharacter.RestartPlayer_Still_Transform += StaticSetSpeed;

        UnityMainThreadDispatcher.Instance().log("AP Hooks Complete");

        // Once the player starts in game do the loading as somethings are not setup yet
        On.GM_API.OnSpawnedInHub += StaticLoadHubHook;

        FactSystem.SetFact(new Fact("APFirstLoad"), 1f);
        UnityMainThreadDispatcher.Instance().log("AP Transitioning to original actions");
        ApDebugLog.Instance.DisplayMessage("Loading normally now");
        orig();
    }

    private static void StaticSetSpeed(On.PlayerCharacter.orig_RestartPlayer_Still_Transform orig, PlayerCharacter self, Transform spawnPoint)
    {
        if (FactSystem.GetFact(new Fact("APSpeedUpgrades")) == 1)
        {
            SecretSetSpeed(self);
        }
        orig(self, spawnPoint);
    }

    private static void StaticSetSpeed(On.PlayerCharacter.orig_RestartPlayer_Launch_Transform_float orig, PlayerCharacter self, Transform spawnPoint, float minVel)
    {
        if (FactSystem.GetFact(new Fact("APSpeedUpgrades")) == 1)
        {
            SecretSetSpeed(self);
        }
        orig(self, spawnPoint, minVel);
    }

    private static void SecretSetSpeed(PlayerCharacter player)
    {
        var APspeed = FactSystem.GetFact(new Fact("APSpeedUpgradesCollected"));
        float calcSpeed = 0.6f + (APspeed * 0.1f);
        ApDebugLog.Instance.DisplayMessage($"Setting speed to {calcSpeed}");
        player.player.stats.runSpeed.multiplier = calcSpeed;
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
        SetHubState();

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
        On.PlayerCharacter.RestartPlayer_Launch_Transform_float -= StaticSetSpeed;
        On.PlayerCharacter.RestartPlayer_Still_Transform -= StaticSetSpeed;
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
            string? locationName; // location to send
            string? FragKeyword; // suffix to put on everything
            if (FactSystem.GetFact(new Fact("APFragmentsanity")) == 1f)
            {
                var currentShard = RunHandler.RunData.shardID + 1;
                locationName = $"Shard {currentShard} Fragment Clear ";
                FragKeyword = "Shard" + currentShard;
            } else
            {
                locationName = "Global Fragment Clear ";
                FragKeyword = "Global";
            }

            if (FactSystem.GetFact(new Fact("APFragmentsanityLocation" + FragKeyword)) < FactSystem.GetFact(new Fact("APFragmentsanityQuantity")))
            {
                FactSystem.AddToFact(new Fact("APFragmentsanity" + FragKeyword), 1f);
                ApDebugLog.Instance.DisplayMessage($"Completed Fragment. Progress: {FactSystem.GetFact(new Fact("APFragmentsanity" + FragKeyword))}/{FactSystem.GetFact(new Fact("APFragmentLimit" + FragKeyword))} for Clear {Convert.ToInt32(FactSystem.GetFact(new Fact("APFragmentsanityLocation" + FragKeyword))) + 1}", isDebug: false);
                if (FactSystem.GetFact(new Fact("APFragmentsanity" + FragKeyword)) == FactSystem.GetFact(new Fact("APFragmentLimit" + FragKeyword)))
                {
                    connection.SendLocation(locationName + (Convert.ToInt32(FactSystem.GetFact(new Fact("APFragmentsanityLocation" + FragKeyword))) + 1).ToString("D2"));
                    FactSystem.AddToFact(new Fact("APFragmentsanityLocation" + FragKeyword), 1f);
                    FactSystem.SetFact(new Fact("APFragmentsanity" + FragKeyword), 0f);
                    SetFragmentLimits(FragKeyword);
                }
            }

        }
        orig(player);
    }

    public static void SetFragmentLimits(string keyword)
    {
        if (FactSystem.GetFact(new Fact("APFragmentsanity")) == 2f)
        {
            // if MODE H-TRI
            // LIMIT = floor(FRAGMENTLOCATION / 2)
            FactSystem.SetFact(new Fact($"APFragmentLimit{keyword}"), Math.Max((float)Math.Floor((FactSystem.GetFact(new Fact($"APFragmentsanityLocation{keyword}")) + 1) / 2), 1f));
        }
        else if (FactSystem.GetFact(new Fact("APFragmentsanity")) == 3f)
        {
            // if MODE BALANCED HALF-TRI
            // LIMIT = min( floor(FRAGMENTLOCATION / 2), 10
            FactSystem.SetFact(new Fact($"APFragmentLimit{keyword}"), Math.Max(Math.Min((float)Math.Floor((FactSystem.GetFact(new Fact($"APFragmentsanityLocation{keyword}")) + 1) / 2), 10f), 1f));
        }
        else if (FactSystem.GetFact(new Fact("APFragmentsanity")) == 4f)
        {
            // if MODE TRI
            // LIMIT = FRAGMENTLOCATION
            FactSystem.SetFact(new Fact($"APFragmentLimit{keyword}"), FactSystem.GetFact(new Fact($"APFragmentsanityLocation{keyword}")) + 1);
        }
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
            item_location = "Shard " + currentShard + " Shop Item " + (Convert.ToInt32(FactSystem.GetFact(new Fact("APShopsanityShard" + currentShard))) + 1).ToString("D2");
            FactSystem.AddToFact(new Fact("APShopsanityShard" + currentShard), 1f);
            canSend = FactSystem.GetFact(new Fact("APShopsanityShard" + currentShard)) <= FactSystem.GetFact(new Fact("APShopsanityQuantity"));
        } else if (FactSystem.GetFact(new Fact("APShopsanity")) == 2f)
        {
            item_location = "Global Shop Item " + Convert.ToInt32(FactSystem.GetFact(new Fact("APShopsanityGlobal")) + 1).ToString("D3");
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
        FactSystem.SetFact(new Fact("APBossDefeated"), Math.Max(FactSystem.GetFact(new Fact("APBossDefeated")), currentShard));

        var boss_location = "Shard " + currentShard + " Boss";
        UnityMainThreadDispatcher.Instance().log("AP sending Boss: (" + boss_location + ")");
        ApDebugLog.Instance.DisplayMessage("Boss Defeated");

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
        var ability_location = $"{GetAbilityName(ability_name)} Purchase";
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
            else if (self.unlockInteraction != null && !connection!.IsLocationChecked($"{GetAbilityName(abilityName)} Purchase"))
            {
                // fix for stupid case where Daro never leaves the hub regardless of her corresponding fact
                if ((self.character.name == "Sage" && FactSystem.GetFact(new Fact("APSageInHub")) == 1f) || self.character.name != "Sage")
                {
                    self.State.SwitchState<InteractableCharacter.HasAbilityUnlockState>();
                } 
            }
        }
    }

    private static void StaticInteractableCharacterInteractHook(On.InteractableCharacter.orig_Interact orig, InteractableCharacter self)
    {
        var ability = self.character.Ability;
        var abilityName = Enum.GetName(typeof(AbilityKind), ability);

        if (self.character.HasAbilityUnlock && connection!= null && FactSystem.GetFact(new Fact("in_run")) == 0)
        {
            if ((self.character.name == "Sage" && FactSystem.GetFact(new Fact("APSageInHub")) == 1f) || self.character.name != "Sage")
            {
                connection.SendHintedLocation($"{GetAbilityName(abilityName)} Purchase");
            }
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

    public override List<LocalizedString> GetLocalizedChoices() => [new UnlocalizedString("All Messages"), new UnlocalizedString("Only Messages About Player"), new UnlocalizedString("Messages About Player + Chat")];

}

[HasteSetting]
public class ApLogXOffset : IntSetting, IExposedSetting
{
    public override void ApplyValue() => ApDebugLog.Instance.xBaseOffset = Value;
    protected override int GetDefaultValue() => -650;
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Text Client X Offset");
    public string GetCategory() => "AP";
}

[HasteSetting]
public class ApLogYOffset : IntSetting, IExposedSetting
{
    public override void ApplyValue() => ApDebugLog.Instance.yBaseOffset = Value;
    protected override int GetDefaultValue() => 150;
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Text Client Y Offset");
    public string GetCategory() => "AP";
}

[HasteSetting]
public class ApLogFontSize : IntSetting, IExposedSetting
{
    public override void ApplyValue()
    {
        ApDebugLog.Instance.fontSize = Value;
    }
    protected override int GetDefaultValue() => 16;
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Text Client Font Size");
    public string GetCategory() => "AP";
}

[HasteSetting]
public class ApLogLineSpacing : IntSetting, IExposedSetting
{
    public override void ApplyValue() => ApDebugLog.Instance.lineSpacing = Value;
    protected override int GetDefaultValue() => 20;
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Text Client Line Spacing");
    public string GetCategory() => "AP";
}

[HasteSetting]
public class ApLogDirection : BoolSetting, IExposedSetting
{
    public override LocalizedString OffString => new UnlocalizedString("Messages go up");
    public override LocalizedString OnString => new UnlocalizedString("Messages go down");
    public override void ApplyValue() => ApDebugLog.Instance.messagesGoDown = Value;
    protected override bool GetDefaultValue() => true;
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Text Client Message Direction");
    public string GetCategory() => "AP";
}

[HasteSetting]
public class ApLogTestMessage : ButtonSetting, IExposedSetting
{
    public LocalizedString GetDisplayName() => new UnlocalizedString("AP Text Client Example Message");
    public string GetCategory() => "AP";

    public override void OnClicked(ISettingHandler settingHandler)
    {
        ApDebugLog.Instance.DisplayMessage("This is a test message that is really long so that way you can make sure the text appears on screen correctly.", isDebug: false);
    }

    public override string GetButtonText()
    {
        return "Show Example Message";
    }
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