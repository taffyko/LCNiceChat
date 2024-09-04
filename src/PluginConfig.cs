using System;
using System.Drawing;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UIElements;

namespace NiceChat;

public partial class Plugin {
    public static float DefaultFontSize { get; private set; }
    public static bool EnlargeChatWindow { get; private set; }
    public static int CharacterLimit { get; private set; }
    public static float MessageRange { get; private set; }
    public static bool HearDeadPlayers { get; private set; }
    public static bool EnableTimestamps { get; private set; }
    public static bool ShowScrollbar { get; private set; }
    public static float FadeOpacity { get; private set; }
    public static float FadeTimeAfterMessage { get; private set; }
    public static float FadeTimeAfterOwnMessage { get; private set; }
    public static float FadeTimeAfterUnfocused { get; private set; }
    public static bool GuiFadeFix { get; private set; }
    public static bool SpectatorChatHideOtherHudElements { get; private set; }
    public static UnityEngine.Color InputTextColor { get; private set; }

    void ConfigInit() {
        Config.Bind<string>("README", "README", "", """
All config values are text-based, as a workaround to make it possible for default values to change in future updates.

See https://github.com/taffyko/LCNiceChat/issues/3 for more information.

If you enter an invalid value, it will change back to "default" when the game starts.
""");
        ColorUtility.TryParseHtmlString("#585ed1d4", out var vanillaInputTextColor);
        ConfEntry("Chat", nameof(DefaultFontSize), 11.0f, "Font size.", float.TryParse, vanillaValue: 13.0f);
        ConfEntry("Chat", nameof(EnlargeChatWindow), true, "Increases the size of the chat area.", bool.TryParse);
        ConfEntry("Chat", nameof(CharacterLimit), 1000, "Maximum character limit for messages in your lobby.", int.TryParse, hostControlled: true);
        ConfEntry("Chat", nameof(MessageRange), 25f, "Maximum distance from which messages between living players can be heard without a walkie-talkie.", float.TryParse, hostControlled: true, vanillaValue: 25f);
        ConfEntry("Chat", nameof(HearDeadPlayers), false, "When enabled, allows living players to hear messages from dead players.", bool.TryParse, hostControlled: true);
        ConfEntry("Chat", nameof(EnableTimestamps), true, "Adds timestamps to messages whenever the clock is visible.", bool.TryParse);
        ConfEntry("Chat", nameof(ShowScrollbar), true, "If false, the scrollbar is permanently hidden even when the chat input is focused.", bool.TryParse);
        ConfEntry("Chat", nameof(InputTextColor), UnityEngine.Color.white, "Default color of text in the input field", ColorUtility.TryParseHtmlString, vanillaValue: vanillaInputTextColor);
        ConfEntry("Fade Behaviour", nameof(FadeOpacity), 0.0f, "The opacity of the chat when it fades from inactivity. 0.0 makes the chat fade away completely.", float.TryParse, vanillaValue: 0.2f);
        ConfEntry("Fade Behaviour", nameof(FadeTimeAfterMessage), 4.0f, "The amount of seconds before the chat fades out after a message is sent by another player.", float.TryParse, vanillaValue: 4.0f);
        ConfEntry("Fade Behaviour", nameof(FadeTimeAfterOwnMessage), 2.0f, "The amount of seconds before the chat fades out after a message is sent by you.", float.TryParse, vanillaValue: 2.0f);
        ConfEntry("Fade Behaviour", nameof(FadeTimeAfterUnfocused), 1.0f, "The amount of seconds before the chat fades out after the chat input is unfocused.", float.TryParse, vanillaValue: 1.0f);
        ConfEntry("Compatibility", nameof(GuiFadeFix), true, "Workaround to prevent other UI elements (like the indicator from LethalLoudnessMeter) from also fading out when the chat fades", bool.TryParse);
        ConfEntry("Compatibility", nameof(SpectatorChatHideOtherHudElements), true, "For spectator chat, ensures that only the chat window is shown, and that irrelevant HUD elements (inventory/etc.) are hidden.", bool.TryParse);
    }

    delegate bool ParseConfigValue<T>(string input, out T output);
    private static bool NoopParse(string input, out string output) {
        output = input;
        return true;
    }
    private void ConfEntry<T>(string category, string name, T defaultValue, string description, ParseConfigValue<T> tryParse, bool hostControlled = false) {
        ConfEntryInternal(category, name, defaultValue, description, tryParse, hostControlled);
    }
    private void ConfEntry<T>(string category, string name, T defaultValue, string description, ParseConfigValue<T> tryParse, T vanillaValue, bool hostControlled = false) {
        ConfEntryInternal(category, name, defaultValue, description, tryParse, hostControlled, ConfEntryToString(vanillaValue));
    }
    private void ConfEntryInternal<T>(string category, string name, T defaultValue, string description, ParseConfigValue<T> tryParse, bool hostControlled = false, string? vanillaValueText = null) {
        var property = typeof(Plugin).GetProperty(name);
        // Build description
        string desc = $"[default: {ConfEntryToString(defaultValue)}]\n{description}";
        desc += hostControlled ? "\n(This setting is overridden by the lobby host)" : "\n(This setting's effect applies to you only)";
        if (vanillaValueText != null) {
            desc += $"\n(The original value of this setting in the base-game is {vanillaValueText})";
        }
        var config = Config.Bind<string>(category, name, "default", desc);
        if (string.IsNullOrEmpty(config.Value)) { config.Value = "default"; }
        // Load value
        bool validCustomValue = tryParse(config.Value, out T value) && config.Value != "default";
        property.SetValue(null, validCustomValue ? value : defaultValue);
        if (!validCustomValue) { config.Value = "default"; }
        // Handle changes in value during the game
        EventHandler loadConfig = (object? sender, EventArgs? e) => {
            bool validCustomValue = tryParse(config.Value, out T value) && config.Value != "default";
            property.SetValue(null, validCustomValue ? value : defaultValue);
        };
        config.SettingChanged += loadConfig;

        cleanupActions.Add(() => {
            config.SettingChanged -= loadConfig;
            property.SetValue(null, defaultValue);
        });
    }
    private string ConfEntryToString(object? value) {
        if (value == null) { return "null"; }
        var type = value.GetType();
        if (type == typeof(float)) {
            return string.Format("{0:0.0#####}", (float)value);
        } else if (type == typeof(UnityEngine.Color)) {
            return $"#{ColorUtility.ToHtmlStringRGBA((UnityEngine.Color)value)}";
        } else {
            return value.ToString();
        }
    }
}