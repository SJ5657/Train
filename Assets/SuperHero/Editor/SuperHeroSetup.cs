using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SuperHero;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// SuperHero 메뉴: 애니메이션(근육 포즈 클립 + 컨트롤러)과 씬(SuperHeroCity)을 한 번에 구성한다.
/// 도시/건물은 플레이 시작 때 CityGenerator가 만든다.
/// </summary>
public static class SuperHeroSetup
{
    const string Root = "Assets/SuperHero";
    const string AnimDir = Root + "/Animations";
    const string MatDir = Root + "/Materials";
    public const string ScenePath = "Assets/Scenes/SuperHeroCity.unity";
    const string VrmPath = "Assets/Models/Hero_GIrl_YellowCape.vrm";
    const string ControllerPath = AnimDir + "/Hero.controller";
    const string PoolRoot = "Assets/SimplePoly City - Low Poly Assets/Prefab/";

    // PoseLab으로 잡아서 뽑은 근육값 (휴머노이드 머슬)
    const string SpecSuperman =
        "Chest Front-Back=0.15;Neck Nod Down-Up=0.375;Head Nod Down-Up=0.875;" +
        "Left Upper Leg Front-Back=0.713;Left Upper Leg In-Out=0.021;Left Lower Leg Stretch=1.028;Left Lower Leg Twist In-Out=-0.012;Left Foot Up-Down=0.526;Left Foot Twist In-Out=-0.026;" +
        "Right Upper Leg Front-Back=0.713;Right Upper Leg In-Out=0.021;Right Lower Leg Stretch=1.028;Right Lower Leg Twist In-Out=-0.012;Right Foot Up-Down=0.526;Right Foot Twist In-Out=-0.026;" +
        "Left Arm Down-Up=1.174;Left Arm Front-Back=-0.405;Left Arm Twist In-Out=0.069;Left Forearm Stretch=0.989;Left Forearm Twist In-Out=-0.06;Left Hand Down-Up=0.004;" +
        "Right Arm Down-Up=1.174;Right Arm Front-Back=-0.405;Right Arm Twist In-Out=0.069;Right Forearm Stretch=0.989;Right Forearm Twist In-Out=-0.06;Right Hand Down-Up=0.004;";

    const string SpecGuard =
        "Left Arm Down-Up=-0.135;Left Arm Front-Back=-0.065;Left Arm Twist In-Out=0.568;Left Forearm Stretch=-0.407;Left Forearm Twist In-Out=0.069;" +
        "Right Arm Down-Up=-0.135;Right Arm Front-Back=-0.065;Right Arm Twist In-Out=0.568;Right Forearm Stretch=-0.407;Right Forearm Twist In-Out=0.069;";

    const string SpecStrikeR =
        "Spine Left-Right=-0.008;Spine Twist Left-Right=-0.3;Chest Front-Back=-0.006;Chest Left-Right=0.047;Chest Twist Left-Right=-0.347;" +
        "Left Arm Down-Up=-0.196;Left Arm Front-Back=-0.178;Left Arm Twist In-Out=0.401;Left Forearm Stretch=-0.525;Left Forearm Twist In-Out=0.085;" +
        "Right Arm Down-Up=0.324;Right Arm Front-Back=-0.343;Right Arm Twist In-Out=-0.162;Right Forearm Stretch=0.966;Right Forearm Twist In-Out=0.142;Right Hand Down-Up=-0.008;Right Hand In-Out=0.004;";

    const string SpecStrikeL =
        "Spine Left-Right=0.008;Spine Twist Left-Right=0.3;Chest Front-Back=-0.006;Chest Left-Right=-0.047;Chest Twist Left-Right=0.347;" +
        "Left Arm Down-Up=0.324;Left Arm Front-Back=-0.343;Left Arm Twist In-Out=-0.162;Left Forearm Stretch=0.966;Left Forearm Twist In-Out=0.142;Left Hand Down-Up=-0.008;Left Hand In-Out=0.004;" +
        "Right Arm Down-Up=-0.196;Right Arm Front-Back=-0.178;Right Arm Twist In-Out=0.401;Right Forearm Stretch=-0.525;Right Forearm Twist In-Out=0.085;";

    const string SpecCast =
        "Left Arm Down-Up=0.261;Left Arm Front-Back=-0.54;Left Arm Twist In-Out=-0.228;Left Forearm Stretch=0.932;Left Forearm Twist In-Out=0.195;Left Hand Down-Up=-0.009;Left Hand In-Out=0.006;" +
        "Right Arm Down-Up=0.261;Right Arm Front-Back=-0.54;Right Arm Twist In-Out=-0.228;Right Forearm Stretch=0.932;Right Forearm Twist In-Out=0.195;Right Hand Down-Up=-0.009;Right Hand In-Out=0.006;";

    // ───────────────────────── 전체 빌드 ─────────────────────────

    [MenuItem("SuperHero/Build Scene (animations + new city scene)")]
    public static void BuildAll()
    {
        EnsureLayer("Hero", 12);
        AddAlwaysIncludedShaders("Standard", "Legacy Shaders/Particles/Alpha Blended", "Legacy Shaders/Particles/Additive", "Sprites/Default", "Unlit/Color", "Skybox/Procedural");
        BuildAnimations();
        BuildScene();
    }

    // ───────────────────────── 레이어 / 셰이더 ─────────────────────────

    static void EnsureLayer(string layerName, int index)
    {
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return;
        var sp = layers.GetArrayElementAtIndex(index);
        if (string.IsNullOrEmpty(sp.stringValue)) { sp.stringValue = layerName; tm.ApplyModifiedProperties(); }
        else Debug.LogWarning("[SuperHero] 레이어 슬롯 " + index + "가 이미 사용 중입니다: " + sp.stringValue);
    }

    static void AddAlwaysIncludedShaders(params string[] names)
    {
        var gs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var arr = gs.FindProperty("m_AlwaysIncludedShaders");
        foreach (var n in names)
        {
            var sh = Shader.Find(n);
            if (sh == null) continue;
            bool has = false;
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) { has = true; break; }
            if (has) continue;
            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
        }
        gs.ApplyModifiedProperties();
    }

    // ───────────────────────── 애니메이션 ─────────────────────────

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    static AnimationClip LoopCopy(string fbxName, string outName, bool loop)
    {
        string path = AssetDatabase.FindAssets(fbxName)
            .Select(AssetDatabase.GUIDToAssetPath)
            .FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == fbxName && p.ToLower().EndsWith(".fbx"));
        if (path == null) { Debug.LogError("[SuperHero] 애니메이션 FBX를 찾지 못함: " + fbxName); return null; }
        var src = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (src == null) { Debug.LogError("[SuperHero] 클립 없음: " + path); return null; }
        string outPath = AnimDir + "/" + outName + ".anim";
        AssetDatabase.DeleteAsset(outPath);
        var copy = Object.Instantiate(src);
        copy.name = outName;
        var st = AnimationUtility.GetAnimationClipSettings(copy);
        st.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(copy, st);
        AssetDatabase.CreateAsset(copy, outPath);
        return copy;
    }

    static Dictionary<string, float> ParseSpec(string spec)
    {
        var d = new Dictionary<string, float>();
        foreach (var part in spec.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=');
            d[kv[0].Trim()] = float.Parse(kv[1], CultureInfo.InvariantCulture);
        }
        return d;
    }

    /// <summary>근육 포즈 키프레임으로 휴머노이드 클립을 만든다. (time, spec) 쌍의 배열.</summary>
    static AnimationClip MuscleClip(string clipName, bool loop, Vector3 bodyPos, params object[] timeSpecPairs)
    {
        var clip = new AnimationClip { name = clipName, frameRate = 30f };
        var names = HumanTrait.MuscleName;
        var keys = new List<KeyValuePair<float, Dictionary<string, float>>>();
        for (int i = 0; i < timeSpecPairs.Length; i += 2)
            keys.Add(new KeyValuePair<float, Dictionary<string, float>>((float)timeSpecPairs[i], ParseSpec((string)timeSpecPairs[i + 1])));

        for (int m = 0; m < names.Length; m++)
        {
            var curve = new AnimationCurve();
            foreach (var k in keys)
            {
                float v; k.Value.TryGetValue(names[m], out v);
                curve.AddKey(new Keyframe(k.Key, v));
            }
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            clip.SetCurve("", typeof(Animator), names[m], curve);
        }
        float end = keys[keys.Count - 1].Key;
        SetConst(clip, "RootT.x", bodyPos.x, end); SetConst(clip, "RootT.y", bodyPos.y, end); SetConst(clip, "RootT.z", bodyPos.z, end);
        SetConst(clip, "RootQ.x", 0f, end); SetConst(clip, "RootQ.y", 0f, end); SetConst(clip, "RootQ.z", 0f, end); SetConst(clip, "RootQ.w", 1f, end);

        var st = AnimationUtility.GetAnimationClipSettings(clip);
        st.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, st);
        string outPath = AnimDir + "/" + clipName + ".anim";
        AssetDatabase.DeleteAsset(outPath);
        AssetDatabase.CreateAsset(clip, outPath);
        return clip;
    }

    static void SetConst(AnimationClip clip, string prop, float v, float end)
    {
        clip.SetCurve("", typeof(Animator), prop, new AnimationCurve(new Keyframe(0f, v), new Keyframe(Mathf.Max(end, 0.033f), v)));
    }

    [MenuItem("SuperHero/Rebuild Animations Only")]
    public static void BuildAnimations()
    {
        EnsureFolder(AnimDir);

        // 기준 몸 위치 (휴머노이드 RootT) 구하기
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VrmPath);
        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var h = new HumanPoseHandler(tmp.GetComponent<Animator>().avatar, tmp.transform);
        var pose = new HumanPose();
        h.GetHumanPose(ref pose);
        Vector3 bodyPos = pose.bodyPosition;
        h.Dispose();
        Object.DestroyImmediate(tmp);

        var idle = LoopCopy("MCU_am_Stand_Idle_05_HandsHips_Loop", "Hero_Idle", true);
        var walk = LoopCopy("MCU_af_Loco_Walk_Fwd_NoRM", "Hero_Walk", true);
        var run = LoopCopy("MCU_af_Loco_Run_Fwd_NoRM", "Hero_Run", true);
        var fall = LoopCopy("MCU_am_Jump_Fall_Loop", "Hero_Fall", true);
        var land = LoopCopy("MCU_am_Jump_LandRecovery", "Hero_Land", false);

        var fly = MuscleClip("Hero_FlySuperman", true, bodyPos, 0f, SpecSuperman, 1.2f, SpecSuperman);
        var punchR = MuscleClip("Hero_PunchR", false, bodyPos, 0f, SpecGuard, 0.09f, SpecStrikeR, 0.2f, SpecStrikeR, 0.40f, SpecGuard);
        var punchL = MuscleClip("Hero_PunchL", false, bodyPos, 0f, SpecGuard, 0.09f, SpecStrikeL, 0.2f, SpecStrikeL, 0.40f, SpecGuard);
        var cast = MuscleClip("Hero_Cast", true, bodyPos, 0f, SpecCast, 1f, SpecCast);

        // 상체 마스크
        string maskPath = AnimDir + "/UpperBody.mask";
        AssetDatabase.DeleteAsset(maskPath);
        var mask = new AvatarMask();
        foreach (AvatarMaskBodyPart part in System.Enum.GetValues(typeof(AvatarMaskBodyPart)))
        {
            if (part == AvatarMaskBodyPart.LastBodyPart) continue;
            bool on = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                   || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers
                   || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
            mask.SetHumanoidBodyPartActive(part, on);
        }
        AssetDatabase.CreateAsset(mask, maskPath);

        // 컨트롤러
        AssetDatabase.DeleteAsset(ControllerPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("FlySpeed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Flying", AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        ctrl.parameters = ctrl.parameters.Select(p => { if (p.name == "Grounded") p.defaultBool = true; return p; }).ToArray();

        var sm = ctrl.layers[0].stateMachine;
        BlendTree locoTree;
        var loco = ctrl.CreateBlendTreeInController("Locomotion", out locoTree, 0);
        locoTree.blendType = BlendTreeType.Simple1D;
        locoTree.blendParameter = "Speed";
        locoTree.useAutomaticThresholds = false;
        locoTree.AddChild(idle, 0f);
        locoTree.AddChild(walk, 0.42f);
        locoTree.AddChild(run, 1f);
        locoTree.AddChild(run, 2.9f);
        var ch = locoTree.children;
        ch[1].timeScale = 1.35f;
        ch[3].timeScale = 1.6f;
        locoTree.children = ch;

        var air = sm.AddState("Air");
        air.motion = fall;

        BlendTree flyTree;
        var flyState = ctrl.CreateBlendTreeInController("Fly", out flyTree, 0);
        flyTree.blendType = BlendTreeType.Simple1D;
        flyTree.blendParameter = "FlySpeed";
        flyTree.useAutomaticThresholds = false;
        flyTree.AddChild(idle, 0f);
        flyTree.AddChild(fly, 0.55f);
        flyTree.AddChild(fly, 1f);

        var slamLand = sm.AddState("SlamLand");
        slamLand.motion = land;

        sm.defaultState = loco;

        AddTrans(loco, air, 0.08f, Cond("Grounded", false), Cond("Flying", false));
        AddTrans(air, loco, 0.1f, Cond("Grounded", true));
        AddTrans(loco, flyState, 0.2f, Cond("Flying", true));
        AddTrans(air, flyState, 0.2f, Cond("Flying", true));
        AddTrans(flyState, loco, 0.25f, Cond("Flying", false), Cond("Grounded", true));
        AddTrans(flyState, air, 0.25f, Cond("Flying", false), Cond("Grounded", false));
        var sl = AddTrans(slamLand, loco, 0.2f);
        sl.hasExitTime = true; sl.exitTime = 0.85f;
        var slFly = AddTrans(slamLand, flyState, 0.15f, Cond("Flying", true));
        slFly.hasExitTime = false;

        // 상체 오버라이드 레이어
        ctrl.AddLayer("Upper");
        var layers = ctrl.layers;
        layers[1].defaultWeight = 0f;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
        layers[1].avatarMask = mask;
        ctrl.layers = layers;
        var usm = ctrl.layers[1].stateMachine;
        var empty = usm.AddState("Empty");
        var sPR = usm.AddState("PunchR"); sPR.motion = punchR;
        var sPL = usm.AddState("PunchL"); sPL.motion = punchL;
        var sCast = usm.AddState("Cast"); sCast.motion = cast;
        usm.defaultState = empty;
        var t1 = AddTrans(sPR, empty, 0.08f); t1.hasExitTime = true; t1.exitTime = 0.9f;
        var t2 = AddTrans(sPL, empty, 0.08f); t2.hasExitTime = true; t2.exitTime = 0.9f;

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        Debug.Log("[SuperHero] 애니메이션 빌드 완료 (fly humanoid=" + fly.isHumanMotion + ", punch humanoid=" + punchR.isHumanMotion + ")");
    }

    static AnimatorCondition Cond(string param, bool v)
    {
        return new AnimatorCondition { parameter = param, mode = v ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, threshold = 0f };
    }

    static AnimatorStateTransition AddTrans(AnimatorState from, AnimatorState to, float dur, params AnimatorCondition[] conds)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = dur;
        t.hasFixedDuration = true;
        foreach (var c in conds) t.AddCondition(c.mode, c.threshold, c.parameter);
        return t;
    }

    // ───────────────────────── 씬 ─────────────────────────

    static GameObject[] LoadPrefabs(params string[] relPaths)
    {
        return relPaths.Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(PoolRoot + p)).Where(g => g != null).ToArray();
    }

    [MenuItem("SuperHero/Rebuild Scene Only")]
    public static void BuildScene()
    {
        EnsureFolder(MatDir);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 하늘 / 안개 / 앰비언트
        string skyPath = MatDir + "/HeroSky.mat";
        AssetDatabase.DeleteAsset(skyPath);
        var sky = new Material(Shader.Find("Skybox/Procedural"));
        sky.SetFloat("_SunSize", 0.045f);
        sky.SetFloat("_SunSizeConvergence", 6f);
        sky.SetFloat("_AtmosphereThickness", 0.85f);
        sky.SetFloat("_Exposure", 1.25f);
        sky.SetColor("_SkyTint", new Color(0.52f, 0.58f, 0.68f));
        sky.SetColor("_GroundColor", new Color(0.62f, 0.66f, 0.7f));
        AssetDatabase.CreateAsset(sky, skyPath);
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.05f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.74f, 0.82f, 0.92f);
        RenderSettings.fogStartDistance = 700f;
        RenderSettings.fogEndDistance = 3800f;

        // 태양
        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.96f, 0.88f);
        sun.intensity = 1.2f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sunGo.transform.rotation = Quaternion.Euler(46f, -38f, 0f);
        RenderSettings.sun = sun;

        // 부트스트랩 / 매니저
        new GameObject("SceneBootstrap").AddComponent<SceneBootstrap>();
        new GameObject("DestructionManager").AddComponent<DestructionManager>();
        new GameObject("FX").AddComponent<Fx>();

        // 도시
        var cityGo = new GameObject("City");
        var city = cityGo.AddComponent<SuperCityGenerator>();
        city.carPrefabs = LoadPrefabs(
            "Vehicles/Vehicle with Static Wheels/Vehicle_Car_color01.prefab", "Vehicles/Vehicle with Static Wheels/Vehicle_Car_color02.prefab", "Vehicles/Vehicle with Static Wheels/Vehicle_Car_color03.prefab",
            "Vehicles/Vehicle with Static Wheels/Vehicle_SUV_color01.prefab", "Vehicles/Vehicle with Static Wheels/Vehicle_SUV_color02.prefab", "Vehicles/Vehicle with Static Wheels/Vehicle_SUV_color03.prefab",
            "Vehicles/Vehicle with Static Wheels/Vehicle_Taxi.prefab", "Vehicles/Vehicle with Static Wheels/Vehicle_Pick up Truck_color01.prefab", "Vehicles/Vehicle with Static Wheels/Vehicle_Police Car.prefab",
            "Vehicles/Vehicle with Static Wheels/Vehicle_Bus_color01.prefab", "Vehicles/Vehicle with Static Wheels/Vehicle_Truck_color01.prefab");
        city.treePrefabs = LoadPrefabs("Natures/Natures_Big Tree.prefab", "Natures/Natures_Cube Tree.prefab", "Natures/Natures_Fir Tree.prefab");
        city.lightPrefabs = LoadPrefabs("Props/Props_Street Light.prefab");

        // 카메라
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 65f;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 6000f;
        camGo.AddComponent<AudioListener>();
        var heroCam = camGo.AddComponent<HeroCamera>();
        heroCam.distance = 5.2f;
        heroCam.shoulder = 0.5f;

        // 영웅
        var hero = new GameObject("Hero");
        hero.transform.position = new Vector3(0f, 1.2f, 0f);
        var cc = hero.AddComponent<CharacterController>();
        var hc = hero.AddComponent<HeroController>();
        hero.AddComponent<HeroSkills>();
        hero.AddComponent<HeroHUD>();

        var pivot = new GameObject("ModelPivot");
        pivot.transform.SetParent(hero.transform, false);
        pivot.transform.localPosition = new Vector3(0f, 0.95f, 0f);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VrmPath);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pivot.transform);
        model.name = "Hero_GIrl_YellowCape";
        model.transform.localPosition = new Vector3(0f, -0.95f, 0f);
        model.transform.localRotation = Quaternion.identity;
        var anim = model.GetComponent<Animator>();
        anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        anim.applyRootMotion = false;

        hc.modelPivot = pivot.transform;
        hc.animator = anim;
        hc.cam = heroCam;
        heroCam.target = hero.transform;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("[SuperHero] 씬 생성 완료: " + ScenePath);
    }
}
