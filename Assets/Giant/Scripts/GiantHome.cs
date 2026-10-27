using UnityEngine;

// 거인의 집(상점): 도시 동쪽 바깥에 거인 크기의 집 + 담벽(도시 쪽에 대문) + 넓은 잔디 마당을 게임 시작 시 만들고,
// 마당에 거인만 한 상인 NPC를 세움. NPC 가까이에서 E를 누르면 게임이 멈추고 상점 창이 열려 돈(Money)으로 HP 회복약·새장(CageSkill) 구매
// (sellItems를 켜면 패시브 아이템도 판매).
// 거인이 마당 안으로 들어올 수 있게 이동 범위를 열어줌(Allows). 집이 화면 밖이면 가장자리에 방향 표시.
public class GiantHome : MonoBehaviour
{
    public static GiantHome Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;
    // 상점이 닫힌 프레임 (ESC로 닫을 때 같은 프레임에 일시정지 메뉴가 열리지 않게)
    public static int ClosedFrame { get; private set; } = -1;
    // 거인이 집 마당 안에 있으면 안전지대: 피해 없음 + 적들은 해산(MilitarySpawner)
    public static bool GiantSafe => Instance != null && Instance.giant && Instance.built && Instance.Allows(Instance.giant.transform.position);

    [Header("모델")]
    [Tooltip("거인 크기로 키울 집 모델")] public GameObject housePrefab;
    [Tooltip("집 크기 (거인 기준). 1 = 사람에게 맞는 집을 거인 크기로 키운 크기")] public float houseSizeRatio = 1f;
    [Tooltip("상인 NPC (비우면 시민 캐릭터 중 하나)")] public GameObject npcPrefab;
    [Tooltip("NPC 크기 배율 (시민과 같은 소인 크기)")] public float npcScale = 1.2f;
    [Tooltip("게임 시작 시 거인을 집 마당에서 시작")] public bool spawnGiantHere = true;

    [Header("마당 / 담벽 (m)")]
    [Tooltip("도시 동쪽 끝에서 마당까지 거리")] public float gapFromCity = 80f;
    [Tooltip("최소 마당 크기 (집이 크면 집에 맞춰 넓어짐)")] public Vector2 yardSize = new Vector2(400f, 400f);
    [Tooltip("대문에서 집 앞까지 앞마당 깊이 (상인·거인 시작 위치)")] public float frontYard = 260f;
    public float wallHeight = 16f;
    public float wallThickness = 5f;
    [Tooltip("도시 쪽 대문 폭")] public float gateWidth = 120f;
    public Color wallColor = new Color(0.86f, 0.8f, 0.68f);

    [Header("상점")]
    [Tooltip("NPC와 이 거리 안이면 말을 걸 수 있음 (거인 크기 기준)")] public float talkRadius = 2.2f;
    [Tooltip("상점에서 패시브 아이템(공장·거인의 식욕)도 팔지. 끄면 회복약만")] public bool sellItems = false;
    [Tooltip("HP 회복약 첫 구매 가격 (살 때마다 2배로 오름)")] public int potionPrice = 100;
    [Tooltip("살 때마다 가격이 오르는 배율 (2 = 2배씩)")] public float potionPriceGrowth = 2f;
    int potionsBought;
    int CurrentPotionPrice => Mathf.RoundToInt(potionPrice * Mathf.Pow(potionPriceGrowth, Mathf.Min(potionsBought, 30)));
    [Tooltip("회복약 하나로 회복하는 양 (최대 HP 대비, 0.35 = 35%)")] [Range(0f, 1f)] public float potionHealRatio = 0.35f;
    static readonly Color PotionColor = new Color(0.4f, 1f, 0.5f);
    static readonly Color CageColor = new Color(0.95f, 0.75f, 0.3f);

    Rect yardRect;           // 마당 (XZ)
    Transform npc, house;
    Vector3 doorPos;         // 집 정면 가운데 (도시 쪽 벽)
    Animator npcAnim;
    GiantController giant;
    bool open, canTalk, built;
    float prevTimeScale = 1f;
    CursorLockMode prevLock; bool prevVisible;
    string message; float messageUntil;
    Mesh cube;

    void Awake() { Instance = this; }

    void OnDestroy()
    {
        if (open) Time.timeScale = 1f;
        if (Instance == this) Instance = null;
    }

    // 거인 이동 범위: 도시 밖이어도 마당 안이면 허용
    public bool Allows(Vector3 p) => yardRect.Contains(new Vector2(p.x, p.z));

    void Start()
    {
        giant = FindObjectOfType<GiantController>();
        var city = FindObjectOfType<CityGenerator>();
        if (!city) return;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube = tmp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmp);

        float pitch = city.blockSize + city.roadWidth;
        float halfX = (city.blocksX * pitch + city.roadWidth) * 0.5f;
        Vector3 c = city.transform.position;
        var root = transform;
        float t = wallThickness;

        // 집: 사람 집 모델을 거인 크기로 (거인 크기 = 사람의 GameScale배). 마당 크기를 정하려고 먼저 만들어서 잼
        GameObject h = null; Bounds hb = default;
        if (housePrefab)
        {
            float gs = giant ? giant.GameScale : 25f;
            h = Instantiate(housePrefab, root);
            h.name = "GiantHouse";
            foreach (var col in h.GetComponentsInChildren<Collider>()) Destroy(col);
            h.transform.rotation = Quaternion.Euler(0f, -90f, 0f); // 모델 정면(+Z) → 서쪽(-X)
            h.transform.localScale = Vector3.one * gs * houseSizeRatio;
            h.transform.position = Vector3.zero;
            hb = Bounds(h);
        }

        // 마당: 앞마당 + 집 + 뒤쪽 여유가 들어가도록 (최소 yardSize)
        Vector2 yard = yardSize;
        if (h)
        {
            yard.x = Mathf.Max(yard.x, frontYard + hb.size.x + t + 30f);
            yard.y = Mathf.Max(yard.y, hb.size.z + 160f);
        }
        float x0 = c.x + halfX + gapFromCity, x1 = x0 + yard.x;
        float z0 = c.z - yard.y * 0.5f, z1 = c.z + yard.y * 0.5f;
        yardRect = Rect.MinMaxRect(x0 - wallThickness, z0, x1, z1);
        float cx = (x0 + x1) * 0.5f;
        float fx = x0 + Mathf.Min(frontYard, yard.x * 0.5f) * 0.5f; // 앞마당 가운데 (상인·거인 시작 위치)

        // 잔디 마당 (거인이 밟고 서는 바닥)
        Box("Yard", root, new Vector3(cx, -0.45f, c.z), new Vector3(yard.x + 40f, 1f, yard.y + 40f), city.parkMat, null, true);

        // 담벽: 서쪽(도시 쪽)은 가운데 대문을 비우고 양쪽으로
        var mpb = new MaterialPropertyBlock(); mpb.SetColor("_Color", wallColor);
        float wy = wallHeight * 0.5f;
        float side = (yard.y - gateWidth) * 0.5f;
        Box("Wall", root, new Vector3(x0, wy, z0 + side * 0.5f), new Vector3(t, wallHeight, side), city.buildingMat, mpb, true);
        Box("Wall", root, new Vector3(x0, wy, z1 - side * 0.5f), new Vector3(t, wallHeight, side), city.buildingMat, mpb, true);
        Box("Wall", root, new Vector3(x1, wy, c.z), new Vector3(t, wallHeight, yard.y), city.buildingMat, mpb, true);
        Box("Wall", root, new Vector3(cx, wy, z0), new Vector3(yard.x, wallHeight, t), city.buildingMat, mpb, true);
        Box("Wall", root, new Vector3(cx, wy, z1), new Vector3(yard.x, wallHeight, t), city.buildingMat, mpb, true);
        // 대문 기둥
        Box("GatePost", root, new Vector3(x0, wallHeight * 0.7f, c.z - gateWidth * 0.5f), new Vector3(t * 2f, wallHeight * 1.4f, t * 2f), city.buildingMat, mpb, true);
        Box("GatePost", root, new Vector3(x0, wallHeight * 0.7f, c.z + gateWidth * 0.5f), new Vector3(t * 2f, wallHeight * 1.4f, t * 2f), city.buildingMat, mpb, true);

        // 집: 마당 안쪽 끝에 정면이 도시(서쪽)를 보게
        if (h)
        {
            h.transform.position = new Vector3(x1 - t - 20f - hb.size.x * 0.5f, 0f, c.z) - new Vector3(hb.center.x, hb.min.y, hb.center.z);
            hb = Bounds(h);
            var hc = new GameObject("HouseCollider"); hc.transform.SetParent(root, false); // 거인이 집을 통과하지 못하게
            var bc = hc.AddComponent<BoxCollider>(); bc.center = hb.center; bc.size = hb.size;
            house = h.transform;
            doorPos = new Vector3(hb.min.x, 0f, hb.center.z);
        }

        // 상인 NPC: 마당 가운데, 대문(도시) 쪽을 보고 서 있음
        var cm = FindObjectOfType<CivilianManager>();
        var prefab = npcPrefab ? npcPrefab : (cm && cm.prefabs != null && cm.prefabs.Length > 0 ? cm.prefabs[0] : null);
        if (prefab)
        {
            var n = Instantiate(prefab, root);
            n.name = "ShopKeeper";
            foreach (var col in n.GetComponentsInChildren<Collider>()) Destroy(col);
            n.transform.rotation = Quaternion.LookRotation(Vector3.left);
            n.transform.localScale = Vector3.one * npcScale; // 소인 (시민과 같은 크기)
            n.transform.position = new Vector3(fx, 0.05f, c.z + 60f);
            npcAnim = n.GetComponentInChildren<Animator>();
            if (npcAnim && cm) { npcAnim.runtimeAnimatorController = cm.locomotion; npcAnim.applyRootMotion = false; }
            npc = n.transform;
        }

        // 거인은 집 마당(대문 안쪽)에서 대문을 바라보며 시작
        if (spawnGiantHere && giant)
        {
            var cc = giant.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            giant.transform.SetPositionAndRotation(new Vector3(fx, 0.1f, c.z - 50f), Quaternion.LookRotation(Vector3.left));
            if (cc) cc.enabled = true;
        }
        built = true;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    void Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, MaterialPropertyBlock mpb, bool solid)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = size;
        go.AddComponent<MeshFilter>().sharedMesh = cube;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        if (mpb != null) mr.SetPropertyBlock(mpb);
        if (solid) go.AddComponent<BoxCollider>();
    }

    // ───────────── 말 걸기 / 상점 열기 ─────────────
    void Update()
    {
        if (!npc || !giant || GameStartMenu.InMenu) { canTalk = false; return; }
        Vector3 d = giant.transform.position - npc.position; d.y = 0f;
        canTalk = d.magnitude < talkRadius * giant.GameScale;
        if (canTalk && d.sqrMagnitude > 1f) // 거인이 가까이 오면 그쪽을 봄
            npc.rotation = Quaternion.RotateTowards(npc.rotation, Quaternion.LookRotation(d), 90f * Time.unscaledDeltaTime);
        if (npcAnim) npcAnim.SetFloat("Speed", 0f);

        if (open)
        {
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape)) Close();
            if (Input.GetKeyDown(KeyCode.Alpha0)) TryBuyPotion();
            int first = CageSkill.Instance ? 1 : 0; // 새장이 1번, 패시브 아이템은 그다음 번호
            if (first == 1 && Input.GetKeyDown(KeyCode.Alpha1)) TryBuyCage();
            for (int k = 0; k < 9 - first; k++) if (Input.GetKeyDown(KeyCode.Alpha1 + first + k)) TryBuy(k);
        }
        else if (Input.GetKeyDown(KeyCode.E) && !CardDraft.IsOpen && Time.timeScale > 0f)
        {
            if (canTalk) Open();
        }
    }

    void Open()
    {
        open = true;
        prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        prevLock = Cursor.lockState; prevVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        message = "어서 와! 부순 건물값으로 뭐든 팔지."; messageUntil = Time.unscaledTime + 3f;
    }

    void Close()
    {
        open = false;
        ClosedFrame = Time.frameCount;
        messageUntil = 0f; // 상점 안 메시지가 닫은 뒤 화면에 남지 않게
        Time.timeScale = prevTimeScale > 0f ? prevTimeScale : 1f;
        Cursor.lockState = prevLock; Cursor.visible = prevVisible;
    }

    void TryBuy(int i)
    {
        var pi = PassiveItems.Instance;
        if (!sellItems || !pi || i >= pi.ItemCount) return;
        var e = pi.GetEntry(i);
        if (e.maxed) { message = "그건 더 강화할 수 없어."; }
        else if (pi.Buy(i)) { message = e.level == 0 ? $"{e.name} 구매 완료!" : $"{e.name} 강화 완료!"; }
        else { message = "돈이 모자라. 건물을 더 부수고 와."; }
        messageUntil = Time.unscaledTime + 2.5f;
    }

    void TryBuyCage()
    {
        var c = CageSkill.Instance;
        if (!c) return;
        bool first = c.Level == 0;
        if (c.Maxed) message = "새장을 더 키울 수는 없어.";
        else if (c.Buy()) message = first ? "새장 구매 완료! 적을 죽이면 새장에 가둬 부하로 쓸 수 있어." : $"새장 강화 완료! 이제 {c.Capacity}명까지 들어가.";
        else message = "돈이 모자라. 건물을 더 부수고 와.";
        messageUntil = Time.unscaledTime + 2.5f;
    }

    // HP 회복약: 즉시 최대 HP의 일정 비율 회복 (HP가 가득 차 있으면 팔지 않음)
    void TryBuyPotion()
    {
        var g = GiantHealth.Instance;
        if (!g) return;
        if (g.HP >= g.maxHP) message = "HP가 이미 가득 찼어.";
        else if (Money.TrySpend(CurrentPotionPrice)) { potionsBought++; g.Heal(g.maxHP * potionHealRatio); message = "회복약을 마셨다! HP 회복."; }
        else message = "돈이 모자라. 건물을 더 부수고 와.";
        messageUntil = Time.unscaledTime + 2.5f;
    }

    // ───────────── 화면 ─────────────
    void OnGUI()
    {
        if (GameStartMenu.InMenu || !npc) return;
        var cam = Camera.main;
        float W = Screen.width, H = Screen.height;

        // NPC 머리 위 이름표
        if (cam && !open)
        {
            Vector3 sp = cam.WorldToScreenPoint(npc.position + Vector3.up * Bounds(npc.gameObject).size.y * 1.05f);
            if (sp.z > 0f && sp.x > 0 && sp.x < W && sp.y > 0 && sp.y < H)
                CardDraft.ShadowLabel(new Rect(sp.x - 100, H - sp.y - 30, 200, 26), "상인", CardDraft.Style(16, FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1f, 0.85f, 0.4f));
            else DrawHomeArrow(cam, W, H);
        }

        if (canTalk && !open)
            CardDraft.ShadowLabel(new Rect(0, H * 0.72f, W, 30), "[E] 상인에게 말 걸기 (상점)", CardDraft.Style(20, FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1f, 0.9f, 0.5f));

        if (open) DrawShop(W, H);
    }

    // 집이 화면 밖일 때 가장자리에 방향 + 거리
    void DrawHomeArrow(Camera cam, float W, float H)
    {
        Vector3 sp = cam.WorldToScreenPoint(npc.position);
        Vector2 dir = new Vector2(sp.x - W * 0.5f, sp.y - H * 0.5f);
        if (sp.z < 0f) dir = -dir;
        if (dir.sqrMagnitude < 1f) dir = Vector2.right;
        dir.Normalize();
        float m = 70f;
        float k = Mathf.Min((W * 0.5f - m) / Mathf.Max(0.001f, Mathf.Abs(dir.x)), (H * 0.5f - m) / Mathf.Max(0.001f, Mathf.Abs(dir.y)));
        Vector2 p = new Vector2(W * 0.5f, H * 0.5f) + dir * k;
        float dist = giant ? Vector3.Distance(giant.transform.position, npc.position) : 0f;
        string arrow = Mathf.Abs(dir.x) > Mathf.Abs(dir.y) ? (dir.x > 0 ? "▶" : "◀") : (dir.y > 0 ? "▲" : "▼");
        CardDraft.ShadowLabel(new Rect(p.x - 90, H - p.y - 22, 180, 44), $"{arrow}\n거인의 집 {dist:0}m", CardDraft.Style(13, FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1f, 0.85f, 0.4f));
    }

    void DrawShop(float W, float H)
    {
        GUI.depth = -150;
        var pi = PassiveItems.Instance;
        int n = sellItems && pi ? pi.ItemCount : 0;
        CardDraft.Box(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.55f));
        int rows = n + 1 + (CageSkill.Instance ? 1 : 0); // 회복약 + 새장 + 패시브 아이템
        float pw = Mathf.Min(640f, W - 40f), rowH = 78f, ph = 150f + rows * rowH + 40f;
        var panel = new Rect((W - pw) * 0.5f, (H - ph) * 0.5f, pw, ph);
        CardDraft.Box(new Rect(panel.x - 3, panel.y - 3, panel.width + 6, panel.height + 6), new Color(1f, 0.85f, 0.4f, 0.85f));
        CardDraft.Box(panel, new Color(0.09f, 0.08f, 0.07f, 0.97f));

        CardDraft.ShadowLabel(new Rect(panel.x, panel.y + 12, pw, 34), "거인의 상점", CardDraft.Style(26, FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1f, 0.85f, 0.4f));
        int money = Money.Instance ? Money.Instance.Amount : 0;
        CardDraft.ShadowLabel(new Rect(panel.x, panel.y + 48, pw, 26), $"가진 돈  {money:N0}", CardDraft.Style(18, FontStyle.Bold, TextAnchor.MiddleCenter), Color.white);
        string msg = Time.unscaledTime < messageUntil ? message : "클릭 또는 숫자키로 구매   ·   E / ESC 닫기";
        GUI.color = new Color(1, 1, 1, 0.7f);
        GUI.Label(new Rect(panel.x, panel.y + 76, pw, 22), msg, CardDraft.Style(13, FontStyle.Normal, TextAnchor.MiddleCenter));
        GUI.color = Color.white;

        var e = Event.current;
        float y = panel.y + 108f;

        // 회복약 (소모품, 0번 키)
        {
            var g = GiantHealth.Instance;
            bool full = !g || g.HP >= g.maxHP;
            if (ShopRow(new Rect(panel.x + 16, y, pw - 32, rowH - 8), "0", "HP 회복약", PotionColor,
                    g ? $"현재 HP  {Mathf.CeilToInt(g.HP)} / {g.maxHP:0}" : "", $"즉시 최대 HP의 {potionHealRatio * 100f:0}% 회복 (살 때마다 가격 {potionPriceGrowth:0.#}배)",
                    full ? "HP 가득" : $"구매  {CurrentPotionPrice:N0}", money >= CurrentPotionPrice, full))
                TryBuyPotion();
            y += rowH;
        }

        // 새장 (1번 키)
        var cage = CageSkill.Instance;
        if (cage)
        {
            string state = cage.Level == 0 ? "미보유" : $"Lv{cage.Level}  ·  포로 {cage.Count} / {cage.Capacity}명";
            string effect = cage.Level == 0 ? "적을 죽이면 새장에 가둬 거인 편으로 (탈것은 탑승자)"
                          : cage.Maxed ? "최대" : $"수용 인원 +{cage.capacityPerLevel}명";
            if (ShopRow(new Rect(panel.x + 16, y, pw - 32, rowH - 8), "1", "새장", CageColor, state, effect,
                    cage.Maxed ? "MAX" : $"{(cage.Level == 0 ? "구매" : "강화")}  {cage.Price:N0}", money >= cage.Price, cage.Maxed))
                TryBuyCage();
            y += rowH;
        }

        for (int i = 0; i < n; i++)
        {
            var it = pi.GetEntry(i);
            if (ShopRow(new Rect(panel.x + 16, y, pw - 32, rowH - 8), $"{i + 1 + (cage ? 1 : 0)}", it.name, it.color, it.state, it.effect,
                    it.maxed ? "MAX" : $"{(it.level == 0 ? "구매" : "강화")}  {it.price:N0}", money >= it.price, it.maxed))
                TryBuy(i);
            y += rowH;
        }

        var close = new Rect(panel.x + pw * 0.5f - 70, panel.yMax - 40, 140, 30);
        CardDraft.Box(close, close.Contains(e.mousePosition) ? new Color(0.5f, 0.5f, 0.55f) : new Color(0.35f, 0.35f, 0.4f));
        GUI.Label(close, "닫기", CardDraft.Style(15, FontStyle.Bold, TextAnchor.MiddleCenter));
        if (e.type == EventType.MouseDown && e.button == 0 && close.Contains(e.mousePosition)) { e.Use(); Close(); }
    }

    // 상점 한 줄: 번호 · 이름 · 상태 · 효과 + 오른쪽 버튼. 버튼을 누르면 true (disabled면 회색 버튼, 눌러도 안내 메시지용으로 true)
    static bool ShopRow(Rect r, string key, string name, Color color, string state, string effect, string button, bool afford, bool disabled)
    {
        var e = Event.current;
        CardDraft.Box(r, new Color(0.15f, 0.14f, 0.13f, 1f));
        CardDraft.Box(new Rect(r.x, r.y, 5, r.height), color);
        GUI.color = new Color(1, 1, 1, 0.45f);
        GUI.Label(new Rect(r.x + 10, r.y, 20, r.height), key, CardDraft.Style(14, FontStyle.Bold, TextAnchor.MiddleCenter));
        GUI.color = Color.white;
        CardDraft.ShadowLabel(new Rect(r.x + 36, r.y + 6, 300, 24), name, CardDraft.Style(18, FontStyle.Bold, TextAnchor.MiddleLeft), color);
        GUI.color = new Color(0.8f, 0.8f, 0.85f);
        GUI.Label(new Rect(r.x + 36, r.y + 30, r.width - 180, 18), state, CardDraft.Style(12, FontStyle.Normal, TextAnchor.MiddleLeft));
        GUI.color = new Color(0.55f, 0.9f, 1f);
        GUI.Label(new Rect(r.x + 36, r.y + 48, r.width - 180, 18), effect, CardDraft.Style(12, FontStyle.Bold, TextAnchor.MiddleLeft));
        GUI.color = Color.white;

        var btn = new Rect(r.xMax - 132, r.y + 14, 120, r.height - 28);
        bool ok = afford && !disabled;
        CardDraft.Box(btn, disabled ? new Color(0.3f, 0.3f, 0.32f) : ok ? (btn.Contains(e.mousePosition) ? new Color(1f, 0.8f, 0.35f) : new Color(0.85f, 0.62f, 0.2f)) : new Color(0.35f, 0.25f, 0.2f));
        GUI.color = ok || disabled ? Color.white : new Color(1, 1, 1, 0.5f);
        GUI.Label(btn, button, CardDraft.Style(15, FontStyle.Bold, TextAnchor.MiddleCenter));
        GUI.color = Color.white;
        if (e.type == EventType.MouseDown && e.button == 0 && btn.Contains(e.mousePosition)) { e.Use(); return true; }
        return false;
    }
}
