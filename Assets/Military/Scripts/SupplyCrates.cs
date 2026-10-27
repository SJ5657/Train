using System.Collections.Generic;
using UnityEngine;

// 보급상자: 건물을 부술 때마다 dropChance 확률로 그 자리에 상자가 나옴. 크기는 거인 키에 비례.
// 거인이 닿아서 먹으면 아래 효과 중 하나가 무작위로 적용됨.
//   1) HP 즉시 회복   2) 일정 시간 경험치 배율   3) 거인을 뺀 모든 적 정지   4) 일정 시간 달리기 속도 배율
public class SupplyCrates : MonoBehaviour
{
    public static SupplyCrates Instance { get; private set; }

    [Header("등장")]
    [Tooltip("건물을 하나 부술 때 상자가 나올 확률 (0.01 = 1%)")] [Range(0f, 1f)] public float dropChance = 0.01f;
    [Tooltip("상자 한 변 = 거인 키 × 이 비율")] public float sizeRatio = 0.15f;
    [Tooltip("먹지 않고 이 시간(초)이 지나면 사라짐")] public float lifetime = 60f;
    [Tooltip("나온 뒤 이 시간(초)이 지나야 먹을 수 있음")] public float pickupDelay = 1.5f;
    public Color crateColor = new Color(0.25f, 0.9f, 0.4f);

    [Header("효과")]
    [Tooltip("HP 회복량 (최대 HP 대비, 1 = 가득)")] [Range(0f, 1f)] public float healRatio = 1f;
    [Tooltip("경험치 배율이 적용되는 시간(초)")] public float xpBoostSeconds = 60f;
    public float xpBoostMultiplier = 2f;
    [Tooltip("적이 멈추는 시간(초)")] public float freezeSeconds = 30f;
    [Tooltip("달리기 속도 배율이 적용되는 시간(초)")] public float runBoostSeconds = 30f;
    public float runBoostMultiplier = 2f;

    class Crate { public Transform t; public float born, baseY, size; }
    readonly List<Crate> crates = new List<Crate>();
    LevelSystem xpTarget; float xpBoostUntil; bool xpBoosting;
    GiantController runTarget; float runBoostUntil; bool runBoosting;
    string toast; float toastUntil;

    static Material crateMaterial;

    // 물음표(?)가 적힌 랜덤상자 무늬: 상자 색 바탕 + 어두운 테두리 + 흰색 물음표 (6면 모두 같은 무늬)
    static Texture2D MakeQuestionTexture(Color body)
    {
        const int n = 64, cell = 7;
        string[] glyph = { ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.." }; // 5x7 '?'
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        Color dark = body * 0.45f; dark.a = 1f;
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                px[y * n + x] = (x < 4 || y < 4 || x >= n - 4 || y >= n - 4) ? dark : body;
        int ox = (n - 5 * cell) / 2, oy = (n - 7 * cell) / 2;
        for (int pass = 0; pass < 2; pass++) // 0 = 그림자, 1 = 흰 글자
            for (int gy = 0; gy < 7; gy++)
                for (int gx = 0; gx < 5; gx++)
                {
                    if (glyph[gy][gx] != '#') continue;
                    int sx = ox + gx * cell + (pass == 0 ? 3 : 0), sy = oy + (6 - gy) * cell - (pass == 0 ? 3 : 0); // 위가 gy=0
                    for (int yy = 0; yy < cell; yy++)
                        for (int xx = 0; xx < cell; xx++)
                        {
                            int x = sx + xx, y = sy + yy;
                            if (x >= 0 && x < n && y >= 0 && y < n) px[y * n + x] = pass == 0 ? dark : Color.white;
                        }
                }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; EndXpBoost(); EndRunBoost(); }

    // 건물이 부서질 때 BuildingDestruction이 호출 (거인이 부순 것만)
    public static void OnBuildingBroken(Vector3 groundPos)
    {
        if (Instance) Instance.TryDrop(groundPos);
    }

    void TryDrop(Vector3 pos)
    {
        if (ItemDrops.Skip(dropChance)) return;
        var g = GiantHealth.Instance;
        float size = Mathf.Max(1f, (g ? g.Height : 28f) * sizeRatio);

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "SupplyCrate";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, true);
        go.transform.localScale = Vector3.one * size;
        go.transform.position = new Vector3(pos.x, size * 0.5f + 0.3f, pos.z);
        var mr = go.GetComponent<MeshRenderer>();
        if (!crateMaterial) { crateMaterial = new Material(mr.sharedMaterial) { mainTexture = MakeQuestionTexture(crateColor), color = Color.white }; }
        mr.sharedMaterial = crateMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ThreatGlow.Add(go.transform, size * 2.4f, 0.02f, new Color(crateColor.r, crateColor.g, crateColor.b, 0.9f)); // 멀리서도 찾을 수 있게 초록 테두리
        ItemDrops.Spawned++;
        crates.Add(new Crate { t = go.transform, born = Time.time, baseY = go.transform.position.y, size = size });
    }

    void Update()
    {
        var g = GiantHealth.Instance;
        for (int i = crates.Count - 1; i >= 0; i--)
        {
            var c = crates[i];
            if (!c.t) { crates.RemoveAt(i); continue; }
            if (Time.time - c.born > lifetime) { Destroy(c.t.gameObject); crates.RemoveAt(i); continue; }
            // 천천히 돌며 위아래로 둥둥
            c.t.Rotate(0f, 60f * Time.deltaTime, 0f, Space.World);
            var p = c.t.position; p.y = c.baseY + Mathf.Sin((Time.time + c.born) * 2.5f) * c.size * 0.12f; c.t.position = p;
            if (g && !g.IsDead && Time.time - c.born >= pickupDelay && g.IsTouching(c.t.position, c.size * 0.6f)) // 나온 직후엔 바로 먹지 못함
            {
                Collect(g, c.t.position);
                Destroy(c.t.gameObject); crates.RemoveAt(i);
            }
        }
        if (xpBoosting && Time.time >= xpBoostUntil) EndXpBoost();
        if (runBoosting && Time.time >= runBoostUntil) EndRunBoost();
    }

    void Collect(GiantHealth g, Vector3 pos)
    {
        CombatFX.Muzzle(pos, Vector3.up, 4f);
        switch (Random.Range(0, 4))
        {
            case 3:
                StartRunBoost();
                ShowItem($"보급상자: 달리기 속도 {runBoostMultiplier:0.#}배 ({runBoostSeconds:0}초)");
                break;
            case 0:
                g.Heal(g.maxHP * healRatio);
                ShowItem("보급상자: HP 회복!");
                break;
            case 1:
                StartXpBoost();
                ShowItem($"보급상자: 경험치 {xpBoostMultiplier:0.#}배 ({xpBoostSeconds:0}초)");
                break;
            default:
                EnemyFreeze.Freeze(freezeSeconds);
                ShowItem($"보급상자: 적 정지 ({freezeSeconds:0}초)");
                break;
        }
    }

    // 인질 구출 보상 등 밖에서 쓰는 도우미 (경험치 배율 + 알림 문구)
    public static void GiveXpBoost(float seconds) { if (Instance) Instance.StartXpBoost(seconds); }
    public static void Notice(string text) { if (Instance) Instance.Show(text); }

    void StartXpBoost(float seconds = -1f)
    {
        var ls = LevelSystem.Instance;
        if (!ls) return;
        if (!xpBoosting) { xpTarget = ls; ls.xpMultiplier *= xpBoostMultiplier; xpBoosting = true; }
        xpBoostUntil = Time.time + (seconds > 0f ? seconds : xpBoostSeconds); // 이미 적용 중이면 시간만 새로 채움
    }

    void EndXpBoost()
    {
        if (!xpBoosting) return;
        if (xpTarget) xpTarget.xpMultiplier /= xpBoostMultiplier;
        xpBoosting = false;
    }

    // 달리기 속도 배율: 이미 적용 중이면 배율은 겹치지 않고 시간만 새로 채움
    void StartRunBoost()
    {
        var ctrl = FindObjectOfType<GiantController>();
        if (!ctrl) return;
        if (!runBoosting) { runTarget = ctrl; ctrl.runSpeed *= runBoostMultiplier; runBoosting = true; }
        runBoostUntil = Time.time + runBoostSeconds;
    }

    void EndRunBoost()
    {
        if (!runBoosting) return;
        if (runTarget) runTarget.runSpeed /= runBoostMultiplier;
        runBoosting = false;
    }

    void Show(string text) { toast = text; toastUntil = Time.time + 3f; }

    // 상자를 먹었을 때: 가운데 알림 + 왼쪽 습득 알림(상자 아이콘)
    void ShowItem(string text)
    {
        Show(text);
        ItemFeed.Show("보급상자", crateMaterial ? crateMaterial.mainTexture : null, text.Replace("보급상자: ", ""), crateColor);
    }

    // 먹은 효과 알림 + 남은 시간 표시 (다른 HUD가 숨겨져 있어도 보이게)
    void OnGUI()
    {
        if (GameStartMenu.InMenu) return;
        var big = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        if (Time.time < toastUntil && !string.IsNullOrEmpty(toast))
        {
            float a = Mathf.Clamp01((toastUntil - Time.time) / 0.5f);
            var r = new Rect(0, Screen.height * 0.2f, Screen.width, 40);
            GUI.color = new Color(0, 0, 0, 0.8f * a); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), toast, big);
            GUI.color = new Color(crateColor.r, crateColor.g, crateColor.b, a); GUI.Label(r, toast, big);
        }
        var small = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        float y = Screen.height * 0.2f + 44f;
        if (xpBoosting) y = DrawTimer($"경험치 {xpBoostMultiplier:0.#}배  {Mathf.Max(0f, xpBoostUntil - Time.time):0}s", y, small);
        if (runBoosting) y = DrawTimer($"달리기 {runBoostMultiplier:0.#}배  {Mathf.Max(0f, runBoostUntil - Time.time):0}s", y, small);
        if (EnemyFreeze.Active) y = DrawTimer($"적 정지  {EnemyFreeze.Left:0}s", y, small);
        if (MilitarySpawner.RetreatLeft > 0f) DrawTimer($"적 후퇴  {MilitarySpawner.RetreatLeft:0}s", y, small);
        GUI.color = Color.white;
    }

    float DrawTimer(string text, float y, GUIStyle style)
    {
        var r = new Rect(0, y, Screen.width, 24);
        GUI.color = new Color(0, 0, 0, 0.8f); GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
        GUI.color = crateColor; GUI.Label(r, text, style);
        return y + 24f;
    }
}
