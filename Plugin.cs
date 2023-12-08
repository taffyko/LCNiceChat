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
    public const string modVersion = "1.0.0";
    
    private readonly Harmony harmony = new Harmony(modGUID);
    public static ManualLogSource? log;

    private static float? defaultFontSize;
    public static float DefaultFontSize => defaultFontSize ?? 11f;
    private static bool? enlargeChatWindow;
    public static bool EnlargeChatWindow => enlargeChatWindow ?? true;

    private void Awake() {
        if (float.TryParse(
            Config.Bind<string?>("Chat", "DefaultFontSize", null, "(default: 11) The base game's font size is 13").Value,
            out var _defaultFontSize
        )) { defaultFontSize = _defaultFontSize; };
        if (bool.TryParse(
            Config.Bind<string?>("Chat", "EnlargeChatWindow", null, "(default: true) Increases the size of the chat area").Value,
            out bool _enlargeChatWindow
        )) { enlargeChatWindow = _enlargeChatWindow; };
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
        public TMPro.TMP_Text? chatText = null;
        public TMPro.TMP_InputField? chatTextField = null;
        public RectTransform? chatTextBgRect = null;
        public RectTransform? chatTextFieldRect = null;
        public RectTransform? chatTextRect = null;
        public RectTransform? scrollContainerRect = null;
        public bool? serverHasMod = null;
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
                f.chatTextField.characterLimit = 500;
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
        }

        if (f.chatTextRect != null && f.chatTextBgRect != null && f.chatTextFieldRect != null) {
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
                    var scrollMask = new GameObject{ name = "ScrollMask" };
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

                    // Add `ScrollRect` component and configure
                    var scrollComponent = scrollContainer.AddComponent<ScrollRect>();
                    scrollComponent.content = f.chatTextRect;
                    scrollComponent.viewport = scrollMaskRect;
                    scrollComponent.vertical = true;
                    scrollComponent.horizontal = false;
                }
            }

            // Make chatText fill the scroll container
            f.chatTextRect.anchorMin = Vector2.zero;
            f.chatTextRect.anchorMax = Vector2.one;
            // Horizontal margins
            f.chatTextRect.offsetMin = new Vector2(9f, 0f);
            f.chatTextRect.offsetMax = new Vector2(-5f, 0f);
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB), "OnDestroy")]
    [HarmonyPostfix]
    private static void OnDestroy(PlayerControllerB __instance) {
        NetworkManager.Singleton?.CustomMessagingManager?.UnregisterNamedMessageHandler(Plugin.modGUID);
        fields.Remove(__instance);
    }

    // Sets up references and handlers as needed, so that the mod can be hot-reloaded at any time
    private static void reload(PlayerControllerB __instance) {
        if (!fields.ContainsKey(__instance)) { fields[__instance] = new CustomFields(); }
        var f = fields[__instance];

        if (NetworkManager.Singleton?.LocalClientId != __instance.playerClientId) { return; }

        if (__instance.NetworkManager.IsConnectedClient || __instance.NetworkManager.IsServer) {
            var msgManager = __instance.NetworkManager.CustomMessagingManager;
            if (!f.handlerRegistered) {
                msgManager.RegisterNamedMessageHandler(Plugin.modGUID, (ulong clientId, FastBufferReader reader) => {
                    if (__instance.NetworkManager.IsServer) {
                        // Modded server responds to inquiries from clients
                        msgManager.SendNamedMessage(Plugin.modGUID, clientId, new FastBufferWriter(0, Allocator.Temp));
                    } else {
                        // If the server responds, the client knows it has the mod
                        f.serverHasMod = true;
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
                    f.serverHasMod = false;
                    msgManager.SendNamedMessage(Plugin.modGUID, NetworkManager.ServerClientId, new FastBufferWriter(0, Allocator.Temp));
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
    }

    // Disable submitting chat messages when shift is held
    [HarmonyPatch(typeof(HUDManager), "SubmitChat_performed")]
    [HarmonyPrefix]
    private static bool SubmitChat_performed() {
        return !(fields[StartOfRound.Instance.localPlayerController].shiftAction?.IsPressed()) ?? true;
    }

    public static string GetChatMessageNameColorTag() {
        var color = "#FF0000";
        return $"<color={color}>";
    }
    static MethodInfo getChatMessageNameColorTag = typeof(Patches).GetMethod(nameof(GetChatMessageNameColorTag));

    [HarmonyPatch(typeof(HUDManager), "AddChatMessage")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> transpiler_AddChatMessage(IEnumerable<CodeInstruction> instructions) {
        bool foundMaxMessageCount = false;
        foreach (var instruction in instructions) {
            // Remove chat message history limit of 4
            if (!foundMaxMessageCount && instruction.opcode == OpCodes.Ldc_I4_4) {
                yield return new CodeInstruction(OpCodes.Ldc_I4, int.MaxValue);
                foundMaxMessageCount = true;
                continue;
            }
            if (instruction.opcode == OpCodes.Ldstr) {
                switch ((string)instruction.operand)
                {
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

    [HarmonyPatch(typeof(HUDManager), "SubmitChat_performed")]
    [HarmonyPatch(typeof(HUDManager), "AddPlayerChatMessageServerRpc")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> transpiler_SubmitChat_performed(IEnumerable<CodeInstruction> instructions) {
        foreach (var instruction in instructions) {
            var found = false;
            if (instruction.opcode == OpCodes.Ldc_I4_S) {
                if ((sbyte)instruction.operand == 50) {
                    found = true;
                    yield return new CodeInstruction(OpCodes.Ldc_I4, 501);
                }
            }
            if (!found) {
                yield return instruction;
            }
        }
    }
}