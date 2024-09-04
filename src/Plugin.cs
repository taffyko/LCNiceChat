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
using System;
using System.Linq;
using TMPro;


namespace NiceChat;

[BepInPlugin(modGUID, modName, modVersion)]
public partial class Plugin : BaseUnityPlugin {
    public const string modGUID = PluginInfo.PLUGIN_GUID;
    public const string modName = PluginInfo.PLUGIN_NAME;
    public const string modVersion = PluginInfo.PLUGIN_VERSION;
    
    private readonly Harmony harmony = new Harmony(modGUID);
    public static ManualLogSource log = null!;
    internal static List<Action> cleanupActions = new List<Action>();

    private void Awake() {
        ConfigInit();
        log = BepInEx.Logging.Logger.CreateLogSource(modName);
        log.LogInfo($"Loading {modGUID}");

        // Plugin startup logic
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        
        ServerVars.characterLimit = new(
            "characterLimit",
            write: (writer, v) => writer.WriteValueSafe(v),
            read: (FastBufferReader r, out int v) => r.ReadValueSafe(out v),
            defaultValue: 49
        );
        ServerVars.hearDeadPlayers = new(
            "hearDeadPlayers",
            write: (writer, v) => writer.WriteValueSafe(v),
            read: (FastBufferReader r, out bool v) => r.ReadValueSafe(out v),
            defaultValue: false
        );
        ServerVars.messageRange = new(
            "messageRange",
            write: (writer, v) => writer.WriteValueSafe(v),
            read: (FastBufferReader r, out float v) => r.ReadValueSafe(out v),
            defaultValue: 25f
        );
        ServerVars.modVersion = new(
            "",
            write: (writer, v) => writer.WriteValueSafe(v),
            read: (FastBufferReader r, out string v) => r.ReadValueSafe(out v),
            defaultValue: null!
        );

    }

    private void OnDestroy() {
        #if DEBUG
        log?.LogInfo($"Unloading {modGUID}");
        var player = StartOfRound.Instance?.localPlayerController;
        if (NetworkManager.Singleton != null && player != null) {
            Patches.fields.TryGetValue(player, out var f);
            if (f != null && f.scrollContainerRect != null && f.chatTextBgRect != null && f.chatTextRect != null && f.chatTextFieldRect != null && f.hudBottomLeftCorner != null) {
                f.chatTextFieldRect.SetParent(f.hudBottomLeftCorner);
                f.chatTextBgRect.SetParent(f.hudBottomLeftCorner);
                f.chatTextRect.SetParent(f.chatTextBgRect.parent, false);
                Destroy(f.scrollContainerRect.gameObject);
                Destroy(f.chatTextBgRect.Find("ScrollMask")?.gameObject);
                Destroy(f.chatContainer?.gameObject);
            }
        }
        foreach (var fields in Patches.fields.Values) {
            foreach (var action in fields.cleanupActions) {
                action();
            }
        }
        harmony?.UnpatchSelf();
        foreach (var action in cleanupActions) {
            action();
        }
        #endif
    }
}

public static class ServerVars {
    public static ServerVar<int> characterLimit = null!;
    public static ServerVar<bool> hearDeadPlayers = null!;
    public static ServerVar<float> messageRange = null!;
    public static ServerVar<string> modVersion = null!;
}

[HarmonyPatch]
internal class Patches {
    public class CustomFields {
        public DateTime? connectTime = null;
        public bool networkReady = false;
        public InputAction? shiftAction = null;
        public InputAction? scrollAction = null;
        public TMPro.TMP_Text? chatText = null;
        public TMPro.TMP_InputField? chatTextField = null;
        public TMPro.TextMeshProUGUI? chatTextFieldGui = null;
        public float previousChatTextHeight = 0f;
        public float previousScrollPosition = 0f;
        public RectTransform? hudBottomLeftCorner = null;
        public RectTransform? chatContainer = null;
        public RectTransform? chatTextBgRect = null;
        public RectTransform? chatTextFieldRect = null;
        public RectTransform? chatTextRect = null;
        public RectTransform? scrollContainerRect = null;
        public CanvasGroup? scrollbarCanvasGroup = null;
        public ScrollRect? scroll = null;
        public Scrollbar? scrollbar = null;
        public Action? restoreHiddenHudElementsAction = null;
        public List<Action> cleanupActions = new List<Action>();
    }

    // Extra player instance fields
    public static Dictionary<PlayerControllerB, CustomFields> fields = new();

    private static bool IsLocalPlayer(PlayerControllerB __instance) {
        return __instance == StartOfRound.Instance?.localPlayerController;
    }

    // Custom action used to check if the player is holding either shift key
    [HarmonyPatch(typeof(PlayerControllerB), "Awake")]
    [HarmonyPostfix]
    private static void Player_Awake(PlayerControllerB __instance) {
        reload(__instance);
    }
    
    public static (HUDElement, float originalOpacity)[] hiddenElements = new (HUDElement, float)[] {};
    
    [HarmonyPatch(typeof(PlayerControllerB), "Update")]
    [HarmonyPostfix]
    private static void Player_Update(PlayerControllerB __instance) {
        reload(__instance);
        if (!IsLocalPlayer(__instance)) { return; }
        if (HUDManager.Instance == null) { return; }
        var f = fields[__instance];
    
        if (__instance.isPlayerDead && f.restoreHiddenHudElementsAction == null) {
            // Display chat while player is dead
            var hud = HUDManager.Instance;
            if (hud.HUDContainer.GetComponent<CanvasGroup>().alpha == 0f) {
                hud.HUDAnimator.SetTrigger("revealHud");
                hud.ClearControlTips();
                if (Plugin.SpectatorChatHideOtherHudElements) {
                    var hudElements = (HUDElement[])Traverse.Create(hud).Field("HUDElements").GetValue();
                    var bottomMiddle = hud.HUDContainer.transform.Find("BottomMiddle");

                    f.restoreHiddenHudElementsAction = () => {
                        foreach (var el in hudElements) {
                            if (el != hud.Chat) {
                                hud.PingHUDElement(el, 0f, 0f, el.targetAlpha);
                            }
                        }
                        if (bottomMiddle != null) { bottomMiddle.gameObject.SetActive(true); }
                    };

                    foreach (var el in hudElements) {
                        if (el != hud.Chat) {
                            hud.PingHUDElement(el, 0f, 0f, 0f);
                        }
                    }
                    if (bottomMiddle != null) { bottomMiddle.gameObject.SetActive(false); }
                }
            }
        }
        if (f.restoreHiddenHudElementsAction != null && (!__instance.isPlayerDead || !Plugin.SpectatorChatHideOtherHudElements)) {
            f.restoreHiddenHudElementsAction();
            f.restoreHiddenHudElementsAction = null;
        }

        if (f.chatTextField != null) {
            f.chatTextField.characterLimit = ServerVars.characterLimit.Value;
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

        if (f.chatTextFieldGui != null) {
            f.chatTextFieldGui.color = Plugin.InputTextColor;
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
                    UnityEngine.Object.Destroy(layoutElement);
                    layoutElement = f.chatText.gameObject.AddComponent<LayoutElement>();
                    layoutElement.minHeight = scrollMaskRect.rect.height;

                    f.chatText.alignment = TMPro.TextAlignmentOptions.BottomLeft;

                    f.chatText.gameObject.TryGetComponent<ContentSizeFitter>(out var fitter);
                    UnityEngine.Object.Destroy(fitter);
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
                    var scrollbar = new GameObject() { name = "Scrollbar" };
                    f.scrollbarCanvasGroup = scrollbar.AddComponent<CanvasGroup>();
                    var scrollbarRect = scrollbar.AddComponent<RectTransform>();
                    scrollbarRect.SetParent(f.scrollContainerRect, false);
                    scrollbarRect.anchorMin = new Vector2(1, 0);
                    scrollbarRect.anchorMax = Vector2.one;
                    // Scrollbar horizontal positioning and width
                    scrollbarRect.offsetMin = new Vector2(-7, 0);
                    scrollbarRect.offsetMax = new Vector2(-5, -5);
                    f.scrollbar = scrollbar.AddComponent<Scrollbar>();
                    var scrollbarImage = scrollbar.AddComponent<Image>();
                    scrollbarImage.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0,0,1,1), new Vector2(0, 0));
                    scrollbarImage.color = new Color(88f/255f, 94f/255f, 209f/255f, 50f/255f);

                    var scrollHandle = new GameObject() { name = "ScrollbarHandle" };
                    var scrollHandleRect = scrollHandle.AddComponent<RectTransform>();
                    scrollHandleRect.SetParent(scrollbarRect, false);
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

                if (f.scroll != null && f.scrollbar != null && f.scrollbarCanvasGroup != null) {
                    // Hide scrollbar handle when it fills the whole scrollbar
                    if (f.chatText.preferredHeight < f.scrollContainerRect.rect.height) {
                        f.scrollbar.handleRect.GetComponent<Image>().color = new Color(0,0,0,0);
                    } else {
                        f.scrollbar.handleRect.GetComponent<Image>().color = new Color(88f/255f, 94f/255f, 209f/255f, 112f/255f);
                    }
                    if (f.chatTextField.isFocused) {
                        HUDManager.Instance.Chat.targetAlpha = 1f;
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
                    f.scrollbarCanvasGroup.alpha = Plugin.ShowScrollbar && f.chatTextField.isFocused ? 1f : 0f;
                    f.previousScrollPosition = f.scroll.verticalNormalizedPosition;
                }
            }
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB), "OnDestroy")]
    [HarmonyPostfix]
    private static void OnDestroy(PlayerControllerB __instance) {
        if (IsLocalPlayer(__instance)) {
            if (fields.ContainsKey(__instance)) {
                foreach (var action in fields[__instance].cleanupActions) {
                    action();
                }
            }
        }
        fields.Remove(__instance);
    }

    // Sets up references and handlers as needed, so that the mod can be hot-reloaded at any time
    private static void reload(PlayerControllerB __instance) {
        CustomFields f;
        if (!fields.ContainsKey(__instance)) {
            fields[__instance] = new CustomFields();
            f = fields[__instance];
            f.cleanupActions.Add(() => {
                if (f.restoreHiddenHudElementsAction != null) {
                    f.restoreHiddenHudElementsAction();
                }
            });
        } else {
            f = fields[__instance];
        }

        if (!IsLocalPlayer(__instance)) { return; }

        if (f.hudBottomLeftCorner == null) {
            f.hudBottomLeftCorner = HUDManager.Instance.HUDContainer.transform.Find("BottomLeftCorner") as RectTransform;
        }

        if (__instance.NetworkManager.IsConnectedClient) {
            if (__instance.NetworkManager.IsServer) {
                ServerVars.characterLimit.Value = Plugin.CharacterLimit;
                ServerVars.modVersion.Value = Plugin.modVersion;
                ServerVars.hearDeadPlayers.Value = Plugin.HearDeadPlayers;
                ServerVars.messageRange.Value = Plugin.MessageRange;
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
        if (f.chatTextFieldGui == null && f.chatTextField != null) {
            f.chatTextFieldGui = f.chatTextField.transform.Find("Text Area/Text")?.GetComponent<TextMeshProUGUI>();
        }
        if (f.chatTextRect == null && f.chatText != null) {
            f.chatText.TryGetComponent<RectTransform>(out f.chatTextRect);
        }
        if (f.chatTextBgRect == null && f.chatTextField != null) {
            f.chatTextField.transform.parent.Find("Image")?.TryGetComponent<RectTransform>(out f.chatTextBgRect);
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

        if (Plugin.GuiFadeFix) {
            if (f.chatContainer == null) {
                var chatContainerObject = new GameObject($"{Plugin.modGUID}.ChatContainer");
                f.chatContainer = chatContainerObject.AddComponent<RectTransform>();
                chatContainerObject.AddComponent<CanvasGroup>();
                f.chatContainer.SetParent(f.hudBottomLeftCorner, false);
            }
            if (f.chatTextFieldRect?.parent != f.chatContainer) {
                f.chatTextFieldRect?.SetParent(f.chatContainer);
                f.chatTextBgRect?.SetParent(f.chatContainer);
                HUDManager.Instance!.Chat.canvasGroup = f.chatContainer.GetComponent<CanvasGroup>();
                f.hudBottomLeftCorner!.GetComponent<CanvasGroup>().alpha = 1f;
            }
        } else {
            if (f.chatTextFieldRect?.parent != f.hudBottomLeftCorner) {
                f.chatTextFieldRect?.SetParent(f.hudBottomLeftCorner);
                f.chatTextBgRect?.SetParent(f.hudBottomLeftCorner);
                HUDManager.Instance!.Chat.canvasGroup = f.hudBottomLeftCorner!.GetComponent<CanvasGroup>();
            }
        }
    }

    // Disable submitting chat messages when shift is held
    [HarmonyPatch(typeof(HUDManager), "SubmitChat_performed")]
    [HarmonyPrefix]
    private static bool SubmitChat_performed() {
        return !(fields[StartOfRound.Instance.localPlayerController].shiftAction?.IsPressed()) ?? true;
    }

    [HarmonyPatch(typeof(HUDManager), "PingHUDElement")]
    [HarmonyPrefix]
    private static void PingHUDElementPrefix(HUDElement element, ref float delay, float startAlpha, ref float endAlpha) {
        if (element != null && element == HUDManager.Instance?.Chat) {
            endAlpha = Plugin.FadeOpacity;
            if (delay == 4f) {
                delay = Plugin.FadeTimeAfterMessage;
            } else if (delay == 2f) {
                delay = Plugin.FadeTimeAfterOwnMessage;
            } else if (delay == 1f) {
                delay = Plugin.FadeTimeAfterUnfocused;
            }
        }
    }

    public static string GetChatMessageNameOpeningTag() {
        var color = "#FF0000";
        string text = "";
        if (
            Plugin.EnableTimestamps && TimeOfDay.Instance.currentDayTimeStarted && HUDManager.Instance?.clockNumber != null
            && (
                (HUDManager.Instance.clockNumber.IsActive() && HUDManager.Instance.Clock.targetAlpha > 0f)
                || StartOfRound.Instance.localPlayerController.isPlayerDead
            )
        ) {
            text += $"<color=#7069ff>[{HUDManager.Instance.clockNumber.text.Replace("\n", "")}] </color>";
        }

        {
            if (messageContext != null) {
                if (messageContext.senderDead) {
                    text += $"<color=#878787>*DEAD* ";
                    goto next;
                } else if (messageContext.walkie) {
                    text += $"<color=#006600>*WALKIE* ";
                    goto next;
                }
            }
            text += $"<color={color}>";
        }
        next:

        messageContext = null;
        return text;
    }

    public static string GetChatMessageNameClosingTag() {
        return "</color>: <color=#FFFF00>";
    }

    static MethodInfo MethodInfo_GetChatMessageNameOpeningTag = typeof(Patches).GetMethod(nameof(GetChatMessageNameOpeningTag));
    static MethodInfo MethodInfo_GetChatMessageNameClosingTag = typeof(Patches).GetMethod(nameof(GetChatMessageNameClosingTag));
    static MethodInfo stringEqual = typeof(string).GetMethod("op_Equality", new[] { typeof(string), typeof(string) });


    public static bool _HelperMessageSentByLocalPlayer(HUDManager instance, string chatMessage, int playerId) {
        object __rpc_exec_stage = Traverse.Create(instance).Field("__rpc_exec_stage").GetValue();
        if ((int)__rpc_exec_stage == 0x02 /* client */) {
            if (playerId >= 0 && playerId < StartOfRound.Instance.allPlayerScripts.Length) {
                if (IsLocalPlayer(StartOfRound.Instance.allPlayerScripts[playerId])) {
                    return true;
                }
            }
        }
        return false;
    }

    [HarmonyPatch(typeof(HUDManager), "AddPlayerChatMessageClientRpc")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> transpiler_AddPlayerChatMessageClientRpc(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
        yield return new CodeInstruction(OpCodes.Ldarg_0);
        yield return new CodeInstruction(OpCodes.Ldarg_1);
        yield return new CodeInstruction(OpCodes.Ldarg_2);
        // check if the message coming from the server was one that you sent and have already added to history on the client-side
        yield return new CodeInstruction(OpCodes.Call, typeof(Patches).GetMethod(nameof(_HelperMessageSentByLocalPlayer)));
        var label = generator.DefineLabel();
        var nopInstruction = new CodeInstruction(OpCodes.Nop);
        nopInstruction.labels.Add(label);
        var retInstruction = new CodeInstruction(OpCodes.Ret);
        var brfalseInstruction = new CodeInstruction(OpCodes.Brfalse, label); // skip early return when function returns false
        yield return brfalseInstruction;
        yield return retInstruction;
        yield return nopInstruction;

        foreach (var instruction in instructions) {
            yield return instruction;
        }
    }
    
    record MessageContext(int senderId, bool walkie, bool senderDead);
    static MessageContext? messageContext = null;
        
    public static bool CanHearMessage(int senderId) {
        // defensive
        if (senderId < 0) { return true; }
        if (HUDManager.Instance == null) { return true; }
        if (HUDManager.Instance.playersManager.allPlayerScripts.Length <= senderId) { return true; }

        var sender = HUDManager.Instance.playersManager.allPlayerScripts[senderId];
        var recipient = StartOfRound.Instance.localPlayerController;
        if (sender == null || recipient == null) { return true; }
        
        var walkie = sender.holdingWalkieTalkie && recipient.holdingWalkieTalkie;
        
        var send = false;
        {
            if (sender.isPlayerDead) {
                if (ServerVars.hearDeadPlayers.Value || recipient.isPlayerDead) {
                    send = true; goto end;
                } else {
                    goto end;
                }
            }
            
            if (walkie) {
                send = true; goto end;
            }

            var inRange = (Vector3.Distance(sender.transform.position, recipient.transform.position) > ServerVars.messageRange.Value);
            if (inRange) {
                send = true; goto end;
            }
        }
        
        end:
        if (send) {
            messageContext = new (senderId, walkie, sender.isPlayerDead);
        }
        return send;
    }
    
    static MethodInfo MethodInfo_Vector3_Distance = typeof(Vector3).GetMethod(nameof(Vector3.Distance));
    static MethodInfo MethodInfo_PlayerCanHearMessage = typeof(Patches).GetMethod(nameof(CanHearMessage));

    [HarmonyPatch(typeof(HUDManager), "AddPlayerChatMessageClientRpc")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> transpiler_CanHearMessageOverride(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
        bool deadCheckFound = false;
        bool distanceCheckFound = false;
        using (var e = instructions.GetEnumerator()) {
            while (e.MoveNext()) {
                if (!deadCheckFound && e.Current.opcode == OpCodes.Ldfld && (FieldInfo)e.Current.operand == FieldInfo_isPlayerDead) {
                    deadCheckFound = true;
                    // Yield next 5 instructions
                    for (int i = 0; i < 5; i++) { yield return e.Current; e.MoveNext(); }
                    // Replace early-return with no-op
                    yield return new CodeInstruction(OpCodes.Nop);
                    e.MoveNext();
                } else if (!distanceCheckFound && e.Current.opcode == OpCodes.Call && (MethodInfo)e.Current.operand == MethodInfo_Vector3_Distance) {
                    distanceCheckFound = true;
                    // Yield next 4 instructions
                    for (int i = 0; i < 4; i++) { yield return e.Current; e.MoveNext(); }
                    // Hijack early-return conditional
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Ldarg_2); // playerId
                    yield return new CodeInstruction(OpCodes.Call, MethodInfo_PlayerCanHearMessage);
                }
                yield return e.Current;
            }
        }
    }

    private static float timeAtLastCheck = 0.0f;
    public static bool _ShouldSuppressDuplicateMessage(string message, string senderName) {
        var timeSinceLastCheck = Time.fixedUnscaledTime - timeAtLastCheck;
        timeAtLastCheck = Time.fixedUnscaledTime;
        if (string.IsNullOrEmpty(senderName)) {
            // Don't bypass the check when the sender is a non-player
            return true;
        } else if (timeSinceLastCheck >= 0.0f && timeSinceLastCheck < 0.1f) {
            // Don't bypass the check if less than 100ms have passed since the last identical message
            // (This was introduced to combat duplicate chat RPC events introduced when playing with the MirrorDecor mod, which caused the host to receive all messages multiple times)
            return true;
        }
        return false;
    }


    [HarmonyPatch(typeof(HUDManager), "AddChatMessage")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> transpiler_AddChatMessage(IEnumerable<CodeInstruction> instructions) {
        bool foundMaxMessageCount = false;
        bool foundPreviousMessageComparison = false;
        foreach (var instruction in instructions) {
            if (!foundMaxMessageCount && instruction.opcode == OpCodes.Ldc_I4_4) {
                // Remove chat message history limit of 4
                yield return new CodeInstruction(OpCodes.Ldc_I4, int.MaxValue);
                foundMaxMessageCount = true;
                continue;
            } else if (!foundPreviousMessageComparison && instruction.opcode == OpCodes.Call && instruction.operand == (object)stringEqual) {
                // Limit the check that prevents the same message from being sent twice to only apply under certain circumstances
                // so that players can say the same thing twice.
                // - This results in a player's own messages potentially printing multiple times, once locally, and one or more times after the server broadcasts the message back.
                //   A patch to `AddPlayerChatMessageClientRpc` attempts to prevent this problem.
                foundPreviousMessageComparison = true;
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Ldarg_1); // message contents
                yield return new CodeInstruction(OpCodes.Ldarg_2); // message sender name
                yield return new CodeInstruction(OpCodes.Call, typeof(Patches).GetMethod(nameof(_ShouldSuppressDuplicateMessage)));
                yield return new CodeInstruction(OpCodes.And);
                continue;
            } else if (instruction.opcode == OpCodes.Ldstr) {
                switch ((string)instruction.operand) {
                    case "<color=#FF0000>":
                        yield return new CodeInstruction(OpCodes.Call, MethodInfo_GetChatMessageNameOpeningTag);
                        continue;
                    case "</color>: <color=#FFFF00>'":
                        yield return new CodeInstruction(OpCodes.Call, MethodInfo_GetChatMessageNameClosingTag);
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
    static FieldInfo FieldInfo_isPlayerDead = typeof(PlayerControllerB).GetField(nameof(PlayerControllerB.isPlayerDead));


    [HarmonyPatch(typeof(HUDManager), "SubmitChat_performed")]
    [HarmonyPatch(typeof(HUDManager), "EnableChat_performed")]
    [HarmonyPatch(typeof(HUDManager), "AddPlayerChatMessageServerRpc")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> transpiler_SubmitChat_performed(IEnumerable<CodeInstruction> instructions) {
        var foundCharacterLimit = false;
        var foundPlayerDeadCheck = false;
        foreach (var instruction in instructions) {
            if (!foundCharacterLimit && instruction.opcode == OpCodes.Ldc_I4_S) {
                if ((sbyte)instruction.operand == 50) {
                    foundCharacterLimit = true;
                    yield return new CodeInstruction(OpCodes.Call, getCharacterLimit);
                    continue;
                }
            } else if (!foundPlayerDeadCheck && instruction.opcode == OpCodes.Ldfld && (FieldInfo)instruction.operand == FieldInfo_isPlayerDead) {
                foundPlayerDeadCheck = true;
                yield return new CodeInstruction(OpCodes.Pop);
                yield return new CodeInstruction(OpCodes.Ldc_I4_0);
                continue;
            }
            yield return instruction;
        }
    }
    
    
    [HarmonyPatch(typeof(HUDManager), "AddTextToChatOnServer")]
    [HarmonyPrefix]
    private static void AddTextToChatOnServer_Prefix(HUDManager __instance, int playerId) {
        if (playerId >= 0 && playerId < StartOfRound.Instance.allPlayerScripts.Length) {
            if (IsLocalPlayer(StartOfRound.Instance.allPlayerScripts[playerId])) {
                // Set message context for formatting own messages
                CanHearMessage(playerId);
            }
        }
    }
}