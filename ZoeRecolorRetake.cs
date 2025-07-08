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
// using Mono.WebBrowser;

namespace GayZoe;


[LandfallPlugin]
public class GayZoePluginRetake
{
    public static ComputeShader DefaultTextureTinter;
    public static GameObject tintMenu;
    public static GameObject tintMenuInstance;
    public static GameObject disabledSettingsObj;
    static bool NetworkPrefabInitialized = false;


    public static Dictionary<int, ZRCInstance> recolorInstances = new();

    public static string AssemblyDirectory
    {
        get
        {
            string codeBase = Assembly.GetExecutingAssembly().CodeBase;
            UriBuilder uri = new UriBuilder(codeBase);
            string path = Uri.UnescapeDataString(uri.Path);
            return Path.GetDirectoryName(path);
        }
    }

    static GayZoePluginRetake()
    {
        return;
        Debug.Log("Loading Gay Zoe...");

        string targetZrcBundle = "zoerecolorswindows";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            targetZrcBundle = "zoerecolorswindows";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            targetZrcBundle = "zoerecolorsmacos";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            targetZrcBundle = "zoerecolorslinux";
        }
        else
        {
            Debug.LogError("Could not determine platform type!");
        }

        Debug.Log("Loading asset bundle: " + targetZrcBundle);

        AssetBundle assetBundle = AssetBundle.LoadFromFile(Path.Combine(AssemblyDirectory, targetZrcBundle));
        if (assetBundle == null)
        {
            Debug.LogError("Failed to load AssetBundle!");
            return;
        }

        DefaultTextureTinter = assetBundle.LoadAsset<ComputeShader>("EffectsShader");
        tintMenu = assetBundle.LoadAsset<GameObject>("ColorMenu");


        Debug.LogWarning("Attempting to add ZRC local instance");
        recolorInstances.Add(-1, new ZRCInstance(true, -1));

        GameObject c = new GameObject("GayZoeMenuCanvas");
        c.AddComponent<Canvas>().sortingOrder = 100;
        c.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        c.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        c.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
        c.GetComponent<CanvasScaler>().screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        c.AddComponent<GraphicRaycaster>();
        tintMenuInstance = GameObject.Instantiate(tintMenu);
        tintMenuInstance.transform.SetParent(c.transform);
        tintMenuInstance.AddComponent<ColorMenuController>().renderCanvas = c.GetComponent<Canvas>();

        tintMenuInstance.transform.localPosition = new Vector3(0, 0, 0);
        // c.GetComponent<Canvas>().enabled = false;

        GameObject.DontDestroyOnLoad(c);

        if (!LoadTintPresetsFromDirectory(AssemblyDirectory))
        {
            AddDefaultTintPresets();
        }




        //Patches
        //On Character Start
        //On Outfit Change
        Debug.LogWarning("Setting hooks");


        On.PlayerCharacter.Awake += (On.PlayerCharacter.orig_Awake orig, PlayerCharacter self) =>
        {
            orig(self);
            ColorMenuController.instance.StartCoroutine(DoThingsInAMoment(false));

        };

        FactSystem.SubscribeToFact(SkinManager.EquippedSkinHeadFact, (__) =>
        {
            string s = SceneManager.GetActiveScene().name;
            if (s == "MainMenu" || s.Contains("Intro"))
            {
                return;
            }
            ColorMenuController.instance.StartCoroutine(DoThingsInAMoment(false));
        });

        On.Unity.Netcode.NetworkManager.Initialize += (orig, self, isServer) =>
        {
            orig(self, isServer);
            if (self.CustomMessagingManager == null)
            {
                return;
            }
            CustomMessagingManager msgManager = self.CustomMessagingManager;
            msgManager.RegisterNamedMessageHandler("GayZoeUpdateColor", OnUpdateAndApplyColor);
            msgManager.RegisterNamedMessageHandler("GayZoeUpdateRefsAndColor", OnUpdateRefsAndApplyColor);
            msgManager.RegisterNamedMessageHandler("GayZoeRegisterClient", OnRegisterMe);
            msgManager.RegisterNamedMessageHandler("GayZoeSearchForClients", OnRegisterRequest);
            msgManager.RegisterNamedMessageHandler("GayZoePing", Ping);

            // if (AddToNetworkPrefabListIfNotAlreadyInNetworkPrefabList(NetworkPrefab, self))
            // {
            //     Debug.LogWarning("NetworkPrefab Already in list");
            // }
            orig(self, isServer);
        };

        On.Unity.Netcode.NetworkManager.StartServer += (orig, self) =>
        {
            Debug.LogWarning("[Zoe] On Start Server");
            bool b = orig(self);
            return b;
        };
        On.Unity.Netcode.NetworkManager.StartHost += (orig, self) =>
        {
            Debug.LogWarning("[Zoe] On Start Host");
            bool b = orig(self);
            return b;
        };
        On.Unity.Netcode.NetworkManager.StartClient += (orig, self) =>
        {
            Debug.LogWarning("[Zoe] On Start Client");
            bool b = orig(self);
            return b;
        };

        On.Landfall.Haste.NGOPlayer.OnNetworkSpawn += (orig, self) =>
        {
            orig(self);
            if (self.IsOwner)
            {
                NetworkHackManager.ForceSendMessageToAll("GayZoeSearchForClients", new FastBufferWriter(1, Unity.Collections.Allocator.Temp));
                NetworkHackManager.ForceSendMessageToAll("GayZoeRegisterClient", new FastBufferWriter(1, Unity.Collections.Allocator.Temp));
            }

        };


        On.Landfall.Haste.NGOPlayer.OnNetworkDespawn += (orig, self) =>
        {
            // if (networkifier != null)
            // {
            //     networkifier.NetworkObject.Despawn(true);
            // }
            orig(self);
        };


        //Some special MainMenu code

        NetworkHackManager.Init();

        Debug.Log("Gay Zoe Loaded!");
    }



    static void OnRegisterMe(ulong clientId, FastBufferReader reader)
    {
        if (recolorInstances.ContainsKey((int)clientId))
        {
            Debug.LogWarning("[Gay Zoe] Register request received from an already registered client!");
            return;
        }
        recolorInstances.Add((int)clientId, new ZRCInstance(false, (int)clientId));
    }
    static void OnRegisterRequest(ulong clientId, FastBufferReader reader)
    {
        NetworkHackManager.ForceSendMessage("GayZoeRegisterClient", clientId, new FastBufferWriter(1, Unity.Collections.Allocator.Temp));
    }
    static void OnUpdateRefsAndApplyColor(ulong clientId, FastBufferReader reader)
    {
        reader.ReadValue(out string str);
        ColorPreset preset = new ColorPreset(str);
        if (HasteNetworking.TryGetPlayer(clientId, out NGOPlayer player))
        {
            recolorInstances[(int)clientId].preset = preset;
            recolorInstances[(int)clientId].CurrentEffect = Effects.Tint;
            recolorInstances[(int)clientId].AttemptUpdateReferences(player.RemotePlayer);
            recolorInstances[(int)clientId].ApplyEffect();
        }
        else
        {
            Debug.LogError("[Gay Zoe] Failed to find NGOPlayer with ID: " + clientId);
        }

    }

    static void OnUpdateAndApplyColor(ulong clientId, FastBufferReader reader)
    {
        reader.ReadValue(out string str);
        ColorPreset preset = new ColorPreset(str);
        recolorInstances[(int)clientId].preset = preset;
        recolorInstances[(int)clientId].CurrentEffect = Effects.Tint;
        recolorInstances[(int)clientId].ApplyEffect();
    }

    static void Ping(ulong clientId, FastBufferReader reader)
    {
        Debug.Log("Ping Recieved from id: " + clientId);
    }


    static IEnumerator DoThingsInAMoment(bool onlyNetwork)
    {
        yield return null;
        yield return null;
        yield return null;
        yield return null;
        if (!onlyNetwork && ZRCInstance.localInstance.AttemptUpdateReferences(PlayerCharacter.localPlayer.gameObject))
        {
            ZRCInstance.localInstance.ApplyEffect();
        }
        if (NetworkManager.Singleton == null)
        {
            yield break;
        }
        else
        {
            Debug.LogWarning("Sending GayZoe Update commands with id: " + HasteNetworking.Player.OwnerClientId);
            FastBufferWriter writer = new FastBufferWriter(128, Unity.Collections.Allocator.Temp);
            writer.WriteValue(ZRCInstance.localInstance.preset.SaveString(1));
            foreach (ZRCInstance inst in recolorInstances.Values)
            {
                if (inst.networkId == -1)
                    continue;
                NetworkHackManager.ForceSendMessage("GayZoeUpdateClientRefsAndApplyColor", (ulong)inst.networkId, writer);
            }
            // UpdateMyReferencesRpc(HasteNetworking.Player.OwnerClientId);
            // networkifier.ChangeMeRpc(ZRCInstance.localInstance.preset.ToString(), HasteNetworking.Player.OwnerClientId);
        }

    }

    public static bool AddToNetworkPrefabListIfNotAlreadyInNetworkPrefabList(GameObject prefab, NetworkManager networkManager)
    {

        foreach (NetworkPrefab networkPrefab in networkManager.NetworkConfig.Prefabs.Prefabs)
        {
            if (networkPrefab.Prefab == prefab)
            {
                return false;
            }
        }

        networkManager.AddNetworkPrefab(prefab);

        return true;
    }

    [ConsoleCommand]
    public static void LogNetworkConfigDump()
    {
        NGOPlayer localPlayer = HasteNetworking.Player;

        if (localPlayer == null)
        {
            Debug.LogError("Local NGOPlayer wasnt found! Make sure multiplayer is enabled!");
            return;
        }

        Debug.Log("[Zoe] Network config dump");
        Debug.Log("Hash: " + localPlayer.NetworkManager.NetworkConfig.GetConfig());
        Debug.Log("Networm Transport: " + localPlayer.NetworkManager.NetworkConfig.NetworkTransport);
        Debug.Log("Protocol Version" + localPlayer.NetworkManager.NetworkConfig.ProtocolVersion);
        Debug.Log("Force Same Prefabs: " + localPlayer.NetworkManager.NetworkConfig.ForceSamePrefabs);
        Debug.Log("Prefab Keys:");
        foreach (KeyValuePair<uint, NetworkPrefab> keyValuePair in (IEnumerable<KeyValuePair<uint, NetworkPrefab>>)localPlayer.NetworkManager.NetworkConfig.Prefabs.NetworkPrefabOverrideLinks.OrderBy<KeyValuePair<uint, NetworkPrefab>, uint>((Func<KeyValuePair<uint, NetworkPrefab>, uint>)(x => x.Key)))
        {
            Debug.Log(keyValuePair.Key);
        }
        Debug.Log("Tick Rate: " + localPlayer.NetworkManager.NetworkConfig.TickRate);
        Debug.Log("Connection Approval: " + localPlayer.NetworkManager.NetworkConfig.ConnectionApproval);
        Debug.Log("Enable Scene Management: " + localPlayer.NetworkManager.NetworkConfig.EnableSceneManagement);
        Debug.Log("Ensure Network Variable Length Safety: " + localPlayer.NetworkManager.NetworkConfig.EnsureNetworkVariableLengthSafety);
        Debug.Log("Rpc Hash Size" + localPlayer.NetworkManager.NetworkConfig.RpcHashSize);


        Debug.Log("[Zoe] End config dump\n");

    }

    public static void AddDefaultTintPresets()
    {
        AddPresetFromString("Agender-Aromantic, main, 0, 0, 0, accent, 65, 66, 67, highlight, 255, 255, 255, hair, 72, 255, 72, 0");
        AddPresetFromString("Asexual Pride, main, 112, 13, 154, accent, 142, 143, 146, highlight, 255, 255, 255, hair, 45, 25, 24, 0");
        AddPresetFromString("Ava!, main, 227, 182, 48, accent, 28, 27, 12, highlight, 156, 123, 224, hair, 56, 32, 4, 0");
        AddPresetFromString("Bisexual Pride, main, 155, 48, 75, accent, 53, 60, 118, highlight, 91, 82, 111, hair, 91, 82, 111, 0");
        AddPresetFromString("Captain!, main, 255, 255, 255, accent, 209, 0, 217, highlight, 211, 161, 0, hair, 112, 28, 0, 0");
        AddPresetFromString("Gan! (or dalil), main, 0, 190, 236, accent, 255, 117, 0, highlight, 0, 45, 89, hair, 255, 41, 48, 0");
        AddPresetFromString("Gay Pride, main, 35, 103, 77, accent, 185, 255, 209, highlight, 73, 92, 211, hair, 255, 255, 255, 0");
        AddPresetFromString("Grandma!, main, 168, 70, 81, accent, 0, 155, 192, highlight, 255, 152, 0, hair, 98, 94, 96, 0");
        AddPresetFromString("Lesbian Pride, main, 255, 104, 60, accent, 255, 149, 115, highlight, 210, 130, 180, hair, 255, 255, 255, 0");
        AddPresetFromString("Niada!, main, 255, 255, 255, accent, 33, 129, 255, highlight, 178, 0, 105, hair, 29, 40, 42, 0");
        AddPresetFromString("Non-Binary, main, 141, 128, 165, accent, 255, 255, 255, highlight, 255, 255, 13, hair, 53, 30, 18, 0");
        AddPresetFromString("Pansexual Pride, main, 19, 212, 255, accent, 249, 235, 49, highlight, 255, 255, 255, hair, 255, 147, 248, 0");
        AddPresetFromString("Riza!, main, 63, 164, 108, accent, 255, 255, 255, highlight, 105, 110, 105, hair, 161, 255, 255, 0");
        AddPresetFromString("Trans Pride, main, 78, 208, 255, accent, 255, 205, 254, highlight, 255, 255, 255, hair, 139, 109, 117, 0");
        AddPresetFromString("Wraith!, main, 9, 53, 48, accent, 65, 183, 158, highlight, 140, 157, 159, hair, 105, 67, 36, 0");
    }

    public static bool LoadTintPresetsFromDirectory(string directory)
    {
        bool hasPresets = false;

        foreach (string path in Directory.GetFiles(directory))
        {
            if (path.EndsWith(".color"))
            {
                hasPresets = true;
                try
                {
                    using (StreamReader sr = new StreamReader(path))
                    {
                        hasPresets = true;
                        Color main;
                        Color accent;
                        Color highlight;
                        Color hair;
                        Color rimLight = new Color(1, 1, 1, 1);
                        string title;
                        string Contents = sr.ReadToEnd();
                        Contents.Replace("\n", "");     //Preset_Name, main, 0-255, 0-255, 0-255, accent, 0-255, 0-255, 0-255, highlight, 0-255, 0-255, 0-255, rimlight, 255, 255, 255, (0,1)
                        string[] Properties = Contents.Split(",");
                        title = Properties[0];
                        main = new Color(float.Parse(Properties[2]) / 255, float.Parse(Properties[3]) / 255, float.Parse(Properties[4]) / 255, 1);
                        accent = new Color(float.Parse(Properties[6]) / 255, float.Parse(Properties[7]) / 255, float.Parse(Properties[8]) / 255, 1);
                        highlight = new Color(float.Parse(Properties[10]) / 255, float.Parse(Properties[11]) / 255, float.Parse(Properties[12]) / 255, 1);

                        hair = new Color(float.Parse(Properties[14]) / 255, float.Parse(Properties[15]) / 255, float.Parse(Properties[16]) / 255, 1);

                        if (Properties[17] == "rimlight")
                        {
                            rimLight = new Color(float.Parse(Properties[18]) / 255, float.Parse(Properties[19]) / 255, float.Parse(Properties[20]) / 255, 1);
                        }

                        ColorMenuController.instance.presets.Add(new ColorPreset(main, accent, highlight, hair, rimLight, title));
                        if (int.Parse(Properties[Properties.Length - 1]) == 1)
                        {
                            ColorMenuController.instance.currentPreset = ColorMenuController.instance.presets.Count - 1;
                            // ApplyEffect();
                        }
                        Debug.Log("Found color preset! " + title);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("Failed to assign color preset: " + path + "\n");
                    Debug.LogError(e.Message + " " + e.StackTrace);
                }
                File.Delete(path);
            }
        }


        return hasPresets;
    }

    public static void AddPresetFromString(string str)
    {
        Color main;
        Color accent;
        Color highlight;
        Color hair;
        string title;
        string Contents = str;
        Contents.Replace("\n", "");     //Preset_Name, main, 0-255, 0-255, 0-255, accent, 0-255, 0-255, 0-255, highlight, 0-255, 0-255, 0-255, (0,1)
        string[] Properties = Contents.Split(",");
        title = Properties[0];
        main = new Color(float.Parse(Properties[2]) / 255, float.Parse(Properties[3]) / 255, float.Parse(Properties[4]) / 255, 1);
        accent = new Color(float.Parse(Properties[6]) / 255, float.Parse(Properties[7]) / 255, float.Parse(Properties[8]) / 255, 1);
        highlight = new Color(float.Parse(Properties[10]) / 255, float.Parse(Properties[11]) / 255, float.Parse(Properties[12]) / 255, 1);
        hair = new Color(float.Parse(Properties[14]) / 255, float.Parse(Properties[15]) / 255, float.Parse(Properties[16]) / 255, 1);


        ColorMenuController.instance.presets.Add(new ColorPreset(main, accent, highlight, hair, new Color(1, 1, 1, 1), title));
        Debug.Log("Added Preset: " + ColorMenuController.instance.presets[ColorMenuController.instance.presets.Count - 1]);
    }

    public static void Back()
    {
        disabledSettingsObj.SetActive(true);
    }


    [ConsoleCommand]
    public static void RefreshMyTexturesAndMaterials()
    {
        ZRCInstance.localInstance.AttemptUpdateReferences(PlayerCharacter.localPlayer.gameObject);
    }

    [ConsoleCommand]
    public static void Ping()
    {
        NetworkHackManager.ForceSendMessageToAll("GayZoePing", new FastBufferWriter(1, Unity.Collections.Allocator.Temp));
    }

}

public class ZRCInstance
{
    public static ZRCInstance localInstance;
    public Texture HairTex;
    public Texture HatTex;
    public Texture BodyTex;
    public Texture FlexTexHead;
    public Texture FlexTexBody;
    public ColorPreset preset;


    public Effects CurrentEffect = Effects.Tint;

    bool isLocal = false;

    public int networkId;

    RenderTexture[] resultTextures = new RenderTexture[5];

    List<Material> HatMats = new();
    List<Material> HairMats = new();
    List<Material> BodyMats = new();
    List<Material> FlexMatsHead = new();
    List<Material> FlexMatsBody = new();

    public ZRCInstance(bool isLocal, int networkId)
    {
        this.isLocal = isLocal;

        this.networkId = networkId;
        Debug.Log("[Gay Zoe] Adding ZRCInstance with ID: " + networkId);

        if (isLocal)
        {
            localInstance = this;
        }

        for (int i = 0; i < resultTextures.Length; i++)
        {
            resultTextures[i] = new RenderTexture(2048, 2048, 0);
            resultTextures[i].enableRandomWrite = true;
        }
        //Hat
        //Hair
        //Body
        //FlexHead
        //FlexBody

        Application.quitting += () =>
        {
            for (int i = 0; i < resultTextures.Length; i++)
            {
                resultTextures[i].Release();
            }
        };
    }

    string realRawCourierShitLikeGoatShitRawSickAfDataLikeLookAtThisAndTellMeHowGnarlyItIs = """
    DEFAULT: 
        Body: Player/Visual/Courier_Retake/Courier/Armature/Hip/Meshes/Body
        Hair: Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Hair
        Hat: Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/Hat

    Crispy:
        Body: Player/Visual/Courier_Retake/Courier/Armature/Hip/Meshes/Body
        Hair: Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Hair
        Hat: Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/Hat

    Little Sister:
        Body: Player/Visual/Courier_Retake/Courier/Armature/Hip/Meshes/Body
        Hair: Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Hair
        Hat: Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/Hat

    Supersonic Zoe:
        Body: Player/Visual/Courier_Retake/Courier/Armature/Hip/Meshes/Body
        Hair: Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Hair
        Hat: Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/Hat
        
    Zoe the Shadow:
        Body: Player/Visual/Courier_Retake/Courier/Armature/Hip/Meshes/Body
        Hair: Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Hair
        Hat: Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/Hat

        

    Totally Accurate Zoe:													Notes: deffinetly need to look at the material spread for these
        Body{4}: Player/Visual/Courier_Retake/Courier/Armature/Hip/Meshes/wobblerBody_low
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Meshes/shoes_low
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Meshes/shoes_low.001
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Meshes/drawstring_low
        Hair{3}: Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/bangs_low
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/brows_low
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/sidebangs_low
        Hat{1}: Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/wobblerhood_low

        Mats{1}: M_Courier_Wobbler											Notes: uhhhhh im pretty sure the wobbler can just do anything
            

    Weeboh:
        Body{6}: Player/Visual/Courier_Retake/Courier/Armature/Hip/Meshes/glovesinner_low.001
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Meshes/body_low
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Meshes/fluff_low
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Meshes/scarfmain_low
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Meshes/body_low
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Meshes/glovesinner_low.001
        Hair: Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Hair
        Hat{3}: Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/head_low
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/topfluff_low
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/antlers_low

        Mats{4}: M_Courier_BaseColorweeboh
            M_Courier_Weeboh
            M_Courier_Hairweeboh
            M_Courier_Wobbler											Notes: WTF????? WHY? HUH? WHAT? WHY IS THE WOBBLER HERE?

    Zoe 64:
        Body{5}: Player/Visual/Courier_Retake/Courier/Armature/Hip/Cube.001
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Cube
            Player/Visual/Courier_Retake/Courier/Armature/Hip/glovesinner_low
            Player/Visual/Courier_Retake/Courier/Armature/Hip/scarfmain_low
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Cube.006
        Hair{3}: Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/bangs_low
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/eyelashes_low
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/hairback_low
        Hat{2}:  Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/hairbackhead_low	Notes:???? might be part of head idk
            Player/Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/hat_low	

        Mats{x}: M_Courier_BaseColor 64
            M_Courier_BaseColor
""";


    string[] Zoe64Body = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Cube.001",
        "Visual/Courier_Retake/Courier/Armature/Hip/Cube",
        "Visual/Courier_Retake/Courier/Armature/Hip/glovesinner_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/scarfmain_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Cube.006"
    };

    string[] Zoe64Hair = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/bangs_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/eyelashes_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/hairback_low"
    };

    string[] Zoe64Hat = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/hat_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/Cube.002"
    };



    string[] WeebohBody = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/glovesinner_low.001",
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/body_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/fluff_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/scarfmain_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/body_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/glovesinner_low"
    };

    string[] WeebohHair = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Hair"
    };

    string[] WeebohHat = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/head_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/topfluff_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/antlers_low"
    };


    string[] TabsBody = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/wobblerBody_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/shoes_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/shoes_low.001",
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/drawstring_low"
    };

    string[] TabsHair = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/bangs_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/brows_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/sidebangs_low"
    };

    string[] TabsHat = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/wobblerhood_low"
    };



    string[] ShadowBody = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/Body"
    };

    string[] ShadowHair = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Hair"
    };

    string[] ShadowHat = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/Hat"
    };



    string[] SuperBody = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/Body"
    };

    string[] SuperHair = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Hair"
    };

    string[] SuperHat = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/Hat"
    };


    string[] GreenBody = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/Body"
    };

    string[] GreenHair = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Hair"
    };

    string[] GreenHat = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/Hat"
    };



    string[] CrispyBody = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/Body"
    };

    string[] CrispyHair = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Hair"
    };

    string[] CrispyHat = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/Hat"
    };




    string[] DefaultBody = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/Body"
    };

    string[] DefaultHair = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Hair"
    };

    string[] DefaultHat = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Head/Hat"
    };


    string[] FlopsyBody = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/neckRuffle_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/clownbody",
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/clownbody_low.001",
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/clownshoes_low"
    };
    string[] FlopsyHair = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Hair",
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Head"
    };
    string[] FlopsyHat = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/clownhat_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Head"
    };

    string[] DarkFlopsyBody = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/neckRuffle_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/clownbody",
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/clownbody_low.001",
        "Visual/Courier_Retake/Courier/Armature/Hip/Meshes/clownshoes_low"
    };
    string[] DarkFlopsyHair = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Hair",
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Head"
    };
    string[] DarkFlopsyHat = new string[]{
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/clownhat_low",
        "Visual/Courier_Retake/Courier/Armature/Hip/Spine_1/Spine_2/Spine_3/Neck/Meshes/Head"
    };



   





    public bool AttemptUpdateReferences(GameObject root)
    {
        Debug.LogWarning(root);
        Debug.LogWarning(root.transform.GetTransformPath());

        foreach (Material mat in HatMats)
        {
            UnityEngine.Object.Destroy(mat);
        }
        HatMats = new();
        HatTex = null;

        foreach (Material mat in BodyMats)
        {
            UnityEngine.Object.Destroy(mat);
        }
        BodyMats = new();
        BodyTex = null;

        foreach (Material mat in HairMats)
        {
            UnityEngine.Object.Destroy(mat);
        }
        HairMats = new();
        HairTex = null;

        foreach (Material mat in FlexMatsBody)
        {
            UnityEngine.Object.Destroy(mat);
        }
        FlexMatsBody = new();
        FlexTexBody = null;

        foreach (Material mat in FlexMatsHead)
        {
            UnityEngine.Object.Destroy(mat);
        }
        FlexMatsHead = new();
        FlexTexHead = null;


        SkinManager.Skin BodySkin = SkinManager.Skin.Default;
        SkinManager.Skin HeadSkin = SkinManager.Skin.Default;
        if (isLocal)
        {
            BodySkin = SkinManager.GetBodySkinFromFacts();
            HeadSkin = SkinManager.GetHeadSkinFromFacts();
        }
        else
        {
            NGOPlayer me;
            if (HasteNetworking.TryGetPlayer((ulong) networkId, out me))
            {
                BodySkin = me.BodySkin;
                HeadSkin = me.HeadSkin;
            }
            else
            {
                Debug.LogError("Something weally weally bad is happening");
                return false;
            }
        }

        if (true)
        {


            try
            {
                switch (HeadSkin)
                {
                    case SkinManager.Skin.Zoe64:
                        foreach (string path in Zoe64Hair)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_Hair 64"))
                                {
                                    if (HairTex == null)
                                    {
                                        HairTex = m.mainTexture;
                                    }


                                    HairMats.Add(m);
                                }
                            }

                        }
                        foreach (string path in Zoe64Hat)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_BaseColor 64") || m.name.Contains("M_Courier_BaseColor"))
                                {
                                    if (HatTex == null)
                                    {
                                        HatTex = m.mainTexture;
                                    }
                                    HatMats.Add(m);
                                }
                            }
                        }
                        break;

                    case SkinManager.Skin.Weeboh:
                        foreach (string path in WeebohHair)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_Hairweeboh"))
                                {
                                    if (HairTex == null)
                                    {
                                        HairTex = m.mainTexture;
                                    }

                                    HairMats.Add(m);
                                }
                            }
                        }

                        foreach (string path in WeebohHat)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_Weeboh"))
                                {
                                    if (HatTex == null)
                                    {
                                        HatTex = m.mainTexture;
                                    }
                                    HatMats.Add(m);
                                }
                                else if (m.name.Contains("M_Courier_Wobbler"))
                                {
                                    if (FlexTexHead == null)
                                    {
                                        FlexTexHead = m.mainTexture;
                                    }

                                    FlexMatsHead.Add(m);
                                }
                            }
                        }
                        break;

                    case SkinManager.Skin.Wobbler:
                        foreach (string path in TabsHair)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_Wobbler"))
                                {
                                    if (HairTex == null)
                                    {
                                        HairTex = m.mainTexture;
                                    }

                                    HairMats.Add(m);
                                }
                            }
                        }

                        foreach (string path in TabsHat)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_Wobbler"))
                                {
                                    if (HatTex == null)
                                    {
                                        HatTex = m.mainTexture;
                                    }
                                    HatMats.Add(m);
                                }
                            }
                        }
                        break;

                    case SkinManager.Skin.Shadow:
                        foreach (string path in ShadowHair)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_DarkZoe_Hair"))
                                {
                                    if (HairTex == null)
                                    {
                                        HairTex = m.mainTexture;
                                    }

                                    HairMats.Add(m);
                                }
                            }
                        }

                        foreach (string path in ShadowHat)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_DarkZoe_clothes") || m.name.Contains("M_Courier_BaseColor"))
                                {
                                    if (HatTex == null)
                                    {
                                        HatTex = m.mainTexture;
                                    }
                                    HatMats.Add(m);
                                }
                            }
                        }
                        break;
                    case SkinManager.Skin.Blue:
                        foreach (string path in SuperHair)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_BlueZoe_Hair"))
                                {
                                    if (HairTex == null)
                                    {
                                        HairTex = m.mainTexture;
                                    }

                                    HairMats.Add(m);
                                }
                            }
                        }

                        foreach (string path in SuperHat)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_BlueZoe_clothes") || m.name.Contains("M_Courier_BaseColor"))
                                {
                                    if (HatTex == null)
                                    {
                                        HatTex = m.mainTexture;
                                    }
                                    HatMats.Add(m);
                                }
                            }
                        }
                        break;

                    case SkinManager.Skin.Green:
                        foreach (string path in GreenHair)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_GreenZoe_Hair"))
                                {
                                    if (HairTex == null)
                                    {
                                        HairTex = m.mainTexture;
                                    }

                                    HairMats.Add(m);
                                }
                            }
                        }

                        foreach (string path in GreenHat)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_GreenZoe_clothes") || m.name.Contains("M_Courier_BaseColor"))
                                {
                                    if (HatTex == null)
                                    {
                                        HatTex = m.mainTexture;
                                    }
                                    HatMats.Add(m);
                                }
                            }
                        }
                        break;

                    case SkinManager.Skin.Crispy:
                        foreach (string path in CrispyHair)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_FRIEDCourier_Hair Variant"))
                                {
                                    if (HairTex == null)
                                    {
                                        HairTex = m.mainTexture;
                                    }

                                    HairMats.Add(m);
                                }
                            }
                        }

                        foreach (string path in CrispyHat)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_FRIEDCourier_BaseColor Variant"))
                                {
                                    if (HatTex == null)
                                    {
                                        HatTex = m.mainTexture;
                                    }
                                    HatMats.Add(m);
                                }
                            }
                        }
                        break;

                    case SkinManager.Skin.Default:
                        foreach (string path in DefaultHair)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_Hair"))
                                {
                                    if (HairTex == null)
                                    {
                                        HairTex = m.mainTexture;
                                    }

                                    HairMats.Add(m);
                                }
                            }
                        }

                        foreach (string path in DefaultHat)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_BaseColor"))
                                {
                                    if (HatTex == null)
                                    {
                                        HatTex = m.mainTexture;
                                    }
                                    HatMats.Add(m);
                                }
                            }
                        }
                        break;
                    
                    case SkinManager.Skin.Clown:
                        foreach (string path in FlopsyHair)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_ClownZoe_Hair"))
                                {
                                    if (HairTex == null)
                                    {
                                        HairTex = m.mainTexture;
                                    }

                                    HairMats.Add(m);
                                }
                            }
                        }

                        foreach (string path in FlopsyHat)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_BaseColor")||m.name.Contains("M_ClownZoe_ClownParts"))
                                {
                                    if (HatTex == null)
                                    {
                                        HatTex = m.mainTexture;
                                    }
                                    HatMats.Add(m);
                                }
                            }
                        }
                        break;

                    case SkinManager.Skin.DarkClown:
                        foreach (string path in DarkFlopsyHair)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_Hair"))
                                {
                                    if (HairTex == null)
                                    {
                                        HairTex = m.mainTexture;
                                    }

                                    HairMats.Add(m);
                                }
                            }
                        }

                        foreach (string path in DarkFlopsyHat)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_BaseColordarkclown")||m.name.Contains("M_Courier_BaseColordarkclownNOSEONLY"))
                                {
                                    if (HatTex == null)
                                    {
                                        HatTex = m.mainTexture;
                                    }
                                    HatMats.Add(m);
                                }
                            }
                        }
                        break;


                    default:
                        Debug.LogError(HeadSkin + " Head skin has no reference implementation!");
                        return false;

                }

                switch (BodySkin)
                {
                    case SkinManager.Skin.Zoe64:
                        foreach (string path in Zoe64Body)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_BaseColor 64") || m.name.Contains("M_Courier_BaseColor"))
                                {
                                    if (BodyTex == null)
                                    {
                                        BodyTex = m.mainTexture;
                                    }
                                    BodyMats.Add(m);
                                }
                            }
                        }
                        break;

                    case SkinManager.Skin.Weeboh:
                        foreach (string path in WeebohBody)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_Weeboh"))
                                {
                                    if (BodyTex == null)
                                    {
                                        BodyTex = m.mainTexture;
                                    }
                                    BodyMats.Add(m);
                                }
                                else if (m.name.Contains("M_Courier_BaseColorweeboh"))
                                {
                                    if (FlexTexBody == null)
                                    {
                                        FlexTexBody = m.mainTexture;
                                    }
                                    FlexMatsBody.Add(m);
                                }

                            }
                        }
                        break;
                    case SkinManager.Skin.Wobbler:

                        foreach (string path in TabsBody)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_Wobbler"))
                                {
                                    if (BodyTex == null)
                                    {
                                        BodyTex = m.mainTexture;
                                    }
                                    BodyMats.Add(m);
                                }
                            }
                        }


                        break;

                    case SkinManager.Skin.Shadow:
                        foreach (string path in ShadowBody)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_DarkZoe_clothes"))
                                {
                                    if (BodyTex == null)
                                    {
                                        BodyTex = m.mainTexture;
                                    }
                                    BodyMats.Add(m);
                                }
                            }
                        }


                        break;

                    case SkinManager.Skin.Blue:
                        foreach (string path in SuperBody)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_BlueZoe_clothes"))
                                {
                                    if (BodyTex == null)
                                    {
                                        BodyTex = m.mainTexture;
                                    }
                                    BodyMats.Add(m);
                                }
                            }
                        }


                        break;
                    case SkinManager.Skin.Green:
                        foreach (string path in GreenBody)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_GreenZoe_clothes"))
                                {
                                    if (BodyTex == null)
                                    {
                                        BodyTex = m.mainTexture;
                                    }
                                    BodyMats.Add(m);
                                }
                            }
                        }


                        break;

                    case SkinManager.Skin.Crispy:
                        foreach (string path in CrispyBody)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_FRIEDCourier_BaseColor Variant"))
                                {
                                    if (BodyTex == null)
                                    {
                                        BodyTex = m.mainTexture;
                                    }
                                    BodyMats.Add(m);
                                }
                            }
                        }


                        break;

                    case SkinManager.Skin.Default:
                        foreach (string path in DefaultBody)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_BaseColor"))
                                {
                                    if (BodyTex == null)
                                    {
                                        BodyTex = m.mainTexture;
                                    }
                                    BodyMats.Add(m);
                                }
                            }
                        }


                        break;

                    case SkinManager.Skin.Clown:
                        foreach (string path in FlopsyBody)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_BaseColor"))
                                {
                                    if (BodyTex == null)
                                    {
                                        BodyTex = m.mainTexture;
                                    }
                                    BodyMats.Add(m);
                                }
                                if (m.name.Contains("M_ClownZoe_ClownParts"))
                                {
                                    if (FlexTexBody == null)
                                    {
                                        FlexTexBody = m.mainTexture;
                                    }
                                    FlexMatsBody.Add(m);
                                }
                            }
                        }


                        break;
                    case SkinManager.Skin.DarkClown:
                        foreach (string path in DarkFlopsyBody)
                        {
                            Material[] mats = root.transform.Find(path).GetComponent<Renderer>().materials;
                            foreach (Material m in mats)
                            {
                                if (m.name.Contains("M_Courier_BaseColordarkclownALTCOLOR")||m.name.Contains("M_Courier_darkclownbaseALTCOLOR")||m.name.Contains("M_Courier_BaseColordarkclown")||m.name.Contains("M_Courier_darkclownbase"))
                                {
                                    if (BodyTex == null)
                                    {
                                        // Debug.LogWarning(m.name);
                                        BodyTex = m.mainTexture;
                                    }
                                    BodyMats.Add(m);
                                }
                            }
                        }


                        break;
                    default:
                        Debug.LogError(BodySkin + " Head skin has no reference implementation!");
                        return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }

            /*
            Transform bodyTrans = PlayerCharacter.localPlayer.transform.FindDeepChild("Body");
            Transform hairTrans = PlayerCharacter.localPlayer.transform.FindDeepChild("Hair");
            Transform hatTrans = PlayerCharacter.localPlayer.transform.FindDeepChild("Hat");
            Debug.Log("Body Transform: " + bodyTrans.GetTransformPath() + " Hair Transform: " + hairTrans.GetTransformPath() + " Hat Transform: " + hatTrans.GetTransformPath());

            if (bodyTrans != null && hairTrans != null && hatTrans != null)
            {
                HatMats.AddRange(hatTrans.GetComponent<MeshRenderer>().materials);
                HairMats.AddRange(hairTrans.GetComponent<MeshRenderer>().materials);
                BodyMats.Add(bodyTrans.GetComponent<MeshRenderer>().materials[0]);

                HatTex = HatMats[0].mainTexture;
                HairTex = HairMats[0].mainTexture;
                BodyTex = BodyMats[0].mainTexture;
            }
            else
            {
                Debug.LogError("Couldnt find transforms on local player! Body Transform: " + bodyTrans + " Hair Transform: " + hairTrans + " Hat Transform: " + hatTrans);
                return false;
            }
            */

        }

        return true;
    }

    public void ApplyEffect()
    {
        if (CurrentEffect == Effects.Tint)
        {

            if (preset == null)
            {
                preset = ColorMenuController.instance.presets[ColorMenuController.instance.currentPreset];
            }
            //Copy Textures
            Graphics.Blit(HatTex, resultTextures[0]);
            Graphics.Blit(HairTex, resultTextures[1]);
            Graphics.Blit(BodyTex, resultTextures[2]);

            if (FlexTexHead != null)
            {
                Graphics.Blit(FlexTexHead, resultTextures[3]);
            }
            if (FlexTexBody != null)
            {
                Graphics.Blit(FlexTexBody, resultTextures[4]);
            }


            ComputeShader shader = GayZoePluginRetake.DefaultTextureTinter;

            SkinManager.Skin HeadSkin = SkinManager.GetHeadSkinFromFacts();


            if (HeadSkin == SkinManager.Skin.Zoe64)
            {

                shader.SetTexture(0, "_Result", resultTextures[0]);
                shader.SetTexture(1, "_Result", resultTextures[1]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);
                shader.Dispatch(1, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);



                foreach (Material mat in HatMats)
                {
                    mat.mainTexture = resultTextures[0];
                }


                foreach (Material mat in HairMats)
                {
                    mat.mainTexture = resultTextures[1];
                }
            }
            else if (HeadSkin == SkinManager.Skin.Weeboh)
            {
                shader.SetTexture(0, "_Result", resultTextures[0]);
                shader.SetTexture(1, "_Result", resultTextures[1]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);
                shader.Dispatch(1, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);



                foreach (Material mat in HatMats)
                {
                    mat.mainTexture = resultTextures[0];
                }


                foreach (Material mat in HairMats)
                {
                    mat.mainTexture = resultTextures[1];
                }



                //Do FlexTexHat?
            }
            else if (HeadSkin == SkinManager.Skin.Wobbler)
            {
                shader.SetTexture(0, "_Result", resultTextures[0]);
                shader.SetTexture(1, "_Result", resultTextures[1]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);
                shader.Dispatch(1, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);



                foreach (Material mat in HatMats)
                {
                    mat.mainTexture = resultTextures[0];
                }


                foreach (Material mat in HairMats)
                {
                    mat.mainTexture = resultTextures[1];
                }
            }
            else if (HeadSkin == SkinManager.Skin.Shadow)
            {
                shader.SetTexture(0, "_Result", resultTextures[0]);
                shader.SetTexture(1, "_Result", resultTextures[1]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);
                shader.Dispatch(1, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);



                foreach (Material mat in HatMats)
                {
                    mat.mainTexture = resultTextures[0];
                }


                foreach (Material mat in HairMats)
                {
                    mat.mainTexture = resultTextures[1];
                }
            }
            else if (HeadSkin == SkinManager.Skin.Blue)
            {
                shader.SetTexture(0, "_Result", resultTextures[0]);
                shader.SetTexture(1, "_Result", resultTextures[1]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);
                shader.Dispatch(1, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);



                foreach (Material mat in HatMats)
                {
                    mat.mainTexture = resultTextures[0];
                }


                foreach (Material mat in HairMats)
                {
                    mat.mainTexture = resultTextures[1];
                }
            }
            else if (HeadSkin == SkinManager.Skin.Green)
            {
                shader.SetTexture(0, "_Result", resultTextures[0]);
                shader.SetTexture(1, "_Result", resultTextures[1]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);
                shader.Dispatch(1, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);



                foreach (Material mat in HatMats)
                {
                    mat.mainTexture = resultTextures[0];
                }


                foreach (Material mat in HairMats)
                {
                    mat.mainTexture = resultTextures[1];
                }
            }
            else if (HeadSkin == SkinManager.Skin.Crispy)
            {
                shader.SetTexture(0, "_Result", resultTextures[0]);
                shader.SetTexture(1, "_Result", resultTextures[1]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);
                shader.Dispatch(1, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);



                foreach (Material mat in HatMats)
                {
                    mat.mainTexture = resultTextures[0];
                }


                foreach (Material mat in HairMats)
                {
                    mat.mainTexture = resultTextures[1];
                }
            }
            else if (HeadSkin == SkinManager.Skin.Default)
            {
                shader.SetTexture(0, "_Result", resultTextures[0]);
                shader.SetTexture(1, "_Result", resultTextures[1]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);
                shader.Dispatch(1, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);



                foreach (Material mat in HatMats)
                {
                    mat.mainTexture = resultTextures[0];
                }


                foreach (Material mat in HairMats)
                {
                    mat.mainTexture = resultTextures[1];
                }
            }else if (HeadSkin == SkinManager.Skin.Clown)
            {
                shader.SetTexture(0, "_Result", resultTextures[0]);
                shader.SetTexture(1, "_Result", resultTextures[1]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);
                shader.Dispatch(1, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);



                foreach (Material mat in HatMats)
                {
                    mat.mainTexture = resultTextures[0];
                }


                foreach (Material mat in HairMats)
                {
                    mat.mainTexture = resultTextures[1];
                }
            }else if (HeadSkin == SkinManager.Skin.DarkClown)
            {
                shader.SetTexture(0, "_Result", resultTextures[0]);
                shader.SetTexture(1, "_Result", resultTextures[1]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);
                shader.Dispatch(1, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);



                foreach (Material mat in HatMats)
                {
                    mat.mainTexture = resultTextures[0];
                }


                foreach (Material mat in HairMats)
                {
                    mat.mainTexture = resultTextures[1];
                }
            } 





            SkinManager.Skin BodySkin = SkinManager.GetBodySkinFromFacts();

            if (BodySkin == SkinManager.Skin.Zoe64)
            {
                shader.SetTexture(0, "_Result", resultTextures[2]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);

                foreach (Material mat in BodyMats)
                {
                    mat.mainTexture = resultTextures[2];
                }
            }
            else if (BodySkin == SkinManager.Skin.Weeboh)
            {
                shader.SetTexture(0, "_Result", resultTextures[2]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);

                shader.SetTexture(0, "_Result", resultTextures[4]);
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);

                foreach (Material mat in BodyMats)
                {
                    mat.mainTexture = resultTextures[2];
                }

                foreach (Material mat in FlexMatsBody)
                {
                    mat.mainTexture = resultTextures[4];
                }
            }
            else if (BodySkin == SkinManager.Skin.Wobbler)
            {
                shader.SetTexture(0, "_Result", resultTextures[2]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);

                foreach (Material mat in BodyMats)
                {
                    mat.mainTexture = resultTextures[2];
                }
            }
            else if (BodySkin == SkinManager.Skin.Shadow)
            {
                shader.SetTexture(0, "_Result", resultTextures[2]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);

                foreach (Material mat in BodyMats)
                {
                    mat.mainTexture = resultTextures[2];
                }
            }
            else if (BodySkin == SkinManager.Skin.Blue)
            {
                shader.SetTexture(0, "_Result", resultTextures[2]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);

                foreach (Material mat in BodyMats)
                {
                    mat.mainTexture = resultTextures[2];
                }
            }
            else if (BodySkin == SkinManager.Skin.Green)
            {
                shader.SetTexture(0, "_Result", resultTextures[2]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);

                foreach (Material mat in BodyMats)
                {
                    mat.mainTexture = resultTextures[2];
                }
            }
            else if (BodySkin == SkinManager.Skin.Crispy)
            {
                shader.SetTexture(0, "_Result", resultTextures[2]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);

                foreach (Material mat in BodyMats)
                {
                    mat.mainTexture = resultTextures[2];
                }
            }
            else if (BodySkin == SkinManager.Skin.Default)
            {
                shader.SetTexture(0, "_Result", resultTextures[2]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);

                foreach (Material mat in BodyMats)
                {
                    mat.mainTexture = resultTextures[2];
                }
            }
            else if (BodySkin == SkinManager.Skin.Clown)
            {
                shader.SetTexture(0, "_Result", resultTextures[2]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);

                foreach (Material mat in BodyMats)
                {
                    mat.mainTexture = resultTextures[2];
                }

                shader.SetTexture(0, "_Result", resultTextures[4]);
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);
                foreach (Material mat in FlexMatsBody)
                {
                    mat.mainTexture = resultTextures[4];
                }
            }
            else if (BodySkin == SkinManager.Skin.DarkClown)
            {
                shader.SetTexture(0, "_Result", resultTextures[2]);

                shader.SetFloats("_Tint", preset.mainColor.ToArray());
                shader.SetFloats("_Accent", preset.accentColor.ToArray());
                shader.SetFloats("_Highlight", preset.highlightColor.ToArray());
                shader.SetFloats("_Hair", preset.hairColor.ToArray());
                shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);

                foreach (Material mat in BodyMats)
                {
                    mat.mainTexture = resultTextures[2];
                }

                // shader.SetTexture(0, "_Result", resultTextures[4]);
                // shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);
                // foreach (Material mat in FlexMatsBody)
                // {
                //     mat.mainTexture = resultTextures[2];
                // }
            }

            //TODO: determine outfit to use different compute shaders

            //Assign shader values



            /*              This is cooked
            shader.SetTexture(0, "_Result", resultTextures[0]);
            shader.SetTexture(1, "_Result", resultTextures[1]);

            shader.SetFloats("_Tint", ColorMenuController.instance.presets[ColorMenuController.instance.currentPreset].mainColor.ToArray());
            shader.SetFloats("_Accent", ColorMenuController.instance.presets[ColorMenuController.instance.currentPreset].accentColor.ToArray());
            shader.SetFloats("_Highlight", ColorMenuController.instance.presets[ColorMenuController.instance.currentPreset].highlightColor.ToArray());
            shader.SetFloats("_Hair", ColorMenuController.instance.presets[ColorMenuController.instance.currentPreset].hairColor.ToArray());
            shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);
            shader.Dispatch(1, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);

            foreach (Material mat in HatMats)
            {
                mat.mainTexture = resultTextures[0];
            }
            shader.SetTexture(0, "_Result", resultTextures[2]);
            shader.Dispatch(0, Mathf.CeilToInt(resultTextures[0].width / 8), Mathf.CeilToInt(resultTextures[0].height / 8), 1);

            foreach (Material mat in HairMats)
            {
                mat.mainTexture = resultTextures[1];
            }

            foreach (Material mat in BodyMats)
            {
                mat.mainTexture = resultTextures[2];
            }
            */

        }
    }


}





public enum Effects
{
    None,
    Tint,
    Cycle,
}


[System.Serializable]
public class ColorPreset
{
    public Color mainColor;
    public Color accentColor;
    public Color highlightColor;
    public Color hairColor;
    public Color rimLight;
    public string title;
    public ColorPreset(Color m, Color a, Color h, Color hair, Color r, string title)
    {
        this.mainColor = m;
        this.accentColor = a;
        this.highlightColor = h;
        this.title = title;
        this.hairColor = hair;
        this.rimLight = r;
    }
    public ColorPreset(ColorPreset preset, string title)
    {
        this.mainColor = preset.mainColor.Clone();
        this.accentColor = preset.accentColor.Clone();
        this.highlightColor = preset.highlightColor.Clone();
        this.title = title;
        this.hairColor = preset.hairColor.Clone();
        this.rimLight = preset.rimLight.Clone();
    }

    public ColorPreset(string str)
    {
        Color main;
        Color accent;
        Color highlight;
        Color hair;
        string title;
        string Contents = str;
        Contents.Replace("\n", "");     //Preset_Name, main, 0-255, 0-255, 0-255, accent, 0-255, 0-255, 0-255, highlight, 0-255, 0-255, 0-255, (0,1)
        string[] Properties = Contents.Split(",");
        title = Properties[0];
        main = new Color(float.Parse(Properties[2]) / 255, float.Parse(Properties[3]) / 255, float.Parse(Properties[4]) / 255, 1);
        accent = new Color(float.Parse(Properties[6]) / 255, float.Parse(Properties[7]) / 255, float.Parse(Properties[8]) / 255, 1);
        highlight = new Color(float.Parse(Properties[10]) / 255, float.Parse(Properties[11]) / 255, float.Parse(Properties[12]) / 255, 1);
        hair = new Color(float.Parse(Properties[14]) / 255, float.Parse(Properties[15]) / 255, float.Parse(Properties[16]) / 255, 1);


        this.title = title;
        this.mainColor = main;
        this.accentColor = accent;
        this.highlightColor = highlight;
        this.hairColor = hair;
    }

    public override string ToString()
    {
        return title + " Main: " + mainColor.ToString() + " Acc: " + accentColor.ToString() + " Highlight: " + highlightColor.ToString() + " Hair: " + hairColor.ToString() + " At index: " + ColorMenuController.instance.currentPreset;
    }

    public string SaveString(int isActive)
    {
        //Preset_Name, main, 0-255, 0-255, 0-255, accent, 0-255, 0-255, 0-255, highlight, 0-255, 0-255, 0-255, (0,1)
        return title + ", main, " + mainColor.r * 255 + ", " + mainColor.g * 255 + ", " + mainColor.b * 255 + ", accent, " + accentColor.r * 255 + ", " + accentColor.g * 255 + ", " + accentColor.b * 255 + ", highlight, " + highlightColor.r * 255 + ", " + highlightColor.g * 255 + ", " + highlightColor.b * 255 + ", hair, " + hairColor.r * 255 + ", " + hairColor.g * 255 + ", " + hairColor.b * 255 + ", " + isActive;


    }
}

public class ColorMenuController : MonoBehaviour
{
    public List<ColorPreset> presets = new List<ColorPreset>();
    public int currentPreset = 0;
    public static ColorMenuController instance;
    InspectMode inspectMode = InspectMode.Main;
    GameObject Dropdown;
    GameObject mainColorObj;
    GameObject accColorObj;
    GameObject highColorObj;
    GameObject hairColorObj;
    GameObject preview;
    GameObject RSlider, GSlider, BSlider;
    GameObject RInput, GInput, BInput;

    GameObject BackButton;

    GameObject PresetName;

    GameObject Delete;

    GameObject Reset;
    GameObject ResetWarning;
    

    public Canvas renderCanvas;

    public bool freeze=false;

    void Awake(){
        instance = this;
    }
    // Start is called before the first frame update
    void Start()
    {
        Dropdown = transform.Find("Dropdown").gameObject;


        mainColorObj = transform.Find("MainColor").gameObject;
        accColorObj = transform.Find("AccentColor").gameObject;
        highColorObj = transform.Find("HighlightColor").gameObject;
        hairColorObj = transform.Find("HairColor").gameObject;

        preview = transform.Find("Preview").gameObject;
        RSlider = preview.transform.Find("RSlider").gameObject;
        GSlider = preview.transform.Find("GSlider").gameObject;
        BSlider = preview.transform.Find("BSlider").gameObject;


        RInput = RSlider.transform.Find("Input").gameObject;
        GInput = GSlider.transform.Find("Input").gameObject;
        BInput = BSlider.transform.Find("Input").gameObject;


        PresetName = transform.Find("PresetName").gameObject;



        BackButton = transform.Find("Back").gameObject;

        Delete = transform.Find("Delete").gameObject;

        Reset = transform.Find("Reset").gameObject;
        ResetWarning = transform.Find("WARNING").gameObject;

        SetAllActives(false);
        
        innitValues();
        
    }

    public void SetAllActives(bool target){
        mainColorObj.SetActive(target);
        accColorObj.SetActive(target);
        highColorObj.SetActive(target);
        hairColorObj.SetActive(target);

        preview.SetActive(target);

        Dropdown.SetActive(target);

        PresetName.SetActive(target);

        BackButton.SetActive(target);

        Delete.SetActive(target);

        Reset.SetActive(target);

        ResetWarning.SetActive(false);

        
    }

    public void innitValues(){
        SetAllActives(true);
        ResetWarning.SetActive(true);
        RSlider.GetComponent<Slider>().value = presets[currentPreset].mainColor.r*255;
        GSlider.GetComponent<Slider>().value = presets[currentPreset].mainColor.g*255;
        BSlider.GetComponent<Slider>().value = presets[currentPreset].mainColor.b*255;

        PresetName.GetComponent<TMP_InputField>().text = presets[currentPreset].title;

        RSlider.transform.Find("Input").GetComponent<TMP_InputField>().text = (presets[currentPreset].mainColor.r*255)+"";
        GSlider.transform.Find("Input").GetComponent<TMP_InputField>().text = (presets[currentPreset].mainColor.g*255)+"";
        BSlider.transform.Find("Input").GetComponent<TMP_InputField>().text = (presets[currentPreset].mainColor.b*255)+"";

        UpdateDropDownOptions();
        UpdateColorDisplays();
        Debug.Log(presets[currentPreset]);

        mainColorObj.GetComponent<Button>().onClick.AddListener(() => {inspectMode=InspectMode.Main;freeze=true;UpdateColorDisplays();freeze=false;});
        accColorObj.GetComponent<Button>().onClick.AddListener(() => {inspectMode=InspectMode.Accent;freeze=true;UpdateColorDisplays();freeze=false;});
        highColorObj.GetComponent<Button>().onClick.AddListener(() => {inspectMode=InspectMode.Highlight;freeze=true;UpdateColorDisplays();freeze=false;});
        hairColorObj.GetComponent<Button>().onClick.AddListener(() => {inspectMode=InspectMode.Hair;freeze=true;UpdateColorDisplays();freeze=false;});

        RSlider.GetComponent<Slider>().onValueChanged.AddListener((val)=>OnSliderChanged(val,0));
        GSlider.GetComponent<Slider>().onValueChanged.AddListener((val)=>OnSliderChanged(val,1));
        BSlider.GetComponent<Slider>().onValueChanged.AddListener((val)=>OnSliderChanged(val,2));

        RInput.GetComponent<TMP_InputField>().onValidateInput+= (str,  c,  a)=>{return (OnValidateInput(str, a));};
        RInput.GetComponent<TMP_InputField>().onEndEdit.AddListener((str)=>UpdateColor_Input());
        GInput.GetComponent<TMP_InputField>().onValidateInput+= (str,  c,  a)=>{return (OnValidateInput(str, a));};
        GInput.GetComponent<TMP_InputField>().onEndEdit.AddListener((str)=>UpdateColor_Input());
        BInput.GetComponent<TMP_InputField>().onValidateInput+= (str,  c,  a)=>{return (OnValidateInput(str, a));};
        BInput.GetComponent<TMP_InputField>().onEndEdit.AddListener((str)=>UpdateColor_Input());

        PresetName.GetComponent<TMP_InputField>().onEndEdit.AddListener(OnNameEnter);
        PresetName.GetComponent<TMP_InputField>().onValidateInput+= (str,  c,  a)=>{return (OnValidateName(str, a));};

        BackButton.GetComponent<Button>().onClick.AddListener(()=>{
            GayZoePluginRetake.Back();
            // renderCanvas.enabled = false;
            SetAllActives(false);
            
            });

        
        Dropdown.GetComponent<TMP_Dropdown>().onValueChanged.AddListener((i)=>{freeze=true;OnDropDownChanged(i);freeze=false;});
        Dropdown.GetComponent<TMP_Dropdown>().value = currentPreset;

        Delete.GetComponent<Button>().onClick.AddListener(() => {
            if(presets.Count>1){
                presets.RemoveAt(currentPreset);
                currentPreset=0;
                Dropdown.GetComponent<TMP_Dropdown>().value = 0;
                freeze=true;OnDropDownChanged(0);freeze=false;
                
            }
        });

        Reset.GetComponent<Button>().onClick.AddListener(()=>{
            ResetWarning.SetActive(true);
        });
        ResetWarning.transform.Find("Yes").GetComponent<Button>().onClick.AddListener(()=>{
            List<ColorPreset> nonDefaults = presets.FindAll(p => {return p.title!="Agender-Aromantic"&&p.title!="Asexual Pride"&&p.title!="Ava!"&&p.title!="Bisexual Pride"&&p.title!="Captain!"&&p.title!="Gan! (or dalil)"&&p.title!="Gay Pride"&&p.title!="Grandma!"&&p.title!="Lesbian Pride"&&p.title!="Niada!"&&p.title!="Non-Binary"&&p.title!="Pansexual Pride"&&p.title!="Riza!"&&p.title!="Trans Pride"&&p.title!="Wraith!";});

            presets=nonDefaults;
            GayZoePluginRetake.AddPresetFromString("Agender-Aromantic, main, 0, 0, 0, accent, 65, 66, 67, highlight, 255, 255, 255, hair, 72, 255, 72, 0");
            GayZoePluginRetake.AddPresetFromString("Asexual Pride, main, 112, 13, 154, accent, 142, 143, 146, highlight, 255, 255, 255, hair, 45, 25, 24, 0");
            GayZoePluginRetake.AddPresetFromString("Ava!, main, 227, 182, 48, accent, 28, 27, 12, highlight, 156, 123, 224, hair, 56, 32, 4, 0");
            GayZoePluginRetake.AddPresetFromString("Bisexual Pride, main, 155, 48, 75, accent, 53, 60, 118, highlight, 91, 82, 111, hair, 91, 82, 111, 0");
            GayZoePluginRetake.AddPresetFromString("Captain!, main, 255, 255, 255, accent, 209, 0, 217, highlight, 211, 161, 0, hair, 112, 28, 0, 0");
            GayZoePluginRetake.AddPresetFromString("Gan! (or dalil), main, 0, 190, 236, accent, 255, 117, 0, highlight, 0, 45, 89, hair, 255, 41, 48, 0");
            GayZoePluginRetake.AddPresetFromString("Gay Pride, main, 35, 103, 77, accent, 185, 255, 209, highlight, 73, 92, 211, hair, 255, 255, 255, 0");
            GayZoePluginRetake.AddPresetFromString("Grandma!, main, 168, 70, 81, accent, 0, 155, 192, highlight, 255, 152, 0, hair, 98, 94, 96, 0");
            GayZoePluginRetake.AddPresetFromString("Lesbian Pride, main, 255, 104, 60, accent, 255, 149, 115, highlight, 210, 130, 180, hair, 255, 255, 255, 0");
            GayZoePluginRetake.AddPresetFromString("Niada!, main, 255, 255, 255, accent, 33, 129, 255, highlight, 178, 0, 105, hair, 29, 40, 42, 0");
            GayZoePluginRetake.AddPresetFromString("Non-Binary, main, 141, 128, 165, accent, 255, 255, 255, highlight, 255, 255, 13, hair, 53, 30, 18, 0");
            GayZoePluginRetake.AddPresetFromString("Pansexual Pride, main, 19, 212, 255, accent, 249, 235, 49, highlight, 255, 255, 255, hair, 255, 147, 248, 0");
            GayZoePluginRetake.AddPresetFromString("Riza!, main, 63, 164, 108, accent, 255, 255, 255, highlight, 105, 110, 105, hair, 161, 255, 255, 0");
            GayZoePluginRetake.AddPresetFromString("Trans Pride, main, 78, 208, 255, accent, 255, 205, 254, highlight, 255, 255, 255, hair, 139, 109, 117, 0");
            GayZoePluginRetake.AddPresetFromString("Wraith!, main, 9, 53, 48, accent, 65, 183, 158, highlight, 140, 157, 159, hair, 105, 67, 36, 0");

            UpdateColorDisplays();
            UpdateDropDownOptions();
            ResetWarning.SetActive(false);
            
        });
        ResetWarning.transform.Find("No").GetComponent<Button>().onClick.AddListener(()=>ResetWarning.SetActive(false));

        ResetWarning.SetActive(false);
        SetAllActives(false);

        // UpdateColorDisplays();
        
        // gameObject.SetActive(false);
    }

    void Update(){
        if(Input.GetKeyDown(KeyCode.Escape)){
            // if(renderCanvas.enabled==false)
            // {
            //     return;
            // }
            // GayZoePlugin.Back();
            // renderCanvas.enabled = false;

            if(hairColorObj.activeSelf==false)
            {
                return;
            }
            GayZoePluginRetake.Back();
            
            SetAllActives(false);

            
        }
    }
    void OnSliderChanged(float val, int channel){
        UpdateColor_Slider();
    }

    char OnValidateName(string str, char a){
        if(a==','){
            return '\0';
        }
        return a;
    }
    void OnNameEnter(string str){
        presets[currentPreset].title = str;
        UpdateDropDownOptions();
    }
    char OnValidateInput(string str, char a){
        if(float.TryParse(str+a, out float num)){
            return a;
        }else{
            return '\0';
        }

    }

    void UpdateColor_Input(){
        if(freeze)
            return;
        switch (inspectMode){
            case InspectMode.Main:
                presets[currentPreset].mainColor = new Color(float.Parse(RInput.GetComponent<TMP_InputField>().text)/255, float.Parse(GInput.GetComponent<TMP_InputField>().text)/255, float.Parse(BInput.GetComponent<TMP_InputField>().text)/255, 1);
                break;
            case InspectMode.Accent:
                presets[currentPreset].accentColor = new Color(float.Parse(RInput.GetComponent<TMP_InputField>().text)/255, float.Parse(GInput.GetComponent<TMP_InputField>().text)/255, float.Parse(BInput.GetComponent<TMP_InputField>().text)/255, 1);
                break;
            case InspectMode.Highlight:
                presets[currentPreset].highlightColor = new Color(float.Parse(RInput.GetComponent<TMP_InputField>().text)/255, float.Parse(GInput.GetComponent<TMP_InputField>().text)/255, float.Parse(BInput.GetComponent<TMP_InputField>().text)/255, 1);
                break;
            case InspectMode.Hair:
                presets[currentPreset].hairColor = new Color(float.Parse(RInput.GetComponent<TMP_InputField>().text)/255, float.Parse(GInput.GetComponent<TMP_InputField>().text)/255, float.Parse(BInput.GetComponent<TMP_InputField>().text)/255, 1);
                break;
            default:
                Debug.LogError("InspectMode is not a valid value!");
                break;
        }
        UpdateColorDisplays();
    }

    void UpdateColor_Slider(){
        if(freeze)
            return;
        switch (inspectMode){
            case InspectMode.Main:
                presets[currentPreset].mainColor = new Color(RSlider.GetComponent<Slider>().value/255, GSlider.GetComponent<Slider>().value/255, BSlider.GetComponent<Slider>().value/255, 1);
                break;
            case InspectMode.Accent:
                presets[currentPreset].accentColor = new Color(RSlider.GetComponent<Slider>().value/255, GSlider.GetComponent<Slider>().value/255, BSlider.GetComponent<Slider>().value/255, 1);
                break;
            case InspectMode.Highlight:
                presets[currentPreset].highlightColor = new Color(RSlider.GetComponent<Slider>().value/255, GSlider.GetComponent<Slider>().value/255, BSlider.GetComponent<Slider>().value/255, 1);
                break;
            case InspectMode.Hair:
                presets[currentPreset].hairColor = new Color(RSlider.GetComponent<Slider>().value/255, GSlider.GetComponent<Slider>().value/255, BSlider.GetComponent<Slider>().value/255, 1);
                break;
            default:
                Debug.LogError("InspectMode is not a valid value!");
                break;
        }
        UpdateColorDisplays();
    }

    // Update is called once per frame
    void OnApplicationQuit()
    {
        for(int i=0; i<presets.Count;i++){
            using(StreamWriter sw = new StreamWriter(Path.Combine(GayZoePluginRetake.AssemblyDirectory, presets[i].title)+".color")){
                if(i==currentPreset){
                    sw.WriteLine(presets[i].SaveString(1));
                }else{
                    sw.WriteLine(presets[i].SaveString(0));
                }
            }
        }
    }

    public void UpdateDropDownOptions(){
        List<TMP_Dropdown.OptionData> optionData = new List<TMP_Dropdown.OptionData>();
        foreach (var preset in presets){
            optionData.Add(new TMP_Dropdown.OptionData(preset.title));
        }
        optionData.Add(new TMP_Dropdown.OptionData("New Preset"));
        Dropdown.GetComponent<TMP_Dropdown>().options = optionData;
    }
    public void UpdateDropdownValue(){
        Dropdown.GetComponent<TMP_Dropdown>().value = currentPreset;
    }
    public void OnDropDownChanged(int value){
        
        if(value >= presets.Count){
            presets.Add(new ColorPreset(presets[currentPreset],"New Tint Preset"));
            currentPreset=presets.Count-1;
        }else{
            currentPreset=value;
        }


        UpdateColorDisplays();
        UpdateDropDownOptions();
        PresetName.GetComponent<TMP_InputField>().text = presets[currentPreset].title;
    }

    public void UpdateColorDisplays()
    {
        Debug.Log("Color inspect mode set to: " + inspectMode);
        mainColorObj.GetComponent<Image>().color = presets[currentPreset].mainColor;
        accColorObj.GetComponent<Image>().color = presets[currentPreset].accentColor;
        highColorObj.GetComponent<Image>().color = presets[currentPreset].highlightColor;
        hairColorObj.GetComponent<Image>().color = presets[currentPreset].hairColor;
        switch (inspectMode)
        {
            case InspectMode.Main:
                preview.GetComponent<Image>().color = presets[currentPreset].mainColor;
                break;
            case InspectMode.Accent:
                preview.GetComponent<Image>().color = presets[currentPreset].accentColor;
                break;
            case InspectMode.Highlight:
                preview.GetComponent<Image>().color = presets[currentPreset].highlightColor;
                break;
            case InspectMode.Hair:
                preview.GetComponent<Image>().color = presets[currentPreset].hairColor;
                break;
            default:
                Debug.LogError("InspectMode is not a valid value!");
                break;
        }

        UpdateSliders();
        UpdateInputs();


        // NewBehaviourScript.instance.swappythings(presets[currentPreset]);
        ZRCInstance.localInstance.preset = presets[currentPreset];
        ZRCInstance.localInstance.ApplyEffect();

        if (NetworkManager.Singleton == null)
            return;
        FastBufferWriter writer = new FastBufferWriter(128, Unity.Collections.Allocator.Temp);
        writer.WriteValue(presets[currentPreset].SaveString(1));
        foreach (int key in GayZoePluginRetake.recolorInstances.Keys) {
            if (key == -1)
                continue;
            NetworkHackManager.ForceSendMessage("GayZoeUpdateColor", (ulong) key, writer);
            
        }
    }
    public void UpdateSliders(){
        switch(inspectMode){
            case InspectMode.Main:
                RSlider.GetComponent<Slider>().value = presets[currentPreset].mainColor.r*255;
                GSlider.GetComponent<Slider>().value = presets[currentPreset].mainColor.g*255;
                BSlider.GetComponent<Slider>().value = presets[currentPreset].mainColor.b*255;
                break;
            case InspectMode.Accent:
                RSlider.GetComponent<Slider>().value = presets[currentPreset].accentColor.r*255;
                GSlider.GetComponent<Slider>().value = presets[currentPreset].accentColor.g*255;
                BSlider.GetComponent<Slider>().value = presets[currentPreset].accentColor.b*255;
                break;
            case InspectMode.Highlight:
                RSlider.GetComponent<Slider>().value = presets[currentPreset].highlightColor.r*255;
                GSlider.GetComponent<Slider>().value = presets[currentPreset].highlightColor.g*255;
                BSlider.GetComponent<Slider>().value = presets[currentPreset].highlightColor.b*255;
                break;
            case InspectMode.Hair:
                RSlider.GetComponent<Slider>().value = presets[currentPreset].hairColor.r*255;
                GSlider.GetComponent<Slider>().value = presets[currentPreset].hairColor.g*255;
                BSlider.GetComponent<Slider>().value = presets[currentPreset].hairColor.b*255;
                break;
            default:
                Debug.LogError("InspectMode is not a valid value!");
                break;
        }
    }

    public void UpdateInputs(){
        switch(inspectMode){
            case InspectMode.Main:
                RSlider.transform.Find("Input").GetComponent<TMP_InputField>().text = (presets[currentPreset].mainColor.r*255)+"";
                GSlider.transform.Find("Input").GetComponent<TMP_InputField>().text = (presets[currentPreset].mainColor.g*255)+"";
                BSlider.transform.Find("Input").GetComponent<TMP_InputField>().text = (presets[currentPreset].mainColor.b*255)+"";
                break;
            case InspectMode.Accent:
                RSlider.transform.Find("Input").GetComponent<TMP_InputField>().text = (presets[currentPreset].accentColor.r*255)+"";
                GSlider.transform.Find("Input").GetComponent<TMP_InputField>().text = (presets[currentPreset].accentColor.g*255)+"";
                BSlider.transform.Find("Input").GetComponent<TMP_InputField>().text = (presets[currentPreset].accentColor.b*255)+"";
                break;
            case InspectMode.Highlight:
                RSlider.transform.Find("Input").GetComponent<TMP_InputField>().text = (presets[currentPreset].highlightColor.r*255)+"";
                GSlider.transform.Find("Input").GetComponent<TMP_InputField>().text = (presets[currentPreset].highlightColor.g*255)+"";
                BSlider.transform.Find("Input").GetComponent<TMP_InputField>().text = (presets[currentPreset].highlightColor.b*255)+"";
                break;
            case InspectMode.Hair:
                RSlider.transform.Find("Input").GetComponent<TMP_InputField>().text = (presets[currentPreset].hairColor.r*255)+"";
                GSlider.transform.Find("Input").GetComponent<TMP_InputField>().text = (presets[currentPreset].hairColor.g*255)+"";
                BSlider.transform.Find("Input").GetComponent<TMP_InputField>().text = (presets[currentPreset].hairColor.b*255)+"";
                break;
            default:
                Debug.LogError("InspectMode is not a valid value!");
                break;
        }
    }

    public enum InspectMode{
        Main,
        Accent,
        Highlight,
        Hair
    }
}







[HasteSetting]
public class ColorMode : EnumSetting<Effects>, IExposedSetting
{
    public override void ApplyValue()
    {
        // GayZoePlugin.SetEffect(base.Value);
        Debug.LogWarning(ZRCInstance.localInstance);
        if (ZRCInstance.localInstance == null)
        {
            return;
        }
        ZRCInstance.localInstance.CurrentEffect = base.Value;
        ZRCInstance.localInstance.preset = ColorMenuController.instance.presets[ColorMenuController.instance.currentPreset];

        ZRCInstance.localInstance.ApplyEffect();


        if (NetworkManager.Singleton == null)
            return;
        FastBufferWriter writer = new FastBufferWriter(128, Unity.Collections.Allocator.Temp);
        writer.WriteValue(ColorMenuController.instance.presets[ColorMenuController.instance.currentPreset].SaveString(1));
        foreach (int key in GayZoePluginRetake.recolorInstances.Keys) {
            if (key == -1)
                continue;
            NetworkHackManager.ForceSendMessage("GayZoeUpdateColor", (ulong) key, writer);
            
        }
        
    }

    public string GetCategory()
    {
        return "Zoe ReColor";
    }

    public LocalizedString GetDisplayName()
    {
        return new UnlocalizedString("ReColor Mode");
    }

    public override List<LocalizedString> GetLocalizedChoices()
    {
       return new List<LocalizedString>
        {
            new UnlocalizedString("None"),
            new UnlocalizedString("Tint"),
            new UnlocalizedString("Cycle")
        };
    }

    protected override Effects GetDefaultValue()
    {
        return Effects.Tint;
    }
}



[HasteSetting]
public class OpenTintSettings : ButtonSetting, IExposedSetting
{
    public override string GetButtonText()
    {
        return "Open";
    }

    public string GetCategory()
    {
        return "Zoe ReColor";
    }

    public LocalizedString GetDisplayName()
    {
        return new UnlocalizedString("Open tint settings");
    }

    public override void OnClicked(ISettingHandler settingHandler)
    {
        // ColorMenuController.instance.renderCanvas.enabled = (true);
        ColorMenuController.instance.SetAllActives(true);
        if (SceneManager.GetActiveScene().name == "MainMenu")
        {
            GayZoePluginRetake.disabledSettingsObj = GameObject.Find("EscapeMenu/SettingsPage/SettingsPage");
            GayZoePluginRetake.disabledSettingsObj.SetActive(false);
        }
        else
        {
            GayZoePluginRetake.disabledSettingsObj = GameObject.Find("EscapeMenu/Enabled");
            GayZoePluginRetake.disabledSettingsObj.SetActive(false);
        }
    }
}




public static class Extensions
{
    public static float[] ToArray(this Color color)
    {

        return new float[] { color.r, color.g, color.b, color.a };
    }
    public static float[] ToHSVArray(this Color color)
    {
        Color.RGBToHSV(color, out float h, out float s, out float v);

        return new float[] { h, s, v, color.a };
    }
    public static Color Clone(this Color color)
    {
        return new Color(color.r, color.g, color.b, color.a);
    }

    public static Transform FindDeepChild(this Transform aParent, string aName)
    {
        Queue<Transform> queue = new Queue<Transform>();
        queue.Enqueue(aParent);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (c.name == aName)
                return c;
            foreach (Transform t in c)
                queue.Enqueue(t);
        }
        return null;
    }
    public static string GetTransformPath(this Transform obj) {
    string path = "/" + obj.name;
    while (obj.parent != null) {
        obj = obj.parent;
        path = "/" + obj.name + path;
    }
    return path;
}
}