using UnityEngine;

// 건물을 부술 때 확률로 그 자리 잔해에 깔린 사람(RubbleVictim)을 만듦.
// 사람 크기는 손에 쥐기 좋게 인질과 같은 비율(거인 키 × HostageEvent.hostageHeightRatio).
public class RubbleVictimSpawner : MonoBehaviour
{
    public static RubbleVictimSpawner Instance { get; private set; }

    [Tooltip("건물 하나를 부술 때 깔린 사람이 나올 확률")] [Range(0f, 1f)] public float chance = 0.28f;
    [Tooltip("동시에 있을 수 있는 깔린 사람 수")] public int maxAlive = 4;
    [Tooltip("구하지 않으면 이 시간(초) 뒤 사라짐")] public float lifetime = 45f;
    [Tooltip("거인 키 × 이 비율 안으로 다가가면 집음")] public float pickRangeRatio = 0.35f;
    [Tooltip("깔린 뒤 이 시간(초)이 지나야 집을 수 있음 (건물을 부순 직후 바로 집히지 않게)")] public float grabDelay = 1.5f;
    public Color rubbleColor = new Color(0.55f, 0.53f, 0.5f);

    static Material rubbleMat;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    // 건물이 부서질 때 BuildingDestruction이 호출 (groundPos = 건물 바닥 가운데)
    public static void OnBuildingBroken(Vector3 groundPos)
    {
        if (Instance) Instance.TrySpawn(groundPos);
    }

    void TrySpawn(Vector3 pos)
    {
        if (ItemDrops.Skip(chance) || RubbleVictim.All.Count >= maxAlive) return;
        var mgr = CivilianManager.Instance;
        var hp = GiantHealth.Instance;
        if (!mgr || mgr.prefabs == null || mgr.prefabs.Length == 0 || !hp || GiantShrink.Active) return;
        var he = HostageEvent.Instance;
        float H = Mathf.Max(1f, hp.Height) * (he ? he.hostageHeightRatio : 0.07f); // 사람 키(m)

        // 모델: 인질 이벤트와 같은 GentleMan (없으면 시민 모델)
        var model = he && he.hostagePrefab ? he.hostagePrefab : mgr.prefabs[Random.Range(0, mgr.prefabs.Length)];
        var go = Instantiate(model, transform);
        go.name = "RubbleVictim";
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        // 키를 H로 맞춤 (모델 원래 키를 렌더러로 잼)
        go.transform.rotation = Quaternion.identity;
        var rs = go.GetComponentsInChildren<Renderer>();
        float h0 = 1.8f;
        if (rs.Length > 0) { Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); if (b.size.y > 0.05f) h0 = b.size.y; }
        go.transform.localScale *= H / h0;
        var anim = go.GetComponentInChildren<Animator>();
        if (anim) { anim.runtimeAnimatorController = mgr.locomotion; anim.applyRootMotion = false; anim.SetFloat("Speed", 0f); }

        // 엎드린 자세: 발이 pos, 몸은 yaw 방향으로 누움
        float yaw = Random.Range(0f, 360f);
        Quaternion lying = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(90f, 0f, 0f);
        Vector2 off = Random.insideUnitCircle * H * 0.6f;
        Vector3 p = new Vector3(pos.x + off.x, GroundY(pos) + H * 0.12f, pos.z + off.y);
        go.transform.SetPositionAndRotation(p, lying);

        var v = go.AddComponent<RubbleVictim>();
        v.Init(anim, H, lifetime, pickRangeRatio, grabDelay);
        v.Title = he ? he.PickTitle() : "VIP 인질";

        // 다리·허리 위에 잔해 덩어리
        if (!rubbleMat) { rubbleMat = new Material(Shader.Find("Standard")) { color = rubbleColor }; }
        Vector3 along = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward; // 발 → 머리 방향 (바닥 위)
        float[] at = { 0.18f, 0.42f, 0.58f };
        for (int i = 0; i < at.Length; i++)
        {
            if (i == 2 && Random.value < 0.5f) continue;
            var chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chunk.name = "Rubble";
            Destroy(chunk.GetComponent<Collider>());
            chunk.GetComponent<MeshRenderer>().sharedMaterial = rubbleMat;
            chunk.transform.SetParent(transform, true);
            chunk.transform.localScale = new Vector3(H * Random.Range(0.35f, 0.5f), H * Random.Range(0.18f, 0.28f), H * Random.Range(0.25f, 0.4f));
            chunk.transform.SetPositionAndRotation(
                new Vector3(p.x, p.y, p.z) + along * H * at[i] + Vector3.up * H * 0.12f,
                Quaternion.Euler(Random.Range(-15f, 15f), yaw + Random.Range(-35f, 35f), Random.Range(-15f, 15f)));
            v.AddRubble(chunk);
        }
        // 멀리서도 보이게 노란 테두리
        ThreatGlow.Add(go.transform, H * 1.3f, 0.02f, new Color(1f, 0.85f, 0.2f, 0.85f));
        ItemDrops.Spawned++;
    }

    // ───── 깔린 사람 확대 화면 (우측 상단) ─────
    [Header("확대 화면")]
    [Tooltip("거인에게서 이 거리(m) 안의 깔린 사람을 우측 상단에 확대해서 보여줌")] public float pipRange = 150f;
    public float pipSize = 110f;
    Camera pipCam; RenderTexture pipRT; RubbleVictim shown;

    // 가장 가까운(구하지 않은) 깔린 사람. 손에 쥔 인질 화면이 떠 있으면 그쪽이 우선
    RubbleVictim Nearest()
    {
        var hp = GiantHealth.Instance;
        if (!hp || HostageEvent.ShowingPip || GameStartMenu.InMenu) return null;
        RubbleVictim best = null; float bd = pipRange * pipRange;
        foreach (var v in RubbleVictim.All)
        {
            if (!v || v.Grabbed) continue;
            Vector3 d = v.transform.position - hp.transform.position; d.y = 0f;
            if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = v; }
        }
        return best;
    }

    // 깔린 사람의 머리 쪽 앞 위에서 내려다봄 (엎드려 있으니 얼굴과 등 위 잔해가 함께 보이게)
    void LateUpdate()
    {
        shown = Nearest();
        if (!shown) { if (pipCam) pipCam.enabled = false; return; }
        if (!pipCam)
        {
            var cg = new GameObject("RubbleVictimCam");
            cg.transform.SetParent(transform, false);
            pipCam = cg.AddComponent<Camera>();
            pipCam.fieldOfView = 40f; pipCam.nearClipPlane = 0.1f; pipCam.farClipPlane = 2000f;
            pipRT = new RenderTexture(384, 384, 16);
            pipCam.targetTexture = pipRT;
        }
        pipCam.enabled = true;
        float H = shown.Height;
        Vector3 feet = shown.transform.position, along = shown.transform.up; along.y = 0f; // 엎드린 몸의 발 → 머리 방향
        along = along.sqrMagnitude > 0.01f ? along.normalized : Vector3.forward;
        Vector3 focus = feet + along * H * 0.5f + Vector3.up * H * 0.1f;
        pipCam.transform.position = focus + along * H * 1.5f + Vector3.up * H * 0.7f; // 머리 앞쪽 낮은 곳에서 얼굴과 잔해가 함께 보이게
        pipCam.transform.LookAt(focus);
    }

    void OnGUI()
    {
        if (!shown || !pipRT || !pipCam || !pipCam.enabled) return;
        Rect box = HostageEvent.PipRectAbove(shown.transform.position + Vector3.up * shown.Height * 0.6f, pipSize); // 깔린 사람 바로 위에 띄움
        HostageEvent.DrawPip(box, pipRT, new Color(1f, 0.85f, 0.2f), string.IsNullOrEmpty(shown.Title) ? "VIP 인질" : shown.Title);
    }

    static float GroundY(Vector3 p) => ItemDrops.GroundY(p);
}
