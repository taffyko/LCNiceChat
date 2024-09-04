using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine.TextCore.LowLevel;

namespace NiceChat;

public class ServerVar<T> : IDisposable where T : IEquatable<T>
{
    public string Id { get; init; }
    private T value;
    public T Value {
        get => value;
        set {
            if (NetworkManager.Singleton.IsServer) {
                if (!EqualityComparer<T>.Default.Equals(this.value, value)) {
                    this.value = value;
                    if (NetworkManager.Singleton.IsConnectedClient) {
                        // Broadcast updated value to clients
                        #if DEBUG
                        Plugin.log.LogInfo($"[{Id}] Broadcasting new value \"{value}\" to all clients");
                        #endif
                        var writer = new FastBufferWriter(128, Allocator.Temp);
                        writeValue(writer, value);
                        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(Id, writer);
                    }
                }
            } else {
                Plugin.log.LogWarning($"Client {NetworkManager.Singleton.LocalClientId} tried to set ServerVar \"{Id}\"");
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
        if (NetworkManager.Singleton.IsConnectedClient) {
            Register();
        }
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;
        Plugin.cleanupActions.Add(Dispose);
    }

    void Handler(ulong senderClientId, FastBufferReader messagePayload) {
        if (senderClientId != NetworkManager.Singleton.LocalClientId) {
            if (NetworkManager.Singleton.IsServer) {
                // Server replies to inquiries from clients
                var writer = new FastBufferWriter(128, Allocator.Temp);
                writeValue(writer, value);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(Id, senderClientId, writer);
                #if DEBUG
                Plugin.log.LogInfo($"[{Id}] Responded to request from {senderClientId} with value: \"{value}\"");
                #endif
            } else {
                // Clients update the value with the reply from the server
                readValue(messagePayload, out value);
                #if DEBUG
                Plugin.log.LogInfo($"[{Id}] Received update from {senderClientId} with value: \"{value}\"");
                #endif
            }
        }
    }
    
    void Register() {
        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(Id, Handler);
        #if DEBUG
        Plugin.log.LogInfo($"[{Id}] Registered ServerVar");
        #endif
        if (!NetworkManager.Singleton.IsServer) {
            // After connecting, request value from the server
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(Id, NetworkManager.ServerClientId, new FastBufferWriter(0, Allocator.Temp));
            #if DEBUG
            Plugin.log.LogInfo($"[{Id}] Sent request from {NetworkManager.Singleton.LocalClientId} to {NetworkManager.ServerClientId}");
            #endif
        }
    }

    void OnClientConnectedCallback(ulong clientId) {
        if (clientId == NetworkManager.Singleton.LocalClientId) {
            Register();
        }
    }

    public void Dispose()
    {
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnectedCallback;
        if (NetworkManager.Singleton.CustomMessagingManager != null) {
            NetworkManager.Singleton.CustomMessagingManager?.UnregisterNamedMessageHandler(Id);
            Plugin.log.LogInfo($"[{Id}] Unregistered ServerVar");
        }
    }
}