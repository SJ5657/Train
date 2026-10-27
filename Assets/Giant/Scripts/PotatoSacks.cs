using System.Collections.Generic;
using UnityEngine;

// 감자가방 뭉치: 건물을 부수면 확률로 그 자리에 감자가방 몇 개가 쌓여 나옴. 거인이 닿으면 먹고 큰 방귀를 뀜.
// 먹으면 HP를 회복하고 넓은 방귀 연막을 피움: 거인이 연막 안에 있는 동안 적은 공격하지 못함.
public class PotatoSacks : MonoBehaviour
{
    public static PotatoSacks Instance { get; private set; }

    [Header("감자가방 뭉치")]
    public GameObject sackPrefab;
    [Tooltip("건물 하나를 부술 때 감자가방 뭉치가 나올 확률")] [Range(0f, 1f)] public float chance = 0.13f;
    [Tooltip("동시에 있을 수 있는 뭉치 수")] public int maxPiles = 3;
    [Tooltip("가방 하나 높이 = 거인 키 × 이 비율")] public float sackHeightRatio = 0.13f;
    public Vector2Int sacksPerPile = new Vector2Int(7, 11);
    [Tooltip("안 먹으면 이 시간(초) 뒤 사라짐")] public float lifetime = 60f;
    [Tooltip("나온 뒤 이 시간(초)이 지나야 먹을 수 있음")] public float pickupDelay = 1.5f;

    [Header("방귀")]
    [Tooltip("방귀 범위 반경 = 거인 키 × 이 비율")] public float fartRadiusRatio = 3.2f;
    [Tooltip("가스가 남아 있는 시간(초)")] public float gasDuration = 12f;
    [Tooltip("먹었을 때 회복하는 HP (최대 HP 대비)")] [Range(0f, 1f)] public float healRatio = 0.3f;
    [Tooltip("방귀 연막 색 (반투명 살색)")] public Color smokeColor = new Color(1f, 0.84f, 0.72f, 0.5f);

    class Pile { public GameObject root; public float born, radius; }
    readonly List<Pile> piles = new List<Pile>();

    Texture2D sackIcon;

    void Awake() { Instance = this; }
    void Start() { sackIcon = ItemFeed.RenderIcon(sackPrefab); } // 습득 알림용 감자가방 아이콘
    void OnDestroy() { if (Instance == this) Instance = null; }

    // 건물이 부서질 때 BuildingDestruction이 호출
    public static void OnBuildingBroken(Vector3 groundPos) { if (Instance) Instance.TrySpawn(groundPos); }

    void TrySpawn(Vector3 pos)
    {
        if (!sackPrefab || ItemDrops.Skip(chance) || piles.Count >= maxPiles) return;
        var hp = GiantHealth.Instance;
        if (!hp || GiantShrink.Active) return;
        float sackH = Mathf.Max(0.3f, hp.Height * sackHeightRatio);
        var root = new GameObject("PotatoSacks");
        root.transform.SetParent(transform, false);
        float ground = GroundY(pos);
        root.transform.position = new Vector3(pos.x, ground, pos.z);
        int n = Random.Range(sacksPerPile.x, sacksPerPile.y + 1);
        float scale = sackH / 1.03f; // 프리팹 원래 높이 약 1.03m
        for (int i = 0; i < n; i++)
        {
            var s = Instantiate(sackPrefab, root.transform);
            foreach (var c in s.GetComponentsInChildren<Collider>()) Destroy(c);
            s.transform.localScale = Vector3.one * scale * Random.Range(0.85f, 1.15f);
            // 바닥에 둥글게 놓고, 몇 개는 위에 얹음
            bool top = i >= 6; // 바닥에 6개를 둥글게 깔고 나머지는 위에 얹음
            Vector2 off = Random.insideUnitCircle * sackH * (top ? 0.5f : 1.2f);
            s.transform.localPosition = new Vector3(off.x, top ? sackH * 0.6f : 0f, off.y);
            s.transform.localRotation = Quaternion.Euler(Random.Range(-12f, 12f) + (top ? Random.Range(-60f, 60f) : 0f), Random.Range(0f, 360f), Random.Range(-12f, 12f));
        }
        ThreatGlow.Add(root.transform, sackH * 3f, 0.02f, new Color(0.75f, 0.55f, 0.25f, 0.9f)); // 멀리서도 보이게 갈색 테두리
        piles.Add(new Pile { root = root, born = Time.time, radius = sackH * 1.5f });
        ItemDrops.Spawned++;
    }

    void Update()
    {
        var hp = GiantHealth.Instance;
        var cc = hp ? hp.GetComponent<CharacterController>() : null;
        for (int i = piles.Count - 1; i >= 0; i--)
        {
            var p = piles[i];
            if (!p.root) { piles.RemoveAt(i); continue; }
            if (Time.time - p.born > lifetime) { Destroy(p.root); piles.RemoveAt(i); continue; }
            if (!hp || hp.IsDead || GiantShrink.Active || !cc) continue;
            if (Time.time - p.born < pickupDelay) continue; // 나온 직후엔 바로 먹지 못함
            float d = ItemDrops.PathDistance(p.root.transform.position); // 달리며 지나가도 놓치지 않게 지나간 길로 잼
            if (d < cc.radius * 1.2f + p.radius)
            {
                Destroy(p.root); piles.RemoveAt(i);
                Fart(hp);
                ItemFeed.Show("감자가방", sackIcon, $"방귀 연막(적 공격 차단) · HP +{healRatio * 100f:0}%", new Color(0.85f, 0.65f, 0.35f));
            }
        }
    }

    // 방귀 연막: 넓게 퍼지는 반투명 살색 가스. 거인이 그 안에 있는 동안 적은 공격하지 못함 (GasCloud.GiantHidden). 먹으면 HP도 회복
    void Fart(GiantHealth hp)
    {
        float r = Mathf.Max(5f, hp.Height * fartRadiusRatio);
        var mat = BuildingDestruction.Instance ? BuildingDestruction.Instance.dustMaterial : null;
        if (mat) GasCloud.Create(hp.transform.position, r, gasDuration, 0f, mat, smokeColor);
        hp.Heal(hp.maxHP * healRatio);
    }

    // ───── 감자가방 확대 화면 (뭉치 바로 위) ─────
    [Header("확대 화면")]
    [Tooltip("거인에게서 이 거리(m) 안의 가장 가까운 뭉치를 그 위에 확대해서 보여줌")] public float pipRange = 150f;
    public float pipSize = 110f;
    Camera pipCam; RenderTexture pipRT; Pile shown;

    void LateUpdate()
    {
        shown = null;
        var hp = GiantHealth.Instance;
        if (hp && !GameStartMenu.InMenu)
        {
            float bd = pipRange * pipRange;
            foreach (var p in piles)
            {
                if (!p.root) continue;
                Vector3 d = p.root.transform.position - hp.transform.position; d.y = 0f;
                if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; shown = p; }
            }
        }
        if (shown == null) { if (pipCam) pipCam.enabled = false; return; }
        if (!pipCam)
        {
            var cg = new GameObject("PotatoCam");
            cg.transform.SetParent(transform, false);
            pipCam = cg.AddComponent<Camera>();
            pipCam.fieldOfView = 40f; pipCam.nearClipPlane = 0.1f; pipCam.farClipPlane = 2000f;
            pipRT = new RenderTexture(256, 256, 16);
            pipCam.targetTexture = pipRT;
        }
        pipCam.enabled = true;
        // 거인 쪽에서 비스듬히 내려다봄
        Vector3 c = shown.root.transform.position + Vector3.up * shown.radius * 0.4f;
        Vector3 from = hp.transform.position - shown.root.transform.position; from.y = 0f;
        from = from.sqrMagnitude > 0.01f ? from.normalized : Vector3.back;
        pipCam.transform.position = c + from * shown.radius * 3.2f + Vector3.up * shown.radius * 2.2f;
        pipCam.transform.LookAt(c);
    }

    void OnGUI()
    {
        if (shown == null || !shown.root || !pipRT || !pipCam || !pipCam.enabled) return;
        Rect box = HostageEvent.PipRectAbove(shown.root.transform.position + Vector3.up * shown.radius, pipSize);
        HostageEvent.DrawPip(box, pipRT, new Color(0.85f, 0.62f, 0.3f), "감자가방");
    }

    static float GroundY(Vector3 p) => ItemDrops.GroundY(p);
}
