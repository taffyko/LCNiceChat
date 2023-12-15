using BepInEx;
using HarmonyLib;
using GameNetcodeStuff;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.InputSystem;
using System.Reflection.Emit;
using Unity.Netcode;
using Unity.Collections;

namespace NiceChat;

[BepInPlugin(modGUID, modName, modVersion)]
public class Plugin : BaseUnityPlugin {
    public const string modGUID = "taffyko.NiceChat";
    public const string modName = "NiceChat";
    public const string modVersion = "1.0.1";
    
    private readonly Harmony harmony = new Harmony(modGUID);
    public static ManualLogSource? log;

    private static float? defaultFontSize;
    public static float DefaultFontSize => defaultFontSize ?? 11f;
    private static bool? enlargeChatWindow;
    public static bool EnlargeChatWindow => enlargeChatWindow ?? true;
    private static int? characterLimit;
    public static int CharacterLimit => characterLimit ?? 1000;
    private static bool? enableTimestamps;
    public static bool EnableTimestamps => enableTimestamps ?? true;

    private void Awake() {
        if (float.TryParse(
            Config.Bind<string?>("Chat", "DefaultFontSize", null, "(default: 11) The base game's font size is 13").Value,
            out var _defaultFontSize
        )) { defaultFontSize = _defaultFontSize; };
        if (bool.TryParse(
            Config.Bind<string?>("Chat", "EnlargeChatWindow", null, "(default: true) Increases the size of the chat area").Value,
            out bool _enlargeChatWindow
        )) { enlargeChatWindow = _enlargeChatWindow; };
        if (int.TryParse(
            Config.Bind<string?>("Chat", "CharacterLimit", null, "(default: 1000) Maximum character limit for messages in your lobby (Only applies if you are the host)").Value,
            out var _characterLimit
        )) { characterLimit = _characterLimit; };
        if (bool.TryParse(
            Config.Bind<string?>("Chat", "EnableTimestamps", null, "(default: true) Adds timestamps to messages whenever the clock is visible").Value,
            out var _enableTimestamps
        )) { enableTimestamps = _enableTimestamps; };
        log = BepInEx.Logging.Logger.CreateLogSource(modName);
        log.LogInfo($"Loading {modGUID}");

        // Plugin startup logic
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }

    private void OnDestroy() {
        #if DEBUG
        log?.LogInfo($"Unloading {modGUID}");
        var player = StartOfRound.Instance?.localPlayerController;
        if (NetworkManager.Singleton != null && player != null) {
            Patches.fields.TryGetValue(player, out var f);
            if (f != null && f.scrollContainerRect != null && f.chatTextBgRect != null && f.chatTextRect != null) {
                f.chatTextRect.SetParent(f.chatTextBgRect.parent, false);
                Destroy(f.scrollContainerRect.gameObject);
                Destroy(f.chatTextBgRect.Find("ScrollMask")?.gameObject);
            }
        }
        harmony?.UnpatchSelf();
        #endif
    }
}


[HarmonyPatch]
internal class Patches {
    public class CustomFields {
        public InputAction? shiftAction = null;
        public InputAction? scrollAction = null;
        public TMPro.TMP_Text? chatText = null;
        public TMPro.TMP_InputField? chatTextField = null;
        public float previousChatTextHeight = 0f;
        public float previousScrollPosition = 0f;
        public RectTransform? chatTextBgRect = null;
        public RectTransform? chatTextFieldRect = null;
        public RectTransform? chatTextRect = null;
        public RectTransform? scrollContainerRect = null;
        public ScrollRect? scroll = null;
        public Scrollbar? scrollbar = null;
        public bool? serverHasMod = null;
        public int? serverCharacterLimit = null;
        public bool handlerRegistered = false;
    }

    // Extra player instance fields
    public static Dictionary<PlayerControllerB, CustomFields> fields = new();

    // Custom action used to check if the player is holding either shift key
    [HarmonyPatch(typeof(PlayerControllerB), "Awake")]
    [HarmonyPostfix]
    private static void Player_Awake(PlayerControllerB __instance) {
        reload(__instance);
    }

    [HarmonyPatch(typeof(PlayerControllerB), "Update")]
    [HarmonyPostfix]
    private static void Player_Update(PlayerControllerB __instance) {
        reload(__instance);
        if (NetworkManager.Singleton.LocalClientId != __instance.playerClientId) { return; }
        var f = fields[__instance];

        if (f.chatTextField != null) {
            if (f.serverHasMod == true) {
                f.chatTextField.characterLimit = f.serverCharacterLimit ?? 500;
            } else {
                f.chatTextField.characterLimit = 49;
            }
            f.chatTextField.lineLimit = 0;

            // Enable "enter-for-linebreak" mode while shift is held
            if (f.shiftAction != null && f.shiftAction.IsPressed()) {
                f.chatTextField.lineType = TMPro.TMP_InputField.LineType.MultiLineNewline;
            }
            else {
                f.chatTextField.lineType = TMPro.TMP_InputField.LineType.MultiLineSubmit;
            }
        }

        if (f.chatText != null) {
            f.chatText.fontSize = Plugin.DefaultFontSize;
            // If text history approaches the maximum display limit, trim old chat history
            if (f.chatText.text.Length > f.chatText.maxVisibleCharacters) {
                var oneTenthOfLimit = f.chatText.maxVisibleCharacters/10;
                while (f.chatText.text.Length > (f.chatText.maxVisibleCharacters - oneTenthOfLimit)) {
                    f.chatText.text = f.chatText.text.Remove(0, HUDManager.Instance.ChatMessageHistory[0].Length);
                    HUDManager.Instance.ChatMessageHistory.RemoveAt(0);
                }
            }
        }

        if (f.chatText != null && f.chatTextRect != null && f.chatTextBgRect != null && f.chatTextFieldRect != null) {
            if (Plugin.EnlargeChatWindow) {
                f.chatTextBgRect.anchorMin = new Vector2(0.35f, 0.5f);
                f.chatTextBgRect.anchorMax = new Vector2(0.80f, 0.5f);
                f.chatTextFieldRect.anchorMin = new Vector2(0.30f, 0.5f);
                f.chatTextFieldRect.anchorMax = new Vector2(0.80f, 0.5f);
            } else {
                f.chatTextBgRect.anchorMin = new Vector2(0.5f, 0.5f);
                f.chatTextBgRect.anchorMax = new Vector2(0.5f, 0.5f);
                f.chatTextFieldRect.anchorMin = new Vector2(0.5f, 0.5f);
                f.chatTextFieldRect.anchorMax = new Vector2(0.5f, 0.5f);
            }

            // Scroll container setup
            if (f.scrollContainerRect == null) {
                f.scrollContainerRect = f.chatTextBgRect.parent.Find("ChatScrollContainer")?.GetComponent<RectTransform>();
                if (f.scrollContainerRect == null) {
                    // Create scroll container, parent it to the chat background image
                    var scrollContainer = new GameObject { name = "ChatScrollContainer" };
                    f.scrollContainerRect = scrollContainer.AddComponent<RectTransform>();
                    f.scrollContainerRect.anchorMin = Vector2.zero;
                    f.scrollContainerRect.anchorMax = Vector2.one;
                    f.scrollContainerRect.offsetMin = new Vector2(0f, 40f); // bottom margin to keep the chat from overlapping input area
                    f.scrollContainerRect.offsetMax = new Vector2(0f, -3f); // top margin to make text clip right before it would overlap the border graphic
                    f.scrollContainerRect.SetParent(f.chatTextBgRect, false);

                    // Create mask rect for scrolling content
                    var scrollMask = new GameObject { name = "ScrollMask" };
                    var scrollMaskRect = scrollMask.AddComponent<RectTransform>();
                    var scrollMaskImage = scrollMask.AddComponent<Image>(); // Implicitly adds CanvasRenderer component
                    scrollMaskImage.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0,0,1,1), new Vector2(0, 0));
                    var scrollMaskMask = scrollMask.AddComponent<Mask>();
                    scrollMaskMask.showMaskGraphic = false;
                    // Parent mask to scroll container
                    scrollMaskRect.SetParent(f.scrollContainerRect, false);
                    scrollMaskRect.anchorMin = Vector2.zero;
                    scrollMaskRect.anchorMax = Vector2.one;
                    scrollMaskRect.offsetMin = Vector2.zero;
                    scrollMaskRect.offsetMax = Vector2.zero;

                    // Parent chatText to mask
                    f.chatTextRect.SetParent(scrollMaskRect, false);

                    f.chatText.gameObject.TryGetComponent<LayoutElement>(out var layoutElement);
                    Object.Destroy(layoutElement);
                    layoutElement = f.chatText.gameObject.AddComponent<LayoutElement>();
                    layoutElement.minHeight = scrollMaskRect.rect.height;

                    f.chatText.alignment = TMPro.TextAlignmentOptions.BottomLeft;

                    f.chatText.gameObject.TryGetComponent<ContentSizeFitter>(out var fitter);
                    Object.Destroy(fitter);
                    var containerFitter = f.chatText.gameObject.AddComponent<ContentSizeFitter>();
                    containerFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                    containerFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

                    // Add `ScrollRect` component and configure
                    f.scroll = scrollContainer.AddComponent<ScrollRect>();
                    f.scroll.content = f.chatTextRect;
                    f.scroll.viewport = scrollMaskRect;
                    f.scroll.vertical = true;
                    f.scroll.horizontal = false;
                    f.scroll.verticalNormalizedPosition = 0f;

                    // Scrollbar
                    var scrollBar = new GameObject() { name = "ScrollBar" };
                    var scrollBarRect = scrollBar.AddComponent<RectTransform>();
                    scrollBarRect.SetParent(f.scrollContainerRect, false);
                    scrollBarRect.anchorMin = new Vector2(1, 0);
                    scrollBarRect.anchorMax = Vector2.one;
                    // Scrollbar horizontal positioning and width
                    scrollBarRect.offsetMin = new Vector2(-7, 0);
                    scrollBarRect.offsetMax = new Vector2(-5, -5);
                    f.scrollbar = scrollBar.AddComponent<Scrollbar>();
                    var scrollBarImage = scrollBar.AddComponent<Image>();
                    scrollBarImage.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0,0,1,1), new Vector2(0, 0));
                    scrollBarImage.color = new Color(88f/255f, 94f/255f, 209f/255f, 50f/255f);

                    var scrollHandle = new GameObject() { name = "ScrollBarHandle" };
                    var scrollHandleRect = scrollHandle.AddComponent<RectTransform>();
                    scrollHandleRect.SetParent(scrollBarRect, false);
                    scrollHandleRect.offsetMin = Vector2.zero;
                    scrollHandleRect.offsetMax = Vector2.zero;
                    var scrollHandleImage = scrollHandle.AddComponent<Image>();
                    scrollHandleImage.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0,0,1,1), new Vector2(0, 0));
                    scrollHandleImage.color = Color.white;

                    scrollHandleImage.color = new Color(88f/255f, 94f/255f, 209f/255f, 112f/255f);

                    f.scrollbar.handleRect = scrollHandleRect;
                    f.scrollbar.direction = Scrollbar.Direction.BottomToTop;
                    f.scroll.verticalScrollbar = f.scrollbar;
                }
            }


            f.chatText.gameObject.TryGetComponent<LayoutElement>(out var chatLayoutElement);
            if (chatLayoutElement != null) {
                var parentRect = f.chatTextRect.parent.GetComponent<RectTransform>();
                chatLayoutElement.minHeight = parentRect.rect.height;
                // Horizontal alignment of chatText
                f.chatTextRect.sizeDelta = new Vector2(parentRect.rect.width - 12f, f.chatTextRect.sizeDelta.y);
                f.chatTextRect.localPosition = new Vector2(0f, f.chatTextRect.localPosition.y);
            }

            if (f.chatTextField != null) {
                // Allow scrolling when focused
                if (f.chatTextField.isFocused && f.scrollAction != null && f.scroll != null) {
                    float deltaScroll = f.scrollAction.ReadValue<Vector2>().y/120f; // one Windows scroll increment = 120
                    deltaScroll /= f.chatTextRect.rect.height; // 1 pixel per increment
                    deltaScroll *= 30f; // 30 pixels per increment
                    f.scroll.verticalNormalizedPosition += deltaScroll;
                }

                if (f.scroll != null && f.scrollbar != null) {
                    // Hide scrollbar handle when it fills the whole scrollbar
                    if (f.chatText.preferredHeight < f.scrollContainerRect.rect.height) {
                        f.scrollbar.handleRect.GetComponent<Image>().color = new Color(0,0,0,0);
                    } else {
                        f.scrollbar.handleRect.GetComponent<Image>().color = new Color(88f/255f, 94f/255f, 209f/255f, 112f/255f);
                    }
                    if (f.chatTextField.isFocused) {
                        // When chatText height changes (message added) while the input is focused...
                        if (f.chatTextRect.rect.height != f.previousChatTextHeight) {
                            if (f.chatTextField.isFocused && f.scroll.verticalNormalizedPosition >= (100/f.chatTextRect.rect.height)) {
                                // If scrolled up by at least 100px, restore the scroll position to prevent the user's place in history from being lost
                                var scrollPxFromTop  = (1 - f.previousScrollPosition)*f.previousChatTextHeight;
                                var scrollPx = f.chatTextRect.rect.height - scrollPxFromTop;
                                f.scroll.verticalNormalizedPosition = scrollPx/f.chatTextRect.rect.height;
                            } else {
                                // Otherwise, jump to the most recent message
                                f.scroll.verticalNormalizedPosition = 0f;
                            }
                            f.previousChatTextHeight = f.chatTextRect.rect.height;
                        }
                    } else {
                        // Always remain scrolled to latest message when the chat input is unfocused
                        f.scroll.verticalNormalizedPosition = 0f;
                    }
                    f.previousScrollPosition = f.scroll.verticalNormalizedPosition;
                }
            }
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB), "OnDestroy")]
    [HarmonyPostfix]
    private static void OnDestroy(PlayerControllerB __instance) {
        NetworkManager.Singleton?.CustomMessagingManager?.UnregisterNamedMessageHandler(msgModVersion);
        NetworkManager.Singleton?.CustomMessagingManager?.UnregisterNamedMessageHandler(msgCharacterLimit);
        fields.Remove(__instance);
    }

    public const string msgModVersion = Plugin.modGUID;
    public const string msgCharacterLimit = $"{Plugin.modGUID}.characterLimit";

    // Sets up references and handlers as needed, so that the mod can be hot-reloaded at any time
    private static void reload(PlayerControllerB __instance) {
        if (!fields.ContainsKey(__instance)) { fields[__instance] = new CustomFields(); }
        var f = fields[__instance];

        if (NetworkManager.Singleton?.LocalClientId != __instance.playerClientId) { return; }

        if (__instance.NetworkManager.IsConnectedClient || __instance.NetworkManager.IsServer) {
            var msgManager = __instance.NetworkManager.CustomMessagingManager;
            if (!f.handlerRegistered) {
                msgManager.RegisterNamedMessageHandler(msgModVersion, (ulong clientId, FastBufferReader reader) => {
                    if (__instance.NetworkManager.IsServer) {
                        // Modded server responds to version inquiries from clients
                        var writer = new FastBufferWriter(100, Allocator.Temp);
                        writer.WriteValue(Plugin.modVersion);
                        msgManager.SendNamedMessage(msgModVersion, clientId, writer);
                    } else {
                        // If the server responds, the client knows it has the mod
                        f.serverHasMod = true;
                    }
                });
                msgManager.RegisterNamedMessageHandler(msgCharacterLimit, (ulong clientId, FastBufferReader reader) => {
                    if (__instance.NetworkManager.IsServer) {
                        // Modded server responds to character limit inquiries from clients
                        var writer = new FastBufferWriter(100, Allocator.Temp);
                        writer.WriteValue(Plugin.CharacterLimit);
                        msgManager.SendNamedMessage(msgCharacterLimit, clientId, writer);
                    } else {
                        // If the server responds, the client knows the character limit
                        reader.ReadValue(out int characterLimit);
                        f.serverCharacterLimit = characterLimit;
                    }
                });
                f.handlerRegistered = true;
            }

            if (f.serverHasMod == null) {
                if (__instance.NetworkManager.IsServer) {
                    // Modded server knows it has the mod
                    f.serverHasMod = true;
                } else {
                    // Connected clients send a message to check whether the server has the mod
                    var writer = new FastBufferWriter(100, Allocator.Temp);
                    writer.WriteValue(Plugin.modVersion);
                    msgManager.SendNamedMessage(msgModVersion, NetworkManager.ServerClientId, writer);
                    // Until a response is received, assume vanilla
                    f.serverHasMod = false;
                }
            }

            if (f.serverHasMod == true && f.serverCharacterLimit == null) {
                if (__instance.NetworkManager.IsServer) {
                    // Modded server knows its own character limit
                    f.serverCharacterLimit = Plugin.CharacterLimit;
                } else {
                    // Connected clients send a message to ask the server what the configured character limit is
                    var writer = new FastBufferWriter(0, Allocator.Temp);
                    msgManager.SendNamedMessage(msgCharacterLimit, NetworkManager.ServerClientId, writer);
                    // Until a response is received, assume the v1.0.0 default of 500 characters
                    f.serverCharacterLimit = 500;
                }
            }
        }
        if (f.chatTextField == null) {
            f.chatTextField = HUDManager.Instance?.chatTextField;
        }
        if (f.chatText == null) {
            f.chatText = HUDManager.Instance?.chatText;
        }
        if (f.chatTextFieldRect == null && f.chatTextField != null) {
            f.chatTextField.TryGetComponent<RectTransform>(out f.chatTextFieldRect);
        }
        if (f.chatTextRect == null && f.chatText != null) {
            f.chatText.TryGetComponent<RectTransform>(out f.chatTextRect);
        }
        if (f.chatTextBgRect == null && f.chatText != null) {
            f.chatText.transform.parent.Find("Image")?.TryGetComponent<RectTransform>(out f.chatTextBgRect);
        }
        if (f.shiftAction == null) {
            // Find shiftAction if it's already been created, otherwise create one
            f.shiftAction = __instance.playerActions.FindAction($"{Plugin.modGUID}.Shift");
            if (f.shiftAction == null) {
                __instance.playerActions.Disable();
                var map = __instance.playerActions.Movement;
                f.shiftAction = InputActionSetupExtensions.AddAction(map, $"{Plugin.modGUID}.Shift", binding: Keyboard.current.shiftKey.path, type: InputActionType.Button);
                __instance.playerActions.Enable();
            }
            f.shiftAction?.Enable();
        }
        if (f.scrollAction == null) {
            f.scrollAction = __instance.playerActions.FindAction($"{Plugin.modGUID}.Scroll");
            if (f.scrollAction == null) {
                __instance.playerActions.Disable();
                var map = __instance.playerActions.Movement;
                f.scrollAction = InputActionSetupExtensions.AddAction(map, $"{Plugin.modGUID}.Scroll", binding: Mouse.current.scroll.path, type: InputActionType.Value);
                __instance.playerActions.Enable();
            }
        }
    }

    // Disable submitting chat messages when shift is held
    [HarmonyPatch(typeof(HUDManager), "SubmitChat_performed")]
    [HarmonyPrefix]
    private static bool SubmitChat_performed() {
        return !(fields[StartOfRound.Instance.localPlayerController].shiftAction?.IsPressed()) ?? true;
    }

    public static string GetChatMessageNameColorTag() {
        var color = "#FF0000";
        var timestamp = "";
        if (Plugin.EnableTimestamps && TimeOfDay.Instance.currentDayTimeStarted && HUDManager.Instance?.clockNumber != null && HUDManager.Instance.clockNumber.IsActive() && HUDManager.Instance.Clock.targetAlpha > 0f) {
            timestamp = $"<color=#7069ff>[{HUDManager.Instance.clockNumber.text.Replace("\n", "")}] </color>";
        }
        return $"{timestamp}<color={color}>";
    }
    static MethodInfo getChatMessageNameColorTag = typeof(Patches).GetMethod(nameof(GetChatMessageNameColorTag));
    static MethodInfo stringEqual = typeof(string).GetMethod("op_Equality", new[] { typeof(string), typeof(string) });


    [HarmonyPatch(typeof(HUDManager), "AddPlayerChatMessageClientRpc")]
    [HarmonyPrefix]
    private static bool AddPlayerChatMessageClientRpc(string chatMessage, int playerId, HUDManager __instance) {
        // Skip adding message received from server to the message history if you are the one who sent the message to the server
        object __rpc_exec_stage = Traverse.Create(__instance).Field("__rpc_exec_stage").GetValue();
        if ((int)__rpc_exec_stage == 0x02 /* client */) {
            if (playerId != -1) {
                if (StartOfRound.Instance.allPlayerScripts[playerId].playerClientId == NetworkManager.Singleton.LocalClientId) {
                    return false;
                }
            }
        }
        return true;
    }

    [HarmonyPatch(typeof(HUDManager), "AddChatMessage")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> transpiler_AddChatMessage(IEnumerable<CodeInstruction> instructions) {
        bool foundMaxMessageCount = false;
        bool foundPreviousMessageComparison = false;
        foreach (var instruction in instructions) {
            // Remove chat message history limit of 4
            if (!foundMaxMessageCount && instruction.opcode == OpCodes.Ldc_I4_4) {
                yield return new CodeInstruction(OpCodes.Ldc_I4, int.MaxValue);
                foundMaxMessageCount = true;
                continue;
            } else if (!foundPreviousMessageComparison && instruction.opcode == OpCodes.Call && instruction.operand == (object)stringEqual) {
                // Limit the check that prevents the same message from being sent twice to only apply when the message sender is a non-player)
                // (This results in messages printing twice, once locally, and once after the server broadcasts the message back, which is fixed in a patch to `AddPlayerChatMessageClientRpc`)
                foundPreviousMessageComparison = true;
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Ldarg_2); // message sender name
                yield return new CodeInstruction(OpCodes.Call, typeof(string).GetMethod(nameof(string.IsNullOrEmpty)));
                yield return new CodeInstruction(OpCodes.And);
                continue;
            } else if (instruction.opcode == OpCodes.Ldstr) {
                switch ((string)instruction.operand) {
                    case "<color=#FF0000>":
                        yield return new CodeInstruction(OpCodes.Call, getChatMessageNameColorTag);
                        continue;
                    case "</color>: <color=#FFFF00>'":
                        yield return new CodeInstruction(OpCodes.Ldstr, "</color>: <color=#FFFF00>");
                        continue;
                    case "'</color>":
                        yield return new CodeInstruction(OpCodes.Ldstr, "</color>");
                        continue;
                }
            }
            yield return instruction;
        }
    }

    public static int GetCharacterLimit() {
        return Plugin.CharacterLimit + 1;
    }
    static MethodInfo getCharacterLimit = typeof(Patches).GetMethod(nameof(GetCharacterLimit));

    [HarmonyPatch(typeof(HUDManager), "SubmitChat_performed")]
    [HarmonyPatch(typeof(HUDManager), "AddPlayerChatMessageServerRpc")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> transpiler_SubmitChat_performed(IEnumerable<CodeInstruction> instructions) {
        foreach (var instruction in instructions) {
            var found = false;
            if (instruction.opcode == OpCodes.Ldc_I4_S) {
                if ((sbyte)instruction.operand == 50) {
                    found = true;
                    yield return new CodeInstruction(OpCodes.Call, getCharacterLimit);
                }
            }
            if (!found) {
                yield return instruction;
            }
        }
    }
}