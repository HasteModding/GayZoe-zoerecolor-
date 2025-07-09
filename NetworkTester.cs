using Landfall.Haste;
using Landfall.Modding;
using UnityEngine;
using UnityEngine.Localization;
using Zorro.Settings;
using System.Reflection;
using Unity.Mathematics;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Zorro.Core.CLI;
using TMPro;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections;
using Unity.Netcode;
using Zorro.Core;
using UnityEngine.Events;
using NetworkHack;
using System.Text;


namespace NetworkTest;

[LandfallPlugin]
public class NetworkTest
{
    static NetworkTest()
    {
        On.Landfall.Haste.NGOPlayer.OnNetworkSpawn += (orig, self) =>
        {
            orig(self);
            if (!self.IsOwner)
                return;
            
            CustomMessagingManager msgManager = NetworkManager.Singleton.CustomMessagingManager;

            msgManager.RegisterNamedMessageHandler("TestPing", PingCallBack);
        };
    }

    private static void PingCallBack(ulong clientId, FastBufferReader reader)
    {
        // Debug.Log("Ping Call Back");
        string msg;
        reader.ReadValueSafe(out msg);
        Debug.Log($"Ping Recieved from: {clientId} with Message: {msg}");
    }

    [ConsoleCommand]
    public static void PingServer()
    {
        string msg = $"I am id: {NetworkManager.Singleton.LocalClientId} sending ping";
        using FastBufferWriter writer = new FastBufferWriter(FastBufferWriter.GetWriteSize(msg), Unity.Collections.Allocator.Temp);
        writer.WriteValueSafe(msg);
        NetworkHackManager.ForceSendMessage("TestPing", 0, writer);
    }


    [ConsoleCommand]
    public static void PingAll()
    {
        string msg = $"I am id: {NetworkManager.Singleton.LocalClientId} sending ping";
        using FastBufferWriter writer = new FastBufferWriter(FastBufferWriter.GetWriteSize(msg), Unity.Collections.Allocator.Temp);
        writer.WriteValueSafe(msg);
        NetworkHackManager.ForceSendMessageToAll("TestPing", writer);
    }
    [ConsoleCommand]
    public static void InitNetworkHack()
    {
        NetworkHackManager.Init();
    }
}