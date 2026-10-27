using System.Collections.Generic;
using System.Linq;
using TrainGame;
using UniVRM10;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// TrainGame 메뉴: 기관사 애니메이션 컨트롤러와 기차 게임 씬(TrainGame.unity)을 한 번에 만든다.
/// 순환 선로(직선 + 반경 200m 곡선)를 에셋의 직선 선로 메쉬를 휘어 깔고, 역 4곳·마을·숲·산·열차·기관사를 배치한다.
/// </summary>
public static class TrainGameSetup
{
    const string Root = "Assets/TrainGame";
    const string AnimDir = Root + "/Animations";
    const string GenDir = Root + "/Generated";
    const string MatDir = Root + "/Materials";
    public const string ScenePath = "Assets/Scenes/TrainGame.unity";
    // 기관사 모자를 씌운 복제본 (원본 TrainDriverFemale.vrm, 남색 모자 _Cap.vrm 은 그대로 둔다)
    // _Cap2: 모자 색 = 치마 색, 크기 8% 축소 (Tools/build_cap.js size 0.92)
    const string VrmPath = "Assets/Models/TrainDriverFemale_Cap2.vrm";
    const string ControllerPath = AnimDir + "/Driver.controller";
    const string TrainRoot = "Assets/Steam Trains and Stations (Low Poly)/";
    const string StationPrefabs = TrainRoot + "Prefabs/Railway Station PM/";
    const string SimplePoly = "Assets/SimplePoly City - Low Poly Assets/Prefab/";
    const string Megapolis = "Assets/LowPolyAssetBundle/LowPolyMegapolis/Prefabs/";
    const int PlayerLayer = 13;

    // 선로 다각형 (XZ, 반시계 진행). 진행방향 오른쪽 = 순환선 바깥.
    static readonly Vector2[] Corners =
    {
        new Vector2(-300, -250), new Vector2(450, -250), new Vector2(650, -50), new Vector2(650, 350),
        new Vector2(300, 550), new Vector2(-150, 550), new Vector2(-450, 300), new Vector2(-450, -50)
    };
    const float CurveRadius = 200f;

    // 승강장 기하 (RailwayPlatformCanopy_CS01 실측): 바닥 윗면 로컬 y=2.07, 로컬 z -3.4..6.0
    const float PlatformTop = 0.92f;
    const float PlatformLateral = 5.05f;     // 선로 중심 → 승강장 프리팹 원점
    const float PlatformHalf = 17.015f;      // 승강장 프리팹 길이의 절반
    const float PlatformBehindStop = 26f;    // 정지 위치 → 승강장 중앙 (뒤쪽)

    // 열차 (Large_Steam_Train 프리팹 실측)
    const float LocoHeight = 0.69f, CarHeight = 0.61f;
    const float LocoFront = 5.6f;

    struct StationDef
    {
        public string name, theme;
        public Vector3 approx;
        public StationDef(string n, string t, float x, float z) { name = n; theme = t; approx = new Vector3(x, 0f, z); }
    }

    static readonly StationDef[] StationDefs =
    {
        new StationDef("중앙역", "town", 80f, -250f),
        new StationDef("솔숲역", "forest", 650f, 200f),
        new StationDef("들꽃역", "field", -20f, 550f),
        new StationDef("바위산역", "rock", -450f, 40f),
    };

    struct Zone { public Vector3 c; public float r; public Zone(Vector3 c, float r) { this.c = c; this.r = r; } }

    // ───────────────────────── 메뉴 ─────────────────────────

    [MenuItem("TrainGame/Build Scene (animations + train scene)")]
    public static void BuildAll()
    {
        EnsureLayer("Player", PlayerLayer);
        BuildAnimations();
        BuildScene();
    }

    [MenuItem("TrainGame/Rebuild Animations Only")]
    public static void BuildAnimations()
    {
        EnsureFolder(AnimDir);
        var idle = LoopCopy("MCU_af_Stand_Idle_01", "Driver_Idle");
        var walk = LoopCopy("MCU_af_Loco_Walk_Fwd_NoRM", "Driver_Walk");
        var run = LoopCopy("MCU_af_Loco_Run_Fwd_NoRM", "Driver_Run");
        var fall = LoopCopy("MCU_am_Jump_Fall_Loop", "Driver_Fall");

        AssetDatabase.DeleteAsset(ControllerPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        ctrl.parameters = ctrl.parameters.Select(p => { if (p.name == "Grounded") p.defaultBool = true; return p; }).ToArray();

        var sm = ctrl.layers[0].stateMachine;
        BlendTree tree;
        var loco = ctrl.CreateBlendTreeInController("Locomotion", out tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(idle, 0f);
        tree.AddChild(walk, 1.6f);
        tree.AddChild(run, 5.0f);
        var air = sm.AddState("Air");
        air.motion = fall;
        sm.defaultState = loco;

        var t1 = loco.AddTransition(air); t1.hasExitTime = false; t1.duration = 0.12f; t1.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
        var t2 = air.AddTransition(loco); t2.hasExitTime = false; t2.duration = 0.12f; t2.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        Debug.Log("[TrainGame] 기관사 애니메이션 컨트롤러 생성: " + ControllerPath);
    }

    // ───────────────────────── 씬 ─────────────────────────

    public static void BuildScene()
    {
        EnsureFolder(MatDir);
        if (AssetDatabase.IsValidFolder(GenDir)) AssetDatabase.DeleteAsset(GenDir);
        EnsureFolder(GenDir);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var rnd = new System.Random(20261027);

        SetupEnvironment();
        var track = BuildTrack();
        BuildGround();

        // 역
        var stations = new List<Station>();
        var zones = new List<Zone>();
        var stationsRoot = new GameObject("Stations").transform;
        for (int i = 0; i < StationDefs.Length; i++)
        {
            var def = StationDefs[i];
            float stopS = track.ClosestS(def.approx);
            stations.Add(new Station { name = def.name, stopS = stopS });
            BuildStation(track, def, i, stopS, stationsRoot, rnd);
            float mid = stopS - PlatformBehindStop;
            zones.Add(new Zone(track.GetPosition(mid) - track.GetLeft(mid) * 18f, 62f));
        }

        BuildScenery(track, stations, zones, rnd);
        var train = BuildTrain(track, stations[0].stopS);
        var driver = BuildDriver(track, train, stations[0].stopS);
        var cam = BuildCamera(driver);
        driver.cam = cam;

        var game = new GameObject("Game");
        var route = game.AddComponent<RouteManager>();
        route.train = train;
        route.track = track;
        route.stations = stations.ToArray();
        route.nextIndex = 1;
        var hud = game.AddComponent<TrainHUD>();
        hud.train = train;
        hud.route = route;
        hud.driver = driver;

        EditorSceneManager.SaveScene(scene, ScenePath);
        var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        Debug.Log("[TrainGame] 씬 생성 완료: " + ScenePath + "  선로 길이 " + track.length.ToString("F0") + " m");
    }

    // ───────────────────────── 환경 ─────────────────────────

    static void SetupEnvironment()
    {
        string skyPath = MatDir + "/TrainSky.mat";
        AssetDatabase.DeleteAsset(skyPath);
        var sky = new Material(Shader.Find("Skybox/Procedural"));
        sky.SetFloat("_SunSize", 0.04f);
        sky.SetFloat("_SunSizeConvergence", 5f);
        sky.SetFloat("_AtmosphereThickness", 0.9f);
        sky.SetFloat("_Exposure", 1.2f);
        sky.SetColor("_SkyTint", new Color(0.5f, 0.6f, 0.72f));
        sky.SetColor("_GroundColor", new Color(0.55f, 0.6f, 0.55f));
        AssetDatabase.CreateAsset(sky, skyPath);
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.05f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.72f, 0.8f, 0.88f);
        RenderSettings.fogStartDistance = 350f;
        RenderSettings.fogEndDistance = 3600f;

        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.95f, 0.85f);
        sun.intensity = 1.15f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.8f;
        sunGo.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
        RenderSettings.sun = sun;
    }

    static Texture2D NoiseTexture(string name, Color a, Color b, float scale, int seed)
    {
        string path = GenDir + "/" + name + ".asset";
        const int N = 256;
        var tex = new Texture2D(N, N, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
        var px = new Color[N * N];
        float o = seed * 17.3f;
        // 이음새 없이 반복되는 펄린: 네 귀퉁이 샘플을 위치 가중치로 섞는다
        float Tile(float x, float y, float f)
        {
            float a = Mathf.PerlinNoise(o + x * f, o + y * f), b = Mathf.PerlinNoise(o + (x - N) * f, o + y * f);
            float c = Mathf.PerlinNoise(o + x * f, o + (y - N) * f), d = Mathf.PerlinNoise(o + (x - N) * f, o + (y - N) * f);
            float u = x / N, v = y / N;
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float n = 0f, amp = 0.5f, f = scale * 4f / N, norm = 0f;
                for (int k = 0; k < 4; k++)
                {
                    n += amp * Tile(x, y, f);
                    norm += amp;
                    amp *= 0.5f; f *= 2f;
                }
                n /= norm;
                n = (n - 0.5f) * 1.8f + 0.5f;   // 섞으면서 줄어든 대비 보정
                float grain = ((x * 73856093 ^ y * 19349663 ^ seed) & 255) / 255f;
                px[y * N + x] = Color.Lerp(a, b, Mathf.Clamp01(n * 1.1f - 0.05f)) * (0.93f + grain * 0.1f);
            }
        tex.SetPixels(px);
        tex.Apply(true);
        AssetDatabase.CreateAsset(tex, path);
        return tex;
    }

    static Material MakeMat(string name, Color color, Texture tex = null, float tiling = 1f, float smooth = 0.05f)
    {
        string path = MatDir + "/" + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("Standard"));
        m.color = color;
        m.SetFloat("_Glossiness", smooth);
        if (tex != null) { m.mainTexture = tex; m.mainTextureScale = new Vector2(tiling, tiling); }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void BuildGround()
    {
        var grassTex = NoiseTexture("GrassNoise", new Color(0.33f, 0.47f, 0.2f), new Color(0.5f, 0.6f, 0.28f), 1.6f, 3);
        var mat = MakeMat("Ground", Color.white, grassTex, 700f);
        var g = GameObject.CreatePrimitive(PrimitiveType.Plane);
        g.name = "Ground";
        g.transform.position = new Vector3(100f, 0f, 150f);
        g.transform.localScale = new Vector3(700f, 1f, 700f);
        g.GetComponent<MeshRenderer>().sharedMaterial = mat;
        MarkStatic(g);
    }

    // ───────────────────────── 선로 ─────────────────────────

    static TrackPath BuildTrack()
    {
        Vector3[] pts; byte[] lims; float len;
        TrackPath.BuildFilleted(Corners, CurveRadius, 1f, 60, 90, 60f, 50f, out pts, out lims, out len);
        var go = new GameObject("Track");
        var tp = go.AddComponent<TrackPath>();
        tp.points = pts;
        tp.limits = lims;
        tp.length = len;
        tp.Init();

        // 에셋의 직선 선로(RailwayRoad, 길이 5.53m)를 곡선을 따라 휘어서 이어 붙인다
        var road = AssetDatabase.LoadAssetAtPath<GameObject>(TrainRoot + "Prefabs/Railway Road Parts PM/RailwayRoad.prefab");
        var src = road.GetComponent<MeshFilter>().sharedMesh;
        var roadMat = road.GetComponent<MeshRenderer>().sharedMaterial;
        Vector3[] sv = src.vertices, sn = src.normals;
        Vector2[] suv = src.uv;
        int[] stri = src.triangles;
        float xmin = src.bounds.min.x, pieceLen = src.bounds.size.x;
        int pieces = Mathf.RoundToInt(len / pieceLen);
        float pl = len / pieces, sc = pl / pieceLen;
        const int perChunk = 40;

        for (int c0 = 0, ci = 0; c0 < pieces; c0 += perChunk, ci++)
        {
            int cnt = Mathf.Min(perChunk, pieces - c0);
            Vector3 origin = tp.GetPosition(c0 * pl);
            int nv = sv.Length;
            var V = new Vector3[cnt * nv];
            var Nn = new Vector3[cnt * nv];
            var UV = new Vector2[cnt * nv];
            var T = new int[cnt * stri.Length];
            for (int p = 0; p < cnt; p++)
            {
                float s0 = (c0 + p) * pl;
                for (int v = 0; v < nv; v++)
                {
                    float s = s0 + (sv[v].x - xmin) * sc;
                    Vector3 P = tp.GetPosition(s), tg = tp.GetTangent(s);
                    Vector3 L = Vector3.Cross(tg, Vector3.up).normalized;
                    V[p * nv + v] = P + L * sv[v].z + Vector3.up * sv[v].y - origin;
                    Nn[p * nv + v] = (tg * sn[v].x + Vector3.up * sn[v].y + L * sn[v].z).normalized;
                    UV[p * nv + v] = suv[v];
                }
                for (int t = 0; t < stri.Length; t++) T[p * stri.Length + t] = stri[t] + p * nv;
            }
            var mesh = new Mesh { name = "Track_" + ci };
            if (V.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = V;
            mesh.normals = Nn;
            mesh.uv = UV;
            mesh.triangles = T;
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, GenDir + "/Track_" + ci + ".asset");

            var chunk = new GameObject("TrackChunk_" + ci);
            chunk.transform.SetParent(go.transform, false);
            chunk.transform.position = origin;
            chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = chunk.AddComponent<MeshRenderer>();
            mr.sharedMaterial = roadMat;
            MarkStatic(chunk);
        }

        // 자갈 도상 (선로 밑 띠)
        var ballastTex = NoiseTexture("BallastNoise", new Color(0.3f, 0.28f, 0.26f), new Color(0.52f, 0.49f, 0.45f), 6f, 11);
        var ballastMat = MakeMat("Ballast", Color.white, ballastTex, 1f);
        float[] bx = { -2.9f, -2.05f, 2.05f, 2.9f };
        float[] by = { -0.03f, 0.06f, 0.06f, -0.03f };
        int rows = Mathf.CeilToInt(len / 2f);
        var bv = new Vector3[(rows + 1) * 4];
        var buv = new Vector2[(rows + 1) * 4];
        var bt = new List<int>();
        for (int r = 0; r <= rows; r++)
        {
            float s = r * len / rows;
            Vector3 P = tp.GetPosition(s), L = tp.GetLeft(s);
            for (int k = 0; k < 4; k++)
            {
                bv[r * 4 + k] = P + L * bx[k] + Vector3.up * by[k];
                buv[r * 4 + k] = new Vector2(k / 3f * 1.5f, s / 3f);
            }
            if (r == rows) break;
            for (int k = 0; k < 3; k++)
            {
                int a = r * 4 + k, b = a + 1, c = a + 4, d = a + 5;
                bt.Add(a); bt.Add(c); bt.Add(b);
                bt.Add(b); bt.Add(c); bt.Add(d);
            }
        }
        var bm = new Mesh { name = "Ballast", indexFormat = IndexFormat.UInt32 };
        bm.vertices = bv;
        bm.uv = buv;
        bm.triangles = bt.ToArray();
        bm.RecalculateNormals();
        bm.RecalculateBounds();
        AssetDatabase.CreateAsset(bm, GenDir + "/Ballast.asset");
        var bgo = new GameObject("Ballast");
        bgo.transform.SetParent(go.transform, false);
        bgo.AddComponent<MeshFilter>().sharedMesh = bm;
        bgo.AddComponent<MeshRenderer>().sharedMaterial = ballastMat;
        MarkStatic(bgo);

        return tp;
    }

    // ───────────────────────── 역 ─────────────────────────

    static void BuildStation(TrackPath tp, StationDef def, int index, float stopS, Transform parent, System.Random rnd)
    {
        var root = new GameObject(def.name).transform;
        root.SetParent(parent, false);

        float mid = stopS - PlatformBehindStop;
        Vector3 T = tp.GetTangent(mid), R = -tp.GetLeft(mid);
        Quaternion outward = Quaternion.LookRotation(R, Vector3.up);   // 로컬 +Z = 바깥, 로컬 X = -T
        Vector3 Ground(float s, float lateral) { return tp.GetPosition(s) + R * lateral; }

        // 승강장 (지붕 포함) 2칸 = 68m
        var canopy = Load(StationPrefabs + "RailwayPlatformCanopy_CS0" + (index % 4 + 1) + ".prefab");
        foreach (float off in new[] { -PlatformHalf, PlatformHalf })
        {
            var g = Place(canopy, root, Ground(mid + off, PlatformLateral) + Vector3.up * (PlatformTop - 2.07f), outward);
            AddMeshColliders(g);
        }

        // 양 끝 경사로
        var concrete = MakeMatCached("PlatformRamp", new Color(0.62f, 0.6f, 0.57f));
        foreach (float dir in new[] { -1f, 1f })
        {
            float sEnd = mid + dir * PlatformHalf * 2f;
            Vector3 p = Ground(sEnd, 8.2f);
            var ramp = Ramp(root, p, Quaternion.LookRotation(T * dir, Vector3.up), 4.2f, 6f, PlatformTop, concrete);
            MarkStatic(ramp);
        }

        // 역사 건물 (바닥이 승강장 높이와 맞도록 y=1.81)
        var building = Load(StationPrefabs + "RailwayStation_CS0" + (index % 4 + 1) + ".prefab");
        var bg = Place(building, root, Ground(mid, 14.95f) - T * 10.96f + Vector3.up * 1.81f, outward);
        AddMeshColliders(bg);

        // 정지 위치 표지
        Vector3 stopPos = Ground(stopS, 2.3f) + Vector3.up * PlatformTop;
        Sign(root, "정지", stopPos, R, new Vector2(0.9f, 0.6f), 2.2f, new Color(0.85f, 0.12f, 0.1f), Color.white, 0.075f);
        // 바닥 정지선
        var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(line.GetComponent<Collider>());
        line.name = "StopLine";
        line.transform.SetParent(root, false);
        line.transform.SetPositionAndRotation(Ground(stopS, 2.4f) + Vector3.up * (PlatformTop + 0.005f), outward);
        line.transform.localScale = new Vector3(0.25f, 0.01f, 1.4f);
        line.GetComponent<MeshRenderer>().sharedMaterial = MakeMatCached("StopLine", new Color(0.95f, 0.85f, 0.15f));

        // 역 이름판 (선로에서 읽히도록)
        foreach (float off in new[] { -14f, 14f })
            Sign(root, def.name, Ground(mid + off, 3.2f) + Vector3.up * PlatformTop, R, new Vector2(3.4f, 0.9f), 2.6f, new Color(0.12f, 0.2f, 0.32f), Color.white, 0.12f);

        // 벤치
        var bench = Load(SimplePoly + "Props/Props_Bench_1.prefab");
        foreach (float off in new[] { -22f, -6f, 8f })
            MarkStatic(Place(bench, root, Ground(mid + off, 9.2f) + Vector3.up * PlatformTop, Quaternion.LookRotation(-R, Vector3.up)));

        // 신호기, 종
        var signal = Load(TrainRoot + "Models/TrafficSign/TrainTrafficLightType1.fbx");
        if (signal != null) { var g = Place(signal, root, Ground(stopS + 18f, 2.6f), Quaternion.LookRotation(-T, Vector3.up)); FixBrokenMaterials(g); MarkStatic(g); }
        var bell = Load(TrainRoot + "Models/TrafficSign/BellOnGround.fbx");
        if (bell != null) { var g = Place(bell, root, Ground(mid + PlatformHalf * 2f + 9f, 9f), outward); FixBrokenMaterials(g); MarkStatic(g); }
    }

    static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();

    static Material MakeMatCached(string name, Color c)
    {
        Material m;
        if (matCache.TryGetValue(name, out m) && m != null) return m;
        m = MakeMat(name, c);
        matCache[name] = m;
        return m;
    }

    static GameObject Ramp(Transform parent, Vector3 pos, Quaternion rot, float width, float length, float height, Material mat)
    {
        float w = width * 0.5f, z0 = -0.3f;
        var v = new List<Vector3>();
        var t = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
        void Tri(Vector3 a, Vector3 b, Vector3 c) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i + 1); t.Add(i + 2); }
        // 경사면, 양 옆면
        Quad(new Vector3(-w, height, z0), new Vector3(w, height, z0), new Vector3(w, 0f, length), new Vector3(-w, 0f, length));
        Tri(new Vector3(-w, 0f, z0), new Vector3(-w, height, z0), new Vector3(-w, 0f, length));
        Tri(new Vector3(w, height, z0), new Vector3(w, 0f, z0), new Vector3(w, 0f, length));
        var mesh = new Mesh { name = "Ramp" };
        mesh.SetVertices(v);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(GenDir + "/Ramp.asset"));
        var go = new GameObject("Ramp");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    /// <summary>기둥 + 판 + 글자. 글자는 선로 쪽(-outwardDir)에서 읽힌다.</summary>
    static void Sign(Transform parent, string text, Vector3 basePos, Vector3 outwardDir, Vector2 board, float height, Color boardColor, Color textColor, float charSize)
    {
        var root = new GameObject("Sign_" + text).transform;
        root.SetParent(parent, false);
        root.SetPositionAndRotation(basePos, Quaternion.LookRotation(outwardDir, Vector3.up));
        var poleMat = MakeMatCached("SignPole", new Color(0.18f, 0.18f, 0.2f));
        var boardMat = MakeMatCached("SignBoard_" + ColorUtility.ToHtmlStringRGB(boardColor), boardColor);
        float[] poles = board.x > 1.5f ? new[] { -board.x * 0.4f, board.x * 0.4f } : new[] { 0f };
        foreach (float px in poles)
        {
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(root, false);
            pole.transform.localPosition = new Vector3(px, height * 0.5f, 0.06f);
            pole.transform.localScale = new Vector3(0.08f, height * 0.5f, 0.08f);
            pole.GetComponent<MeshRenderer>().sharedMaterial = poleMat;
            Object.DestroyImmediate(pole.GetComponent<Collider>());
        }
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        b.name = "Board";
        b.transform.SetParent(root, false);
        b.transform.localPosition = new Vector3(0f, height - board.y * 0.5f, 0f);
        b.transform.localScale = new Vector3(board.x, board.y, 0.06f);
        b.GetComponent<MeshRenderer>().sharedMaterial = boardMat;
        Object.DestroyImmediate(b.GetComponent<Collider>());

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var textMat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/SignText.mat");
        if (textMat == null)
        {
            textMat = new Material(Shader.Find("TrainGame/Text3D"));
            if (font != null) textMat.mainTexture = font.material.mainTexture;
            AssetDatabase.CreateAsset(textMat, MatDir + "/SignText.mat");
        }
        // 선로 쪽 면 (로컬 -Z) 과 반대쪽 면 둘 다
        foreach (float side in new[] { -1f, 1f })
        {
            var tgo = new GameObject("Text");
            tgo.transform.SetParent(root, false);
            tgo.transform.localPosition = new Vector3(0f, height - board.y * 0.5f, side * 0.035f);
            tgo.transform.localRotation = side < 0f ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
            var tm = tgo.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = font;
            tm.fontSize = 64;
            tm.characterSize = charSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = textColor;
            tgo.GetComponent<MeshRenderer>().sharedMaterial = textMat;
            tgo.AddComponent<SignText>();
        }
    }

    // ───────────────────────── 풍경 ─────────────────────────

    static void BuildScenery(TrackPath tp, List<Station> stations, List<Zone> zones, System.Random rnd)
    {
        var root = new GameObject("Scenery").transform;
        float R01() { return (float)rnd.NextDouble(); }
        float Range(float a, float b) { return a + (b - a) * R01(); }

        var trees = Enumerable.Range(1, 8).Select(i => Load(Megapolis + "Nature/Tree_0" + i + ".prefab")).Where(g => g != null).ToArray();
        var firs = new[] { Load(SimplePoly + "Natures/Natures_Fir Tree.prefab"), Load(Megapolis + "Nature/Tree_07.prefab") }.Where(g => g != null).ToArray();
        var bushes = new[] { "Natures/Natures_Bush_01", "Natures/Natures_Bush_02", "Natures/Natures_Bush_03" }.Select(n => Load(SimplePoly + n + ".prefab")).Where(g => g != null).ToArray();
        var grass = Enumerable.Range(1, 3).Select(i => Load(Megapolis + "Nature/Grass_0" + i + ".prefab")).Where(g => g != null).ToArray();
        var rocks = Enumerable.Range(1, 5).Select(i => Load(Megapolis + "Nature/Rock_0" + i + ".prefab")).Where(g => g != null).ToArray();
        var hills = Enumerable.Range(1, 4).Select(i => Load(Megapolis + "Nature/Hill_0" + i + ".prefab")).Where(g => g != null).ToArray();
        var mountains = Enumerable.Range(1, 3).Select(i => Load(Megapolis + "Nature/Mountain_0" + i + ".prefab")).Where(g => g != null).ToArray();

        var occupied = new List<Zone>(zones);
        bool Free(Vector3 p, float trackClear, float selfR)
        {
            float d;
            tp.ClosestS(p, out d);
            if (d < trackClear) return false;
            foreach (var z in occupied) if ((p - z.c).sqrMagnitude < (z.r + selfR) * (z.r + selfR)) return false;
            return true;
        }
        GameObject Put(GameObject prefab, Transform parent, Vector3 p, float scale, float yaw)
        {
            var g = Place(prefab, parent, p, Quaternion.Euler(0f, yaw, 0f));
            g.transform.localScale *= scale;
            MarkStatic(g);
            return g;
        }

        // 마을 (중앙역 남쪽)
        var town = new GameObject("Town").transform; town.SetParent(root, false);
        string[] shops = { "Building_Bakery", "Building_Coffee Shop", "Building_Books Shop", "Building_Restaurant", "Building_Pizza", "Building_Drug Store", "Building_Gift Shop", "Building_Clothing", "Building_Music Store", "Building_Fruits  Shop", "Building_Super Market", "Building_Bar" };
        string[] houses = { "Building_House_01_color01", "Building_House_02_color02", "Building_House_03_color03", "Building_House_04_color01", "Building_House_01_color03", "Building_House_02_color01", "Building_Residential_color01", "Building_Residential_color02", "Building_House_04_color02", "Building_House_03_color01" };
        var stLight = Load(SimplePoly + "Props/Props_Street Light.prefab");
        int si = 0, hi = 0;
        for (float x = -150f; x <= 270f; x += 24f)
        {
            if (x > 10f && x < 100f) continue;   // 역 앞 광장
            var shop = Load(SimplePoly + "Buildings/" + shops[si++ % shops.Length] + ".prefab");
            if (shop != null) { var g = Put(shop, town, new Vector3(x, 0f, -303f), 1f, 180f); AddMeshColliders(g); }
            var house = Load(SimplePoly + "Buildings/" + houses[hi++ % houses.Length] + ".prefab");
            if (house != null) { var g = Put(house, town, new Vector3(x + Range(-2f, 2f), 0f, -352f), 1f, 0f); AddMeshColliders(g); }
            var house2 = Load(SimplePoly + "Buildings/" + houses[hi++ % houses.Length] + ".prefab");
            if (house2 != null && R01() < 0.75f) { var g = Put(house2, town, new Vector3(x + Range(-3f, 3f), 0f, -380f), 1f, 180f); AddMeshColliders(g); }
            if (stLight != null) Put(stLight, town, new Vector3(x + 12f, 0f, -289f), 1f, 90f);
        }
        occupied.Add(new Zone(new Vector3(-170f, 0f, -340f), 50f));
        for (float x = -120f; x <= 280f; x += 40f) occupied.Add(new Zone(new Vector3(x, 0f, -340f), 50f));
        var plaza = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plaza.name = "TownStreet";
        plaza.transform.SetParent(town, false);
        plaza.transform.position = new Vector3(60f, 0.01f, -289f);
        plaza.transform.localScale = new Vector3(460f, 0.02f, 12f);
        plaza.GetComponent<MeshRenderer>().sharedMaterial = MakeMatCached("Street", new Color(0.42f, 0.42f, 0.44f));
        Object.DestroyImmediate(plaza.GetComponent<Collider>());
        MarkStatic(plaza);

        // 솔숲 (동쪽)
        var forest = new GameObject("PineForest").transform; forest.SetParent(root, false);
        for (int i = 0; i < 700; i++)
        {
            Vector3 p = new Vector3(Range(470f, 1050f), 0f, Range(-150f, 650f));
            if (!Free(p, 10f, 2f)) continue;
            var pf = R01() < 0.7f ? firs[rnd.Next(firs.Length)] : trees[rnd.Next(trees.Length)];
            Put(pf, forest, p, Range(1.1f, 2.0f), Range(0f, 360f));
        }

        // 들꽃 들판 (북쪽): 덤불·풀·풍차·울타리
        var field = new GameObject("Field").transform; field.SetParent(root, false);
        for (int i = 0; i < 500; i++)
        {
            Vector3 p = new Vector3(Range(-250f, 300f), 0f, Range(470f, 760f));
            if (!Free(p, 7f, 0.5f)) continue;
            bool g = R01() < 0.65f && grass.Length > 0;
            Put(g ? grass[rnd.Next(grass.Length)] : bushes[rnd.Next(bushes.Length)], field, p, g ? Range(2f, 4f) : Range(0.6f, 1.1f), Range(0f, 360f));
        }
        var windmill = Load(SimplePoly + "Props/Props_Windmill.prefab");
        if (windmill != null)
        {
            Put(windmill, field, new Vector3(-60f, 0f, 640f), 1.6f, 200f);
            Put(windmill, field, new Vector3(140f, 0f, 690f), 1.3f, 170f);
            occupied.Add(new Zone(new Vector3(-60f, 0f, 640f), 12f));
            occupied.Add(new Zone(new Vector3(140f, 0f, 690f), 12f));
        }
        var fence = Load(Megapolis + "Props/Fence_01.prefab");
        if (fence != null)
        {
            var probe = (GameObject)Object.Instantiate(fence);
            var b = probe.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
            Object.DestroyImmediate(probe);
            float flen = Mathf.Max(0.5f, Mathf.Max(b.size.x, b.size.z) - 0.05f);
            for (float x = -140f; x <= 240f; x += flen) Put(fence, field, new Vector3(x, 0f, 600f), 1f, b.size.x >= b.size.z ? 0f : 90f);
        }

        // 바위산 (서쪽)
        var rock = new GameObject("RockyHills").transform; rock.SetParent(root, false);
        if (mountains.Length > 0)
        {
            Put(mountains[0], rock, new Vector3(-760f, 0f, 120f), 2.6f, 30f);
            Put(mountains[1 % mountains.Length], rock, new Vector3(-700f, 0f, -150f), 2.0f, 110f);
            Put(mountains[2 % mountains.Length], rock, new Vector3(-820f, 0f, 380f), 2.3f, 250f);
            occupied.Add(new Zone(new Vector3(-760f, 0f, 120f), 110f));
            occupied.Add(new Zone(new Vector3(-700f, 0f, -150f), 90f));
            occupied.Add(new Zone(new Vector3(-820f, 0f, 380f), 100f));
        }
        for (int i = 0; i < 160; i++)
        {
            Vector3 p = new Vector3(Range(-680f, -330f), 0f, Range(-200f, 420f));
            if (!Free(p, 9f, 3f)) continue;
            Put(rocks[rnd.Next(rocks.Length)], rock, p, Range(0.8f, 2.6f), Range(0f, 360f));
        }

        // 언덕
        var hillRoot = new GameObject("Hills").transform; hillRoot.SetParent(root, false);
        for (int i = 0; i < 90; i++)
        {
            Vector3 p = new Vector3(Range(-1100f, 1300f), 0f, Range(-900f, 1200f));
            float sc = Range(3f, 7f);
            if (!Free(p, 30f + sc * 10f, sc * 9f)) continue;
            Put(hills[rnd.Next(hills.Length)], hillRoot, p, sc, Range(0f, 360f));
            occupied.Add(new Zone(p, sc * 9f));
        }

        // 일반 나무: 차창에서 잘 보이도록 선로 가까이에 더 많이
        var treeRoot = new GameObject("Trees").transform; treeRoot.SetParent(root, false);
        for (int i = 0; i < 1600; i++)
        {
            float s = Range(0f, tp.length);
            float lat = (R01() < 0.5f ? -1f : 1f) * Mathf.Lerp(14f, 260f, R01() * R01());
            Vector3 p = tp.GetPosition(s) + tp.GetLeft(s) * lat;
            if (!Free(p, 12f, 2f)) continue;
            Put(trees[rnd.Next(trees.Length)], treeRoot, p, Range(1.0f, 1.9f), Range(0f, 360f));
        }
        for (int i = 0; i < 1400; i++)
        {
            Vector3 p = new Vector3(Range(-1100f, 1300f), 0f, Range(-900f, 1200f));
            if (!Free(p, 11f, 2f)) continue;
            Put(trees[rnd.Next(trees.Length)], treeRoot, p, Range(1.0f, 1.9f), Range(0f, 360f));
        }

        // 먼 산맥
        var far = new GameObject("Mountains").transform; far.SetParent(root, false);
        Vector3 center = new Vector3(100f, 0f, 150f);
        for (int i = 0; i < 22; i++)
        {
            float a = i / 22f * Mathf.PI * 2f + Range(-0.08f, 0.08f);
            float r = Range(1700f, 2400f);
            Put(mountains[rnd.Next(mountains.Length)], far, center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r, Range(4.5f, 8f), Range(0f, 360f));
        }
    }

    // ───────────────────────── 열차 ─────────────────────────

    static TrainController BuildTrain(TrackPath tp, float firstStopS)
    {
        var prefab = Load(TrainRoot + "Prefabs/Steam Trains PM/Large_Steam_Train.prefab");
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        inst.name = "Train";
        inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var t = inst.transform;

        var loco = t.Find("SteamLocomotive_type3_");
        var cars = new[] { t.Find("RC_buffetCar"), t.Find("RC_passengerCar"), t.Find("RC_passengerCar (1)") };
        var elements = t.Find("elements");
        for (int i = 0; i < 3; i++)
        {
            string suffix = i == 0 ? "" : " (" + i + ")";
            var conn = elements.Find("SL_t1_conectionPartLarge" + suffix);
            var hook = elements.Find("RC_hooksConnected" + suffix);
            if (conn != null) conn.SetParent(cars[i], true);
            if (hook != null) hook.SetParent(cars[i], true);
        }
        foreach (var n in new[] { "elements", "RC_passengerCar (2)", "RC_freightCar", "RC_freightCar (1)" })
        {
            var x = t.Find(n);
            if (x != null) Object.DestroyImmediate(x.gameObject);
        }

        // 원본 굴뚝 파티클(네모난 점)은 끄고 TrainSmoke로 대체
        foreach (var ps in loco.GetComponentsInChildren<ParticleSystem>(true))
            if (ps.transform.localPosition.x > 2f) ps.gameObject.SetActive(false);

        var vehicles = new List<TrainController.Vehicle>();
        vehicles.Add(new TrainController.Vehicle { root = loco, offset = 0f, halfBase = 2.6f, height = LocoHeight });
        float locoX = loco.localPosition.x;
        foreach (var c in cars)
            vehicles.Add(new TrainController.Vehicle { root = c, offset = locoX - c.localPosition.x, halfBase = 3.4f, height = CarHeight });

        foreach (var v in vehicles)
        {
            AddBoxCollider(v.root);
            var rb = v.root.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // 기관실 시점 + 전조등
        var cab = new GameObject("CabCamera").transform;
        cab.SetParent(loco, false);
        cab.localPosition = new Vector3(-0.5f, 2.02f, -0.42f);
        cab.localRotation = Quaternion.Euler(0f, 90f, 0f);
        var hl = new GameObject("Headlight");
        hl.transform.SetParent(loco, false);
        hl.transform.localPosition = new Vector3(4.3f, 1.5f, 0f);
        hl.transform.localRotation = Quaternion.Euler(4f, 90f, 0f);
        var light = hl.AddComponent<Light>();
        light.type = LightType.Spot;
        light.range = 90f;
        light.spotAngle = 48f;
        light.intensity = 3f;
        light.color = new Color(1f, 0.92f, 0.75f);
        light.enabled = false;

        var tc = inst.AddComponent<TrainController>();
        tc.track = tp;
        tc.vehicles = vehicles.ToArray();
        tc.frontOffset = LocoFront;
        tc.s = tp.Wrap(firstStopS - LocoFront);
        tc.cabAnchor = cab;
        tc.headlight = light;

        var smoke = inst.AddComponent<TrainSmoke>();
        smoke.train = tc;
        smoke.smokeMaterial = SmokeMaterial();
        var audio = inst.AddComponent<TrainAudio>();
        audio.train = tc;

        // 에디터에서도 첫 정차 위치에 놓아 둔다
        foreach (var v in tc.vehicles)
        {
            float c = tc.s - v.offset;
            Vector3 pf = tp.GetPosition(c + v.halfBase), pr = tp.GetPosition(c - v.halfBase);
            Vector3 fwd = (pf - pr).normalized;
            v.root.SetPositionAndRotation((pf + pr) * 0.5f + Vector3.up * v.height, Quaternion.LookRotation(Vector3.Cross(fwd, Vector3.up), Vector3.up));
        }
        return tc;
    }

    /// <summary>부드러운 뭉게 연기 입자 (알파 블렌딩, 절차 텍스처).</summary>
    static Material SmokeMaterial()
    {
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "SmokePuff" };
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float n = Mathf.PerlinNoise(x * 0.07f, y * 0.07f) * 0.6f + Mathf.PerlinNoise(x * 0.17f + 9f, y * 0.17f + 3f) * 0.4f;
                float a = Mathf.Clamp01(1f - r * (1.05f + 0.35f * (1f - n)));
                a = a * a * (3f - 2f * a);
                float shade = 0.82f + 0.18f * n - 0.12f * Mathf.Clamp01(-v);
                px[y * N + x] = new Color(shade, shade, shade, a * (0.75f + 0.25f * n));
            }
        tex.SetPixels(px);
        tex.Apply(true);
        AssetDatabase.CreateAsset(tex, GenDir + "/SmokePuff.asset");

        string path = MatDir + "/TrainSmoke.mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
        m.mainTexture = tex;
        m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void AddBoxCollider(Transform root)
    {
        var inv = root.worldToLocalMatrix;
        Bounds b = new Bounds();
        bool any = false;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            var m = inv * r.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(c);
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }
        var bc = root.gameObject.AddComponent<BoxCollider>();
        bc.center = b.center;
        bc.size = b.size;
    }

    // ───────────────────────── 기관사 / 카메라 ─────────────────────────

    static DriverController BuildDriver(TrackPath tp, TrainController train, float firstStopS)
    {
        float sCab = firstStopS - LocoFront - 0.4f;
        Vector3 R = -tp.GetLeft(sCab);
        Vector3 spawn = tp.GetPosition(sCab) + R * 3.6f + Vector3.up * (PlatformTop + 0.05f);

        var go = new GameObject("Driver");
        go.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(-R, Vector3.up));
        go.AddComponent<CharacterController>();
        var dc = go.AddComponent<DriverController>();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VrmPath);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, go.transform);
        model.name = "TrainDriverFemale_Cap";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        var anim = model.GetComponent<Animator>();
        anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        anim.applyRootMotion = false;

        dc.animator = anim;
        dc.vrm = model.GetComponent<Vrm10Instance>();
        dc.train = train;
        foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = PlayerLayer;
        return dc;
    }

    static GameCamera BuildCamera(DriverController driver)
    {
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.15f;
        cam.farClipPlane = 5000f;
        camGo.AddComponent<AudioListener>();
        var gc = camGo.AddComponent<GameCamera>();
        gc.target = driver.transform;
        gc.collisionMask = ~((1 << PlayerLayer) | (1 << 2));
        float yaw = driver.transform.eulerAngles.y;
        gc.SetAngles(yaw, 12f);
        camGo.transform.SetPositionAndRotation(driver.transform.position + Vector3.up * 2.2f - driver.transform.forward * 4.2f, Quaternion.Euler(12f, yaw, 0f));
        return gc;
    }

    // ───────────────────────── 유틸 ─────────────────────────

    static GameObject Load(string path) { return AssetDatabase.LoadAssetAtPath<GameObject>(path); }

    static GameObject Place(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot)
    {
        var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        g.transform.SetPositionAndRotation(pos, rot);
        return g;
    }

    /// <summary>표지판 FBX의 내장 재질은 셰이더가 깨져 있어 기관차 팔레트 재질로 바꾼다.</summary>
    static void FixBrokenMaterials(GameObject g)
    {
        var palette = AssetDatabase.LoadAssetAtPath<Material>(TrainRoot + "Models/Locomotives/Materials/palette_locomotive_redBlack.mat");
        var light = AssetDatabase.LoadAssetAtPath<Material>(TrainRoot + "Models/Locomotives/Materials/Light.mat");
        foreach (var r in g.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && mats[i].shader != null && mats[i].shader.isSupported && mats[i].shader.name != "Hidden/InternalErrorShader") continue;
                bool isLight = mats[i] != null && mats[i].name.ToLower().Contains("light");
                mats[i] = isLight && light != null ? light : palette;
            }
            r.sharedMaterials = mats;
        }
    }

    static void AddMeshColliders(GameObject g)
    {
        foreach (var mf in g.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        MarkStatic(g);
    }

    static void MarkStatic(GameObject g)
    {
        foreach (var t in g.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
    }

    static AnimationClip LoopCopy(string fbxName, string outName)
    {
        string path = AssetDatabase.FindAssets(fbxName)
            .Select(AssetDatabase.GUIDToAssetPath)
            .FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == fbxName && p.ToLower().EndsWith(".fbx"));
        if (path == null) { Debug.LogError("[TrainGame] 애니메이션 FBX를 찾지 못함: " + fbxName); return null; }
        var src = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (src == null) { Debug.LogError("[TrainGame] 클립 없음: " + path); return null; }
        string outPath = AnimDir + "/" + outName + ".anim";
        AssetDatabase.DeleteAsset(outPath);
        var copy = Object.Instantiate(src);
        copy.name = outName;
        var st = AnimationUtility.GetAnimationClipSettings(copy);
        st.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(copy, st);
        AssetDatabase.CreateAsset(copy, outPath);
        return copy;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    static void EnsureLayer(string layerName, int index)
    {
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return;
        var sp = layers.GetArrayElementAtIndex(index);
        if (string.IsNullOrEmpty(sp.stringValue)) { sp.stringValue = layerName; tm.ApplyModifiedProperties(); }
        else Debug.LogWarning("[TrainGame] 레이어 슬롯 " + index + "가 이미 사용 중입니다: " + sp.stringValue);
    }
}
