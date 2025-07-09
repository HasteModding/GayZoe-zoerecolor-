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
using System.Text;
using MonoMod;
using MonoMod.RuntimeDetour;



namespace NetworkHack;

public static class NetworkHackManager
{

    static bool ForcedServer;
    public static void ForceSendMessage(string name, ulong id, FastBufferWriter buffer)
    {
        if(!NetworkManager.Singleton.IsServer&&id!=0ul)
            ForcedServer = true;
        

        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(name, id, buffer);
        ForcedServer = false;
    }
    
    public static void ForceSendMessageToAll(string name, FastBufferWriter buffer, bool ignoreSelf = true)
    {
      foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
      {
        if (ignoreSelf && id == NetworkManager.Singleton.LocalClientId)
          continue;
        ForceSendMessage(name, id, buffer);
      }
    }

    public static void Init()
    {
      //HOLY FUCK ITS ALL REFLECTION I HATE IT OMG


      Type messageManagerType = typeof(NetworkManager).Assembly.GetType("Unity.Netcode.NetworkMessageManager");
      MethodInfo canSendMethod = messageManagerType.GetMethod("CanSend", BindingFlags.NonPublic| BindingFlags.Instance);

      Hook hook = new Hook(canSendMethod,
          (Func<object, ulong, Type, NetworkDelivery, bool>)((self, targetClientId, messageType, delivery) =>
          {
            // Debug.LogWarning("Forcing Custom CanSend logic to: "+(ForcedServer||NetworkManager.Singleton.IsServer||targetClientId==0)); so apparently this gets checked every frame but uh trust me it works (atleast i think)
            //Actually not sure if this step is needed as Im getting blocked in the network transport anyways, would need to do more testing to verify
            //Might back out here tho - modifying NetworkTransports seems a little difficult
            return ForcedServer || NetworkManager.Singleton.IsServer || targetClientId == 0;
          })
      );
        
      On.Unity.Netcode.CustomMessagingManager.SendNamedMessage_string_ulong_FastBufferWriter_NetworkDelivery += (self, orig, name, id, buffer, networkDelivery) =>
      {
        // self.ValidateMessageSize(messageStream, networkDelivery, true); -- ehhhhhhh prolly not important
          

        

          ulong hash = 0;
          switch (NetworkManager.Singleton.NetworkConfig.RpcHashSize)
          {
              case HashSize.VarIntFourBytes:
                  hash = (ulong)name.Hash32();
                  break;
              case HashSize.VarIntEightBytes:
                  hash = name.Hash64();
                  break;
          }
          Debug.LogWarning("Hash Size Calculated");

          if (NetworkManager.Singleton.IsHost && (long)id == (long)NetworkManager.Singleton.LocalClientId)//Checks if we are host or sending message to ourselves if we are invoke the message locally -- we can allow this and it shouldn't? cause problems later
          {
              
              Debug.LogWarning("Are host or are sending message to ourself");
              MethodInfo InvokeNamedMessageReflection = self.GetType().GetMethod("InvokeNamedMessage", BindingFlags.NonPublic | BindingFlags.Instance);
              InvokeNamedMessageReflection.Invoke(self, new object[] { hash, NetworkManager.Singleton.LocalClientId, new FastBufferReader(buffer, Unity.Collections.Allocator.None), 0 });
          }
          else
          {
              var asm = typeof(NetworkManager).Assembly;
              var namedMessageType = asm.GetType("Unity.Netcode.NamedMessage");
              if (namedMessageType == null)
              {
                  throw new Exception("NamedMessage type not found! D:");
              }

              Debug.LogWarning("Found NamedMessageType");

              var message = Activator.CreateInstance(namedMessageType, nonPublic: true);
              namedMessageType.GetField("Hash").SetValue(message, hash);
              namedMessageType.GetField("SendData").SetValue(message, buffer);

              Debug.LogWarning("NamedMessage Fields Set");

              //HOLY REFLECTIONS BRO LIKE GIVE ME A BREAKKKKKKKKK
              NetworkConnectionManager connectionManager = typeof(NetworkManager).GetField("ConnectionManager", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(NetworkManager.Singleton) as NetworkConnectionManager;
              Debug.LogWarning("NetworkConnectionManager Found");
              var SendMessageMethods = connectionManager.GetType()
                  .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                  .Where(m => m.Name == "SendMessage" && m.IsGenericMethod);
              
              Debug.LogWarning("SendMessageMethods Found");
              var targetSendMessageMethod = SendMessageMethods.FirstOrDefault(m =>
              {
                  var parameters = m.GetParameters();
                  return parameters.Length == 3
                      && parameters[0].ParameterType.IsByRef
                      && parameters[1].ParameterType == typeof(NetworkDelivery)
                      && parameters[2].ParameterType == typeof(ulong);
              });

              if (targetSendMessageMethod == null)
                  throw new Exception("Correct SendMessage<T> overload not found");

              Debug.LogWarning("Correct SendMessage<T> has been found");

              var genericSendMessageMethod = targetSendMessageMethod.MakeGenericMethod(namedMessageType);

              if (ForcedServer)
              {
                  //Escalate Permissions
                  typeof(NetworkClient).GetProperty("IsServer", BindingFlags.NonPublic|BindingFlags.Instance).SetValue(connectionManager.GetType().GetField("LocalClient", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(connectionManager), true);
                  Debug.LogWarning("Escalated NetworkClient Permissions");
              }

              int bytesCount = (int)genericSendMessageMethod.Invoke(connectionManager, new object[] { message, networkDelivery, id });

              Debug.LogWarning("Sent Message");

              if (bytesCount == 0)
                  return;
              var INetworkMetricsType = asm.GetType("Unity.Netcode.INetworkMetrics");

              Debug.LogWarning("Found INetworkMetricsType");

              var trackNamedMessageMethods = INetworkMetricsType.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == "TrackNamedMessageSent");

              Debug.LogWarning("Found trackNamedMessage Methods");

              var targetTrackNamedMessageMethod = trackNamedMessageMethods.FirstOrDefault(m =>
              {
                  var parameters = m.GetParameters();
                  return parameters.Length == 3 && parameters[0].ParameterType == typeof(ulong);
              });

              Debug.LogWarning("Found target trackNamedMessage Method");
              var networkMetricsManager = typeof(NetworkManager).GetField("MetricsManager", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(NetworkManager.Singleton);
              Debug.LogWarning("Invoking: " + targetTrackNamedMessageMethod + " id: "+id+" name: "+name+" byteCount: "+bytesCount+" NetworkMetricsManager: "+networkMetricsManager);
              targetTrackNamedMessageMethod.Invoke(networkMetricsManager.GetType().GetProperty("NetworkMetrics", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(networkMetricsManager), new object[] { id, name, (long)bytesCount });
              Debug.LogWarning("Tracking Message");
              if (ForcedServer)
              {
                  //De-Escalate Permissions
                  typeof(NetworkClient).GetProperty("IsServer", BindingFlags.NonPublic|BindingFlags.Instance).SetValue(connectionManager.GetType().GetField("LocalClient", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(connectionManager), false);
                  Debug.LogWarning("De-Escalated NetworkClient Permissions");
              }
          }
      };
    }





}

//Just yoinking the unity.netcode XXHash functions cause I cant be bothered to system.reflections my way in there
static class XXHash
{
  
  public static unsafe uint Hash32(byte* input, int length, uint seed = 0)
  {
    uint num1 = seed + 374761393U;
    if (length >= 16 /*0x10*/)
    {
      uint num2 = (uint) ((int) seed - 1640531535 - 2048144777);
      uint num3 = seed + 2246822519U;
      uint num4 = seed;
      uint num5 = seed - 2654435761U;
      int num6 = length >> 4;
      for (int index = 0; index < num6; ++index)
      {
        uint num7 = *(uint*) input;
        uint num8 = *(uint*) (input + 4);
        uint num9 = *(uint*) (input + 8);
        uint num10 = *(uint*) (input + 12);
        uint num11 = num2 + num7 * 2246822519U;
        num2 = (num11 << 13 | num11 >> 19) * 2654435761U;
        uint num12 = num3 + num8 * 2246822519U;
        num3 = (num12 << 13 | num12 >> 19) * 2654435761U;
        uint num13 = num4 + num9 * 2246822519U;
        num4 = (num13 << 13 | num13 >> 19) * 2654435761U;
        uint num14 = num5 + num10 * 2246822519U;
        num5 = (num14 << 13 | num14 >> 19) * 2654435761U;
        input += 16 /*0x10*/;
      }
      num1 = (uint) (((int) num2 << 1 | (int) (num2 >> 31 /*0x1F*/)) + ((int) num3 << 7 | (int) (num3 >> 25)) + ((int) num4 << 12 | (int) (num4 >> 20)) + ((int) num5 << 18 | (int) (num5 >> 14)));
    }
    uint num15 = num1 + (uint) length;
    for (length &= 15; length >= 4; length -= 4)
    {
      uint num16 = num15 + *(uint*) input * 3266489917U;
      num15 = (uint) (((int) num16 << 17 | (int) (num16 >> 15)) * 668265263);
      input += 4;
    }
    for (; length > 0; --length)
    {
      uint num17 = num15 + (uint) *input * 374761393U;
      num15 = (uint) (((int) num17 << 11 | (int) (num17 >> 21)) * -1640531535);
      ++input;
    }
    uint num18 = (num15 ^ num15 >> 15) * 2246822519U;
    uint num19 = (num18 ^ num18 >> 13) * 3266489917U;
    return num19 ^ num19 >> 16 /*0x10*/;
  }

  
  public static unsafe ulong Hash64(byte* input, int length, uint seed = 0)
  {
    ulong num1 = (ulong) seed + 2870177450012600261UL;
    if (length >= 32 /*0x20*/)
    {
      ulong num2 = (ulong) ((long) seed + -7046029288634856825L + -4417276706812531889L);
      ulong num3 = (ulong) seed + 14029467366897019727UL;
      ulong num4 = (ulong) seed;
      ulong num5 = (ulong) seed - 11400714785074694791UL;
      int num6 = length >> 5;
      for (int index = 0; index < num6; ++index)
      {
        ulong num7 = (ulong) *(long*) input;
        ulong num8 = (ulong) *(long*) (input + 8);
        ulong num9 = (ulong) *(long*) (input + 16 /*0x10*/);
        ulong num10 = (ulong) *(long*) (input + 24);
        ulong num11 = num2 + num7 * 14029467366897019727UL;
        num2 = (num11 << 31 /*0x1F*/ | num11 >> 33) * 11400714785074694791UL;
        ulong num12 = num3 + num8 * 14029467366897019727UL;
        num3 = (num12 << 31 /*0x1F*/ | num12 >> 33) * 11400714785074694791UL;
        ulong num13 = num4 + num9 * 14029467366897019727UL;
        num4 = (num13 << 31 /*0x1F*/ | num13 >> 33) * 11400714785074694791UL;
        ulong num14 = num5 + num10 * 14029467366897019727UL;
        num5 = (num14 << 31 /*0x1F*/ | num14 >> 33) * 11400714785074694791UL;
        input += 32 /*0x20*/;
      }
      ulong num15 = (ulong) (((long) num2 << 1 | (long) (num2 >> 63 /*0x3F*/)) + ((long) num3 << 7 | (long) (num3 >> 57)) + ((long) num4 << 12 | (long) (num4 >> 52)) + ((long) num5 << 18 | (long) (num5 >> 46)));
      ulong num16 = num2 * 14029467366897019727UL;
      ulong num17 = (num16 << 31 /*0x1F*/ | num16 >> 33) * 11400714785074694791UL;
      ulong num18 = (ulong) ((long) (num15 ^ num17) * -7046029288634856825L + -8796714831421723037L);
      ulong num19 = num3 * 14029467366897019727UL;
      ulong num20 = (num19 << 31 /*0x1F*/ | num19 >> 33) * 11400714785074694791UL;
      ulong num21 = (ulong) ((long) (num18 ^ num20) * -7046029288634856825L + -8796714831421723037L);
      ulong num22 = num4 * 14029467366897019727UL;
      ulong num23 = (num22 << 31 /*0x1F*/ | num22 >> 33) * 11400714785074694791UL;
      ulong num24 = (ulong) ((long) (num21 ^ num23) * -7046029288634856825L + -8796714831421723037L);
      ulong num25 = num5 * 14029467366897019727UL;
      ulong num26 = (num25 << 31 /*0x1F*/ | num25 >> 33) * 11400714785074694791UL;
      num1 = (ulong) ((long) (num24 ^ num26) * -7046029288634856825L + -8796714831421723037L);
    }
    ulong num27 = num1 + (ulong) length;
    for (length &= 31 /*0x1F*/; length >= 8; length -= 8)
    {
      ulong num28 = (ulong) (*(long*) input * -4417276706812531889L);
      ulong num29 = (ulong) (((long) num28 << 31 /*0x1F*/ | (long) (num28 >> 33)) * -7046029288634856825L);
      ulong num30 = num27 ^ num29;
      num27 = (ulong) (((long) num30 << 27 | (long) (num30 >> 37)) * -7046029288634856825L + -8796714831421723037L);
      input += 8;
    }
    if (length >= 4)
    {
      ulong num31 = num27 ^ (ulong) *(uint*) input * 11400714785074694791UL;
      num27 = (ulong) (((long) num31 << 23 | (long) (num31 >> 41)) * -4417276706812531889L + 1609587929392839161L);
      input += 4;
      length -= 4;
    }
    for (; length > 0; --length)
    {
      ulong num32 = num27 ^ (ulong) *input * 2870177450012600261UL;
      num27 = (ulong) (((long) num32 << 11 | (long) (num32 >> 53)) * -7046029288634856825L);
      ++input;
    }
    ulong num33 = (num27 ^ num27 >> 33) * 14029467366897019727UL;
    ulong num34 = (num33 ^ num33 >> 29) * 1609587929392839161UL;
    return num34 ^ num34 >> 32 /*0x20*/;
  }

  
  public static unsafe uint Hash32(this byte[] buffer)
  {
    int length = buffer.Length;
    fixed (byte* input = buffer)
      return XXHash.Hash32(input, length);
  }

  
  public static uint Hash32(this string text) => Encoding.UTF8.GetBytes(text).Hash32();

  
  public static uint Hash32(this Type type) => type.FullName.Hash32();

  
  public static uint Hash32<T>() => typeof (T).Hash32();

  
  public static unsafe ulong Hash64(this byte[] buffer)
  {
    int length = buffer.Length;
    fixed (byte* input = buffer)
      return XXHash.Hash64(input, length);
  }

  
  public static ulong Hash64(this string text) => Encoding.UTF8.GetBytes(text).Hash64();

  
  public static ulong Hash64(this Type type) => type.FullName.Hash64();

  
  public static ulong Hash64<T>() => typeof (T).Hash64();
}
