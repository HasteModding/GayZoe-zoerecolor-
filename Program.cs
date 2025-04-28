// using HarmonyLib;
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


namespace GayZoe;


/// TODO: 
///         Disable settings on 'esc' -- maybe check landfalls inputs for like a menu close bind?
///         Disable settings content on menu open -- need path and dats it
///         Add hair shading to ApplyValue -- new rt (do memory management) new floats ez
///         Add extra character twinning presets -- need to figure out how to load these, maybe hard code them in if we are loading through legacy
///         Add preset deletion
///         Add saving to file
///         Figure out color inaccuracy bug -- only with hair?
///     Adjust rim lights to highlight color
///     
///     work on gradients;


[LandfallPlugin]
public class GayZoePlugin {
    static Texture zoeMainTex;
    static Texture zoeHairTex;
    static List<Material> zoeMaterials;
    static List<Material> zoeHairMats;
    static ComputeShader shader;
    static bool assetsLoaded = false;
    public bool triedToLoadFilePresets=false;
    static RenderTexture rt;
    static RenderTexture hair;
    public static Effects currentEffect;
    public static float CycleSpeed = 1;
    public static float[] LegacyTintValues = [-1,-1,-1];
    public static FloatSetting[] tintSettings = new FloatSetting[3];
    public static bool globalEnable = false;
    static Color tint = Color.white;
    public static GameObject tintMenu;
    public static GameObject tintMenuInstance;
    public static GameObject disabledSettingsObj;

    public static float internalTime = 0;
    public static string AssemblyDirectory {
        get {
            string codeBase = Assembly.GetExecutingAssembly().CodeBase;
            UriBuilder uri = new UriBuilder(codeBase);
            string path = Uri.UnescapeDataString(uri.Path);
            return Path.GetDirectoryName(path);
        }
    }

    static GayZoePlugin(){
        zoeMaterials = new List<Material>();
        zoeHairMats = new List<Material>();
        CycleSpeed = 1;
        AssetBundle assetBundle = AssetBundle.LoadFromFile(Path.Combine(AssemblyDirectory, "zoerecolors"));
        if (assetBundle == null) {
            Debug.LogError("Failed to load AssetBundle!");
            return;
        }
        zoeMainTex = assetBundle.LoadAsset<Texture2D>("courier_c_clothes_BaseColor_0"); //-- grabbing from game mat now to hopefully avoid compression artifacts -- does not fix compression artifacts :( might redraw the boots texture to fix re enabling for now
        zoeHairTex = assetBundle.LoadAsset<Texture2D>("c_hair_basecolor_24");
        shader = assetBundle.LoadAsset<ComputeShader>("EffectsShader");
        tintMenu = assetBundle.LoadAsset<GameObject>("ColorMenu");
        
        assetsLoaded=true;

        // harmony.PatchAll();

        SceneManager.sceneLoaded+= OnSceneLoaded;
        On.PlayerCharacter.Update += PlayerCharacterPatch.updatePatch;
        On.PlayerCharacter.Awake += PlayerCharacterPatch.awakePatch;
        Application.quitting+=()=>
        {
            if(rt!=null){
                rt.Release();
            }
            if(hair!=null){
                hair.Release();
            }
        };//my ass CANNOT be trusted with memory management

        
        GameObject c = new GameObject("GayZoeMenuCanvas");
        c.AddComponent<Canvas>().sortingOrder=100;
        c.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        c.AddComponent<CanvasScaler>().uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        c.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920,1080);
        c.AddComponent<GraphicRaycaster>();
        tintMenuInstance = GameObject.Instantiate(tintMenu);
        tintMenuInstance.transform.SetParent(c.transform);
        tintMenuInstance.AddComponent<ColorMenuController>().renderCanvas = c.GetComponent<Canvas>();
        tintMenuInstance.transform.localPosition = new Vector3(0,0,0);
        c.GetComponent<Canvas>().enabled = false;
        GameObject.DontDestroyOnLoad(c);
        // tintMenuInstance.SetActive(false);
        
    }

    // 

        // List<SerializedFact> myFacts = FactSystem.GetSerializedFacts().FindAll((fact)=>{return fact.Key.Contains("Gay_Zoe");});
        // SerializedFact tintR = myFacts.Find((fact)=>{return fact.Key == "Gay_Zoe_Tint_R";});
        // if (tintR != null){

        // }

    public static void tryLoadFile(){
        bool hasPresets = false;
         foreach (string path in Directory.GetFiles(AssemblyDirectory)){
            if (path.EndsWith(".color")){
                try{
                    using(StreamReader sr = new StreamReader(path)){
                        hasPresets = true;
                        Color main;
                        Color accent; 
                        Color highlight;
                        Color hair;
                        string title;
                        string Contents = sr.ReadToEnd();
                        Contents.Replace("\n", "");     //Preset_Name, main, 0-255, 0-255, 0-255, accent, 0-255, 0-255, 0-255, highlight, 0-255, 0-255, 0-255, (0,1)
                        string[] Properties = Contents.Split(",");
                        title = Properties[0];
                        main = new Color(float.Parse(Properties[2])/255,float.Parse(Properties[3])/255,float.Parse(Properties[4])/255,1);
                        accent = new Color(float.Parse(Properties[6])/255,float.Parse(Properties[7])/255,float.Parse(Properties[8])/255,1);
                        highlight = new Color(float.Parse(Properties[10])/255,float.Parse(Properties[11])/255,float.Parse(Properties[12])/255,1);
                        
                        hair = new Color(float.Parse(Properties[14])/255,float.Parse(Properties[15])/255,float.Parse(Properties[16])/255,1);

                        ColorMenuController.instance.presets.Add(new ColorPreset(main, accent, highlight, hair, title));
                        if(int.Parse(Properties[17])==1){
                            ColorMenuController.instance.currentPreset=ColorMenuController.instance.presets.Count-1;
                            ApplyEffect();
                        }
                        Debug.Log("Found color preset! "+title);
                    }
                }catch(Exception e){
                    Debug.LogError("Failed to assign color preset: " + path+"\n");
                    Debug.LogError(e.Message+" "+e.StackTrace);
                }
                File.Delete(path);
            }
         }

         if (!hasPresets){
            Debug.LogWarning("No color presets found! Loading Legacy tint values");
            LoadFromLegacy();
         }
    }

    public static void LoadFromLegacy(){
        // Debug.Log(tintPresets.Count);
        // foreach(var legacy in LegacyTintValues){
        //     Debug.Log(legacy);
        // }
        if(ColorMenuController.instance.presets.Count==0&&LegacyTintValues.All<float>((v)=>{return v!=-1;})){
            Debug.Log("Loading from Legacy Settings!");
            Color main = new Color(LegacyTintValues[0]/255,LegacyTintValues[1]/255,LegacyTintValues[2]/255,1);
            Color accent = new Color(18f/255, 135f/255, 1, 1);
            Color highlight = Color.white;
            Color hair = new Color(86f/255, 52f/255, 37f/255, 1);
            ColorMenuController.instance.presets.Add(new ColorPreset(main, accent, highlight, hair, "Gay Zoe Default Preset"));
            ColorMenuController.instance.currentPreset=0;
            Debug.Log("Legacy Preset: "+ColorMenuController.instance.presets[ColorMenuController.instance.currentPreset].ToString());
            ColorMenuController.instance.currentPreset = ColorMenuController.instance.presets.Count-1;
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
            ApplyEffect();
        }
    }
    public static void AddPresetFromString(string str){
        Color main;
        Color accent; 
        Color highlight;
        Color hair;
        string title;
        string Contents = str;
        Contents.Replace("\n", "");     //Preset_Name, main, 0-255, 0-255, 0-255, accent, 0-255, 0-255, 0-255, highlight, 0-255, 0-255, 0-255, (0,1)
        string[] Properties = Contents.Split(",");
        title = Properties[0];
        main = new Color(float.Parse(Properties[2])/255,float.Parse(Properties[3])/255,float.Parse(Properties[4])/255,1);
        accent = new Color(float.Parse(Properties[6])/255,float.Parse(Properties[7])/255,float.Parse(Properties[8])/255,1);
        highlight = new Color(float.Parse(Properties[10])/255,float.Parse(Properties[11])/255,float.Parse(Properties[12])/255,1);
        hair = new Color(float.Parse(Properties[14])/255,float.Parse(Properties[15])/255,float.Parse(Properties[16])/255,1);

        ColorMenuController.instance.presets.Add(new ColorPreset(main, accent, highlight, hair, title));
        Debug.Log("Added Preset: "+ColorMenuController.instance.presets[ColorMenuController.instance.presets.Count-1]);
    }

    public static void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        
        if(scene.name=="MainMenu"){
            
            
            tryLoadFile();

            ColorMenuController.instance.innitValues();
            if(!tryGetMaterialInfoAlt()){
                Debug.LogError("Could not find courier material to apply effects!");
                return;
            }
            
            GameObject g = new GameObject("GayZoeMainMenuManager");
            g.AddComponent<GayZoeColorManager>();
            ApplyEffect();
            




            // colorMenu = new GameObject("GayColorMenu");
            // colorMenu.transform.SetParent(g.transform);
            // colorMenu.AddComponent<Image>();             immmmm not doing this by hand... time 4 asset bundle

        }
    }
    [ConsoleCommand]
    public static void GetRizaHair(){
        GameObject riza = GameObject.Find("Hub_Characters/Riza/Keeper/BL_Keeper_Riza/riza_head");
        Material mat = riza.GetComponent<SkinnedMeshRenderer>().materials[1];

        GameObject zoe = GameObject.Find("Player/Visual/Courier_Retake/Courier/Meshes/Head");
        zoe.GetComponent<SkinnedMeshRenderer>().materials[4] = mat;
        zoe.GetComponent<SkinnedMeshRenderer>().materials[5] = mat;
        zoe.GetComponent<SkinnedMeshRenderer>().materials[6] = mat;
        
    }
    [ConsoleCommand]
    public static void cmdApplyValue(){
        ApplyEffect();
    }
    // [ConsoleCommand]
    // public static void cmdAddAndApplyTintPreset(string name, float mainR, float mainG, float mainB, float accR=18, float accG=135, float accB=255, float highR=255, float highG=255, float highB=255){
    //     Color main = new Color(mainR/255, mainG/255, mainG/255);
    //     Color acc = new Color(accR/255, accG/255, accG/255);
    //     Color high = new Color(highR/255, highG/255, highB/255);
    //     ColorMenuController.instance.presets.Add(new ColorPreset(main, acc, high, name));
    //     ColorMenuController.instance.currentPreset = ColorMenuController.instance.presets.Count-1;
    //     ApplyEffect(Effects.Tint);
    // }
    [ConsoleCommand]
    public static void cmdPrintPreset(){
        Debug.Log(ColorMenuController.instance.presets[ColorMenuController.instance.currentPreset]);
    }
    public static void ApplyEffect(Effects? effect = null){
        // Debug.Log(zoeMaterials.Count);
        // if(zoeMaterial==null){
        //     List<Material> materials = [zoeMaterials[^1], zoeMaterials[^2]];
        //     zoeMaterials = materials;
        // }
        if(effect.HasValue){
            currentEffect = effect.Value;
        }else{
            effect=currentEffect;
        }
        
        if(zoeMaterials.Count==0){
            if(!tryGetMaterialInfo()){
                Debug.LogError("Could not find courier material to apply effects!");
                return;
            }
        }else if(zoeMaterials.Count>2){
            zoeMaterials = [zoeMaterials[^1],zoeMaterials[^2]];
        }
        if(zoeHairMats.Count>3){
            zoeHairMats = [zoeHairMats[^1],zoeHairMats[^2],zoeHairMats[^3]];
        }
        if(effect==Effects.None){
            
            zoeMaterials[0].mainTexture = zoeMainTex;
            zoeMaterials[1].mainTexture = zoeMainTex;
            
            zoeHairMats[0].mainTexture = zoeHairTex;
            zoeHairMats[1].mainTexture = zoeHairTex;
            zoeHairMats[2].mainTexture = zoeHairTex;
            return;
        }

        if(!assetsLoaded){
            Debug.LogError("Trying to apply values on unloaded assets!");
            return;
        }
        if(rt==null){
            rt=new RenderTexture(zoeMainTex.width,zoeMainTex.height,0);
            rt.enableRandomWrite=true;
        }
        if(hair==null){
            hair = new RenderTexture(zoeHairTex.width, zoeHairTex.height,0);
            hair.enableRandomWrite=true;
        }

        // Debug.Log("Ready to fuck !!"+ColorMenuController.instance.presets[ColorMenuController.instance.currentPreset].mainColor+" "+effect);
        
        if(effect==Effects.Tint){
            Graphics.Blit(zoeMainTex, rt);
            Graphics.Blit(zoeHairTex, hair);
            shader.SetTexture(0,"_Result", rt);
            shader.SetTexture(1,"_Result", hair);
            shader.SetFloats("_Tint", ColorMenuController.instance.presets[ColorMenuController.instance.currentPreset].mainColor.ToArray());
            shader.SetFloats("_Accent", ColorMenuController.instance.presets[ColorMenuController.instance.currentPreset].accentColor.ToArray());
            shader.SetFloats("_Highlight", ColorMenuController.instance.presets[ColorMenuController.instance.currentPreset].highlightColor.ToArray());
            shader.SetFloats("_Hair", ColorMenuController.instance.presets[ColorMenuController.instance.currentPreset].hairColor.ToArray());
            shader.Dispatch(0,Mathf.CeilToInt(rt.width/8),Mathf.CeilToInt(rt.height/8),1);
            shader.Dispatch(1,Mathf.CeilToInt(rt.width/8),Mathf.CeilToInt(rt.height/8),1);
            zoeMaterials[0].mainTexture = rt;
            zoeMaterials[1].mainTexture = rt;
            zoeHairMats[0].mainTexture = hair;
            zoeHairMats[1].mainTexture = hair;
            zoeHairMats[2].mainTexture = hair;
            
            
        }else if(effect==Effects.Cycle){
            Graphics.Blit(zoeMainTex, rt);
            shader.SetTexture(0,"_Result", rt);
            shader.SetFloats("_Tint",Color.HSVToRGB(internalTime%1,1,1).ToArray());
            shader.SetFloats("_Accent", ColorMenuController.instance.presets[ColorMenuController.instance.currentPreset].accentColor.ToArray());
            shader.SetFloats("_Highlight", ColorMenuController.instance.presets[ColorMenuController.instance.currentPreset].highlightColor.ToArray());
            shader.Dispatch(0,Mathf.CeilToInt(rt.width/8),Mathf.CeilToInt(rt.height/8),1);
            zoeMaterials[0].mainTexture = rt;
            zoeMaterials[1].mainTexture = rt;
        }

        
    }
    public static bool tryGetMaterialInfoAlt(){
        GameObject b = GameObject.Find("MainMenu/Courier_Main Menu/Courier/Meshes/Body");
        GameObject h = GameObject.Find("MainMenu/Courier_Main Menu/Courier/Meshes/Head");
        if(b!=null){
            zoeMaterials.Add(b.GetComponent<SkinnedMeshRenderer>().material);
            zoeMaterials.Add(h.GetComponent<SkinnedMeshRenderer>().materials[1]);
            zoeHairMats.Add(h.GetComponent<SkinnedMeshRenderer>().materials[4]);
            zoeHairMats.Add(h.GetComponent<SkinnedMeshRenderer>().materials[5]);
            zoeHairMats.Add(h.GetComponent<SkinnedMeshRenderer>().materials[6]);
            // zoeMainTex = zoeMaterial.GetTexture("_MainTex");
            Debug.Log("Tex: "+zoeMainTex);
            Debug.Log("Main: "+zoeMaterials.Count);
            Debug.Log("Hair: "+zoeMaterials.Count);
            return true;
            
        }
        Debug.Log("Failed Alt Mat Gathering");
        return false;
    }



    public static bool tryGetMaterialInfo(){
        GameObject b = GameObject.Find("Player/Visual/Courier_Retake/Courier/Meshes/Body");
        GameObject h = GameObject.Find("Player/Visual/Courier_Retake/Courier/Meshes/Head");
        if(b!=null){
            zoeMaterials.Add(b.GetComponent<SkinnedMeshRenderer>().material);
            zoeMaterials.Add(h.GetComponent<SkinnedMeshRenderer>().materials[1]);

            zoeHairMats.Add(h.GetComponent<SkinnedMeshRenderer>().materials[4]);
            zoeHairMats.Add(h.GetComponent<SkinnedMeshRenderer>().materials[5]);
            zoeHairMats.Add(h.GetComponent<SkinnedMeshRenderer>().materials[6]);
            // zoeMainTex = zoeMaterial.GetTexture("_MainTex");
            Debug.Log("Tex: "+zoeMainTex);
            Debug.Log("Main: "+zoeMaterials.Count);
            Debug.Log("Hair: "+zoeMaterials.Count);
            return true;
        }
        Debug.Log("Failed Main Mat Gathering");
        return false;
     }

    public static void OpenColorMenu(){

    }

    public static void SetEffect(Effects effects){
        currentEffect = effects;
        ApplyEffect(effects);
    }

    public static void Back()
    {
        disabledSettingsObj.SetActive(true);
    }
}
public enum Effects{
    None,
    Tint,
    Cycle,
}

public static class Extensions{
    public static float[] ToArray(this Color color){
    
        return new float[]{color.r,color.g,color.b,color.a};
    }
    public static float[] ToHSVArray(this Color color){
        Color.RGBToHSV(color, out float h, out float s, out float v);
    
        return new float[]{h,s,v,color.a};
    }
}



public static class PlayerCharacterPatch {
    
    public static void awakePatch(On.PlayerCharacter.orig_Awake orig, PlayerCharacter self)
    {
        orig(self);
        GayZoePlugin.tryGetMaterialInfo();
        GayZoePlugin.ApplyEffect(GayZoePlugin.currentEffect);
    }

    public static void updatePatch(On.PlayerCharacter.orig_Update orig, PlayerCharacter self)
    {
        orig(self);
        GayZoePlugin.internalTime+=Time.deltaTime*GayZoePlugin.CycleSpeed;
        if(GayZoePlugin.currentEffect == Effects.Cycle){
            GayZoePlugin.ApplyEffect();
        }
    }
}

public class ColorPreset{
    public Color mainColor;
    public Color accentColor;
    public Color highlightColor;
    public Color hairColor;
    public string title;
    public ColorPreset(Color m, Color a, Color h, Color hair, string title){
        this.mainColor = m;
        this.accentColor = a;
        this.highlightColor = h;
        this.title = title;
        this.hairColor = hair;
    }
    public ColorPreset(ColorPreset preset, string title){
        this.mainColor = preset.mainColor;
        this.accentColor = preset.accentColor;
        this.highlightColor = preset.highlightColor;
        this.title = title;
        this.hairColor = preset.hairColor;
    }
    public override string ToString(){
        return title+" Main: "+mainColor.ToString()+" Acc: "+accentColor.ToString()+" Highlight: "+highlightColor.ToString()+" Hair: "+hairColor.ToString()+" At index: "+ColorMenuController.instance.currentPreset;
    }

    public string SaveString(int isActive){
        //Preset_Name, main, 0-255, 0-255, 0-255, accent, 0-255, 0-255, 0-255, highlight, 0-255, 0-255, 0-255, (0,1)
        return title+", main, "+mainColor.r*255+", "+mainColor.g*255+", "+mainColor.b*255+", accent, "+accentColor.r*255+", "+accentColor.g*255+", "+accentColor.b*255+", highlight, "+highlightColor.r*255+", "+highlightColor.g*255+", "+highlightColor.b*255+", hair, "+hairColor.r*255+", "+hairColor.g*255+", "+hairColor.b*255+", "+isActive;

        
    }
}

public class GayZoeColorManager : MonoBehaviour{
    void Update(){
        GayZoePlugin.internalTime+=Time.deltaTime*GayZoePlugin.CycleSpeed;
        if(GayZoePlugin.currentEffect == Effects.Cycle){
            GayZoePlugin.ApplyEffect();
        }
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

    public Canvas renderCanvas;

    bool freeze=false;

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
        
    }

    public void innitValues(){
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
            GayZoePlugin.Back();
            renderCanvas.enabled = false;
            
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

        // UpdateColorDisplays();
        
        // gameObject.SetActive(false);
    }

    void Update(){
        if(Input.GetKeyDown(KeyCode.Escape)){
            if(renderCanvas.enabled==false)
            {
                return;
            }
            GayZoePlugin.Back();
            renderCanvas.enabled = false;
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
            using(StreamWriter sw = new StreamWriter(Path.Combine(GayZoePlugin.AssemblyDirectory, presets[i].title)+".color")){
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

    public void UpdateColorDisplays(){
        mainColorObj.GetComponent<Image>().color=presets[currentPreset].mainColor;
        accColorObj.GetComponent<Image>().color=presets[currentPreset].accentColor;
        highColorObj.GetComponent<Image>().color=presets[currentPreset].highlightColor;
        hairColorObj.GetComponent<Image>().color=presets[currentPreset].hairColor;
        switch(inspectMode){
            case InspectMode.Main:
                preview.GetComponent<Image>().color=presets[currentPreset].mainColor;
                break;
            case InspectMode.Accent:
                preview.GetComponent<Image>().color=presets[currentPreset].accentColor;
                break;
            case InspectMode.Highlight:
                preview.GetComponent<Image>().color=presets[currentPreset].highlightColor;
                break;
            case InspectMode.Hair:
                preview.GetComponent<Image>().color=presets[currentPreset].hairColor;
                break;
            default:
                Debug.LogError("InspectMode is not a valid value!");
                break;
        }

            UpdateSliders();
            UpdateInputs();
        
        
        // NewBehaviourScript.instance.swappythings(presets[currentPreset]);
        GayZoePlugin.ApplyEffect();
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



// [System.Serializable]
// public class ColorPreset{
//     [SerializeField]
//     public Color mainColor;
//     [SerializeField]
//     public Color accentColor;
//     [SerializeField]
//     public Color highlightColor;
//     [SerializeField]
//     public Color hairColor;
//     [SerializeField]
//     public string title;
//     public ColorPreset(Color m, Color a, Color h, Color hair, string title){
//         this.mainColor = m;
//         this.accentColor = a;
//         this.highlightColor = h;
//         this.title = title;
//         this.hairColor = hair;
//     }
//     public ColorPreset(ColorPreset preset, string title){
//         this.mainColor = preset.mainColor;
//         this.accentColor = preset.accentColor;
//         this.highlightColor = preset.highlightColor;
//         this.title = title;
//         this.hairColor = preset.hairColor;
//     }
// }


[HasteSetting]
public class ColorMode : EnumSetting<Effects>, IExposedSetting
{
    public override void ApplyValue()
    {
        GayZoePlugin.SetEffect(base.Value);
        
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
        ColorMenuController.instance.renderCanvas.enabled = (true);
        if(SceneManager.GetActiveScene().name=="MainMenu"){
            GayZoePlugin.disabledSettingsObj = GameObject.Find("EscapeMenu/SettingsPage/SettingsPage");
            GayZoePlugin.disabledSettingsObj.SetActive(false);
        }else
            GayZoePlugin.disabledSettingsObj = GameObject.Find("EscapeMenu/Enabled");
            GayZoePlugin.disabledSettingsObj.SetActive(false);
    }
}




[HasteSetting]
public class CycleSpeed : FloatSetting, IExposedSetting
{
    public override void ApplyValue()
    {
        GayZoePlugin.CycleSpeed=base.Value;
        
    }

    public string GetCategory()
    {
        return "Zoe ReColor";
    }

    public LocalizedString GetDisplayName()
    {
        return new UnlocalizedString("Gradients Cycle Speed");
    }

    protected override float GetDefaultValue()
    {
        return 1;
    }

    protected override float2 GetMinMaxValue()
    {
       return new float2(0,5);
    }
}


// class ReferenceValue<T>{
//     public T Value { get; set;}
//     public ReferenceValue(T value){
//         Value = value;
//     }
//     public static implicit operator T(ReferenceValue<T> item){
//         return item.Value;
//     }
// }



//legacy

[HasteSetting]
public class TintR : FloatSetting
{
    public override void ApplyValue()
    {
        GayZoePlugin.LegacyTintValues[0]=base.Value;
        // GayZoePlugin.LoadFromLegacy();
        // GayZoePlugin.ApplyEffect(GayZoePlugin.currentEffect);
    }

    public string GetCategory()
    {
        return "Zoe ReColor";
    }

    public LocalizedString GetDisplayName()
    {
        return new UnlocalizedString("Tint R");
    }

    protected override float GetDefaultValue()
    {
        return 255;
    }

    protected override float2 GetMinMaxValue()
    {
       return new float2(0,255);
    }
}
[HasteSetting]
public class TintG : FloatSetting
{
    public override void ApplyValue()
    {
        GayZoePlugin.LegacyTintValues[1]=base.Value;
        // GayZoePlugin.LoadFromLegacy();
    }

    public string GetCategory()
    {
        return "Zoe ReColor";
    }

    public LocalizedString GetDisplayName()
    {
        return new UnlocalizedString("Tint G");
    }

    protected override float GetDefaultValue()
    {
        return 255;
    }

    protected override float2 GetMinMaxValue()
    {
       return new float2(0,255);
    }
}
[HasteSetting]
public class TintB : FloatSetting
{
    public override void ApplyValue()
    {
        GayZoePlugin.LegacyTintValues[2]=base.Value;
        // GayZoePlugin.LoadFromLegacy();
        
    }

    public string GetCategory()
    {
        return "Zoe ReColor";
    }

    public LocalizedString GetDisplayName()
    {
        return new UnlocalizedString("Tint B");
    }

    protected override float GetDefaultValue()    
    {
        return 255;
    }

    protected override float2 GetMinMaxValue()
    {
       return new float2(0,255);
    }
    
}
