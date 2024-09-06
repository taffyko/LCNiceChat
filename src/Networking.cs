using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine.TextCore.LowLevel;

namespace NiceChat;
public interface IServerVar : IDisposable {
    void TryRegister();
    public static List<IServerVar> AllServerVars = new();
}

public class ServerVar<T> : IServerVar where T : IEquatable<T>
{
    public string Id { get; init; }
    private T value;
    public T Value {
        get => value;
        set {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer) {
                if (!EqualityComparer<T>.Default.Equals(this.value, value)) {
                    this.value = value;
                    if (NetworkManager.Singleton.IsConnectedClient) {
                        // Broadcast updated value to clients
                        Plugin.log.LogDebug($"[{Id}] Broadcasting new value \"{value}\" to all clients");
                        var writer = new FastBufferWriter(128, Allocator.Temp);
                        writeValue(writer, value);
                        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(Id, writer);
                    }
                }
            } else {
                Plugin.log.LogWarning($"Client tried to set ServerVar \"{Id}\"");
            }
        }
    }
    
    public delegate void ReadFromReader<T1>(FastBufferReader reader, out T1 value);

    Action<FastBufferWriter, T> writeValue;
    ReadFromReader<T> readValue;

    public ServerVar(string id, Action<FastBufferWriter, T> write, ReadFromReader<T> read, T defaultValue = default!) {
        this.value = defaultValue;
        this.Id = String.IsNullOrEmpty(id) ? Plugin.modGUID : $"{Plugin.modGUID}.{id}";
        this.writeValue = write;
        this.readValue = read;
        IServerVar.AllServerVars.Add(this);
        ((IServerVar)this).TryRegister();
        Plugin.cleanupActions.Add(Dispose);
    }

    void Handler(ulong senderClientId, FastBufferReader messagePayload) {
        if (senderClientId != NetworkManager.Singleton.LocalClientId) {
            if (NetworkManager.Singleton.IsServer) {
                // Server replies to inquiries from clients
                var writer = new FastBufferWriter(128, Allocator.Temp);
                writeValue(writer, value);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(Id, senderClientId, writer);
                Plugin.log.LogDebug($"[{Id}] Responded to request from {senderClientId} with value: \"{value}\"");
            } else {
                // Clients update the value with the reply from the server
                readValue(messagePayload, out value);
                Plugin.log.LogDebug($"[{Id}] Received update from {senderClientId} with value: \"{value}\"");
            }
        }
    }

    void OnClientConnectedCallback(ulong clientId) {
        if (clientId == NetworkManager.Singleton.LocalClientId) {
            ((IServerVar)this).TryRegister();
        }
    }

    public void Dispose() {
        Plugin.log.LogDebug($"[{Id}] Disposed ServerVar");
        if (NetworkManager.Singleton != null) {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnectedCallback;
            if (NetworkManager.Singleton.IsConnectedClient) {
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(Id);
            }
        }
        IServerVar.AllServerVars.Remove(this);
    }

    void IServerVar.TryRegister() {
        Plugin.log.LogDebug($"[{Id}] Attempting to register. NetworkManager: {NetworkManager.Singleton != null}, Connected: {NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient}");
        if (NetworkManager.Singleton != null) {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;
            if (NetworkManager.Singleton.IsConnectedClient) {
                // FIXME
                NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(Id, Handler);
                NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(Id, Handler);
                NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(Id, Handler);
                Plugin.log.LogDebug($"[{Id}] Registered ServerVar");
                if (!NetworkManager.Singleton.IsServer) {
                    // After connecting, request value from the server
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(Id, NetworkManager.ServerClientId, new FastBufferWriter(0, Allocator.Temp));
                    Plugin.log.LogDebug($"[{Id}] Sent request from {NetworkManager.Singleton.LocalClientId} to {NetworkManager.ServerClientId}");
                }
            }
        }
    }
}

[HarmonyPatch]
class NetworkingPatches {
    [HarmonyPatch(typeof(StartOfRound), "Awake")]
    [HarmonyPostfix]
    private static void StartOfRound_Awake_Postfix() {
        foreach (var v in IServerVar.AllServerVars) {
            v.TryRegister();
        }
    }
}