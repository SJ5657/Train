using System.Collections.Generic;
using UnityEngine;

// 길가 아이템 + 패시브 아이템.
// 패시브 아이템은 거인의 집 상점(GiantHome)에서 돈으로 구매 (길가 아이템 상자는 roadsidePickups로 켤 때만).
// 없는 아이템은 새로 장착(등급 없음), 이미 가진 아이템은 강화(희귀도 랜덤).
// 패시브 아이템 = 특수 건물 등장 확률(거인 주변에 들어온 흰 건물마다 그 확률로 특수 건물로 바뀜)
// 또는 거인의 식욕(시민 잡아먹기 활성화 + 먹을 때 HP 회복).
// 보유 아이템의 강화 카드는 레벨업 카드에도 능력치 카드와 섞여 나옴 → 고르면 등장 확률 증가.
public class PassiveItems : MonoBehaviour
{
    public static PassiveItems Instance { get; private set; }

    [Header("길가 아이템")]
    [Tooltip("길가에 아이템 상자가 나오게 할지 (끄면 패시브 아이템은 거인의 집 상점에서만 구매)")] public bool roadsidePickups = false;
    [Tooltip("동시에 놓여 있을 수 있는 아이템 수")] public int maxPickups = 2;
    [Tooltip("이 간격(초)마다 아이템이 생길지 확률을 굴림")] public float pickupInterval = 10f;
    [Tooltip("굴릴 때마다 아이템이 생길 확률")] [Range(0, 1)] public float pickupChance = 0.2f;
    [Tooltip("거인으로부터 이 거리 범위의 도로 위에 생김 (m)")] public Vector2 pickupDistance = new Vector2(120f, 320f);
    [Tooltip("거인과 이만큼 멀어지면 사라짐 (m)")] public float despawnDistance = 900f;
    [Tooltip("줍는 반경 (거인 크기 기준)")] public float pickupRadius = 0.7f;
    public Color pickupColor = new Color(0.75f, 0.45f, 1f);

    [Header("특수 건물 아이템")]
    [Tooltip("새 아이템을 얻었을 때의 등장 확률")] [Range(0, 1)] public float newItemChance = 0.01f;
    [Tooltip("일반 등급 강화 1번으로 오르는 등장 확률 (희귀도가 높을수록 배로 늘어남)")] [Range(0, 1)] public float upgradeChance = 0.005f;
    [Tooltip("등장 확률 상한")] [Range(0, 1)] public float maxChance = 0.2f;
    [Tooltip("주변 건물에 확률을 굴리는 간격(초)")] public float rollInterval = 0.5f;

    [Header("시민 먹기 아이템 (거인의 식욕)")]
    [Tooltip("새로 얻었을 때 시민 한 명당 HP 회복량")] public float newEatHeal = 20f;
    [Tooltip("일반 등급 강화 1번으로 늘어나는 회복량 (희귀도가 높을수록 배로 늘어남)")] public float upgradeEatHeal = 10f;

    [Header("상점 가격 (거인의 집)")]
    [Tooltip("공장 아이템 첫 구매 가격")] public int factoryPrice = 150;
    [Tooltip("거인의 식욕 첫 구매 가격")] public int eatPrice = 300;
    [Tooltip("강화할 때마다 가격이 오르는 비율 (0.5 = 레벨마다 +50%)")] public float priceGrowth = 0.5f;

    class Item
    {
        public string name, icon, desc;
        public Color color;
        public SpecialBuildingSet set;
        public int level;        // 0 = 미보유
        public float chance;     // 주변 건물이 이 건물로 바뀔 확률
        public GiantEat eat;     // 시민 먹기 아이템이면 연결 (set 대신)
        public float heal;       // 시민 먹기: 한 명당 HP 회복량
    }
    readonly List<Item> items = new List<Item>();
    readonly List<Item> owned = new List<Item>();
    readonly List<Transform> pickups = new List<Transform>();
    float nextPickupAt, nextRollAt;
    CityGenerator city;
    GiantController ctrl;
    string toast; Color toastColor; float toastUntil;
    static Mesh ringMesh, beamMesh;

    void Awake() { Instance = this; }

    void Start()
    {
        city = FindObjectOfType<CityGenerator>();
        ctrl = FindObjectOfType<GiantController>();
        AddItem(FindObjectOfType<ExplosiveBuildings>(), "공장", "공장", "공장이 더 자주 나타남.\n부수면 확률로 폭발해 주변까지 파괴.", new Color(1f, 0.35f, 0.3f));
        var eat = FindObjectOfType<GiantEat>();
        if (eat) items.Add(new Item { eat = eat, name = "거인의 식욕", icon = "식욕", desc = "시민을 잡아먹을 수 있게 됨.\n(없으면 밟기만 함)", color = new Color(1f, 0.5f, 0.6f) });
        nextPickupAt = Time.time + 3f;
    }

    void AddItem(SpecialBuildingSet set, string name, string icon, string desc, Color color)
    {
        if (set) items.Add(new Item { set = set, name = name, icon = icon, desc = desc, color = color });
    }

    void Update()
    {
        if (GameStartMenu.InMenu || CardDraft.IsOpen) return;
        var g = GiantHealth.Instance;
        if (!g || g.IsDead) return;
        Vector3 gp = g.transform.position;

        // 장착한 아이템: 거인 주변에 새로 들어온 건물마다 등장 확률을 굴림
        if (Time.time >= nextRollAt)
        {
            nextRollAt = Time.time + rollInterval;
            foreach (var it in owned) if (it.set) it.set.RollNearby(gp);
        }

        // 길가 아이템: 줍기 / 멀어지면 제거 / 모자라면 생성
        float pickR = pickupRadius * (ctrl ? ctrl.GameScale : 27f);
        for (int i = pickups.Count - 1; i >= 0; i--)
        {
            var p = pickups[i];
            if (!p) { pickups.RemoveAt(i); continue; }
            Vector3 d = p.position - gp; d.y = 0f;
            if (d.sqrMagnitude <= pickR * pickR) { Collect(p); pickups.RemoveAt(i); }
            else if (d.sqrMagnitude > despawnDistance * despawnDistance) { Destroy(p.gameObject); pickups.RemoveAt(i); }
        }
        // 보유 아이템 수와 관계없이 낮은 확률로 계속 생김
        if (roadsidePickups && items.Count > 0 && pickups.Count < maxPickups && Time.time >= nextPickupAt)
        {
            nextPickupAt = Time.time + pickupInterval;
            if (Random.value < pickupChance && TryRoadPoint(gp, out Vector3 pos)) pickups.Add(CreatePickup(pos));
        }

        // 아이템 상자 애니메이션 (둥실둥실 + 회전)
        foreach (var p in pickups)
        {
            if (!p) continue;
            var gem = p.GetChild(0);
            gem.localPosition = Vector3.up * (16f + Mathf.Sin(Time.time * 2.5f + p.GetInstanceID()) * 3f);
            gem.localRotation = Quaternion.Euler(45f, Time.time * 90f, 45f);
        }
    }

    // ───────────── 패시브 아이템 ─────────────
    void Collect(Transform p)
    {
        Destroy(p.gameObject);
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.15f);
        CardDraft.Enqueue(new CardDraft.Request
        {
            title = "아이템 획득!",
            titleColor = pickupColor,
            subtitle = "아이템을 고르세요 (이미 가진 아이템은 강화)",
            build = BuildItemCards
        });
    }

    // 길가 아이템을 먹었을 때: 아이템 최대 3장. 없는 아이템은 새 아이템(등급 없음), 이미 가진 아이템은 강화(희귀도 랜덤)
    List<CardDraft.Card> BuildItemCards()
    {
        var pool = new List<Item>(items);
        var result = new List<CardDraft.Card>();
        for (int i = pool.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
        for (int i = 0; i < Mathf.Min(3, pool.Count); i++)
            result.Add(pool[i].level == 0 ? NewItemCard(pool[i]) : UpgradeCard(pool[i], CardDraft.RollRarity()));
        return result;
    }

    // 레벨업 카드에 섞일 '보유 아이템 강화' 후보 (희귀도는 LevelUpCards가 굴려서 넘겨줌)
    public List<System.Func<int, CardDraft.Card>> UpgradeOffers()
    {
        var offers = new List<System.Func<int, CardDraft.Card>>();
        foreach (var o in owned) { var it = o; offers.Add(r => UpgradeCard(it, r)); }
        return offers;
    }

    static string Pct(float p) => $"{p * 100f:0.##}%";

    // ───────────── 상점 (거인의 집) ─────────────
    public struct ShopEntry
    {
        public string name, desc, state, effect;
        public Color color;
        public int level, price;
        public bool maxed;
    }

    public int ItemCount => items.Count;

    int Price(Item it) => Mathf.RoundToInt((it.eat ? eatPrice : factoryPrice) * (1f + priceGrowth * it.level));
    float BuyAmount(Item it) => it.eat ? (it.level == 0 ? newEatHeal : upgradeEatHeal) : (it.level == 0 ? newItemChance : upgradeChance);
    bool Maxed(Item it) => !it.eat && it.chance >= maxChance - 0.0001f;

    public ShopEntry GetEntry(int i)
    {
        var it = items[i];
        float a = BuyAmount(it);
        var e = new ShopEntry { name = it.name, desc = it.desc, color = it.color, level = it.level, price = Price(it), maxed = Maxed(it) };
        if (it.eat)
        {
            e.state = it.level == 0 ? "미보유 (밟기만 함)" : $"Lv{it.level}  ·  먹을 때 HP +{it.heal:0}";
            e.effect = it.level == 0 ? $"시민을 잡아먹을 수 있게 됨 (HP +{a:0})" : $"먹을 때 회복 +{a:0}";
        }
        else
        {
            e.state = it.level == 0 ? "미보유" : $"Lv{it.level}  ·  추가 등장 +{Pct(it.chance)}";
            e.effect = e.maxed ? "최대" : $"{it.set.DisplayName} 추가 등장 확률 +{Pct(a)}";
        }
        return e;
    }

    // 돈을 내고 구매(첫 구매는 장착, 이후는 강화). 성공하면 true
    public bool Buy(int i)
    {
        if (i < 0 || i >= items.Count) return false;
        var it = items[i];
        if (Maxed(it) || !Money.TrySpend(Price(it))) return false;
        Equip(it, BuyAmount(it));
        return true;
    }

    CardDraft.Card NewItemCard(Item it)
    {
        if (it.eat)
        {
            float heal = newEatHeal;
            return new CardDraft.Card
            {
                badge = "NEW  새 아이템", badgeColor = new Color(1f, 0.85f, 0.3f),
                name = it.name, stat = "시민 잡아먹기", color = it.color, rarity = -1,
                amount = $"HP +{heal:0}", desc = it.desc,
                current = "밟기만 함", preview = $"먹을 때 HP +{heal:0}",
                footer = "가까이 온 시민을 잡아먹고 HP 회복",
                pick = () => Equip(it, heal)
            };
        }
        float amount = newItemChance;
        return new CardDraft.Card
        {
            badge = "NEW  새 아이템", badgeColor = new Color(1f, 0.85f, 0.3f),
            name = it.name, stat = $"{it.set.DisplayName} 등장 확률", color = it.color, rarity = -1,
            amount = Pct(amount), desc = it.desc,
            current = "미보유", preview = Pct(Mathf.Min(amount, maxChance)),
            footer = "주변 건물이 이 확률로 바뀜",
            pick = () => Equip(it, amount)
        };
    }

    CardDraft.Card UpgradeCard(Item it, int rarity)
    {
        if (it.eat)
        {
            float heal = Mathf.Round(upgradeEatHeal * CardDraft.RarityPower[rarity]);
            return new CardDraft.Card
            {
                badge = $"아이템 강화  Lv {it.level} → {it.level + 1}", badgeColor = new Color(0.55f, 0.9f, 1f),
                name = it.name, stat = "먹을 때 HP 회복", color = it.color, rarity = rarity,
                amount = $"+{heal:0}", desc = it.desc,
                current = $"HP +{it.heal:0}", preview = $"HP +{it.heal + heal:0}",
                footer = "시민 한 명당 회복량",
                pick = () => Equip(it, heal)
            };
        }
        float amount = upgradeChance * CardDraft.RarityPower[rarity];
        return new CardDraft.Card
        {
            badge = $"아이템 강화  Lv {it.level} → {it.level + 1}", badgeColor = new Color(0.55f, 0.9f, 1f),
            name = it.name, stat = $"{it.set.DisplayName} 등장 확률", color = it.color, rarity = rarity,
            amount = "+" + Pct(amount), desc = it.desc,
            current = Pct(it.chance), preview = Pct(Mathf.Min(it.chance + amount, maxChance)),
            footer = it.chance + amount >= maxChance ? $"최대 {Pct(maxChance)}" : "주변 건물이 이 확률로 바뀜",
            pick = () => Equip(it, amount)
        };
    }

    // 장착/강화: 확률을 올리고 바로 주변 건물에 적용 (시민 먹기는 회복량 증가 + 먹기 활성화)
    void Equip(Item it, float amount)
    {
        if (it.level == 0) owned.Add(it);
        it.level++;
        if (it.eat)
        {
            it.heal += amount;
            it.eat.unlocked = true;
            it.eat.healPerCivilian = it.heal;
            if (it.level == 1) Toast("이제 시민을 잡아먹을 수 있다!", it.color);
            return;
        }
        it.chance = Mathf.Min(it.chance + amount, maxChance);
        int n = it.set.SetChance(it.chance);
        var g = GiantHealth.Instance;
        if (g) n += it.set.RollNearby(g.transform.position);
        if (n > 0) Toast($"주변에 {it.set.DisplayName} {n}채가 나타났다!", it.color);
    }

    // ───────────── 세이브 (SaveGame) ─────────────
    [System.Serializable]
    public class State
    {
        public int[] level;
        public float[] chance, heal;
        public int[] owned; // 장착한 순서 (화면 왼쪽 위 아이콘 순서)
    }

    public State Capture()
    {
        var s = new State { level = new int[items.Count], chance = new float[items.Count], heal = new float[items.Count], owned = new int[owned.Count] };
        for (int i = 0; i < items.Count; i++) { s.level[i] = items[i].level; s.chance[i] = items[i].chance; s.heal[i] = items[i].heal; }
        for (int i = 0; i < owned.Count; i++) s.owned[i] = items.IndexOf(owned[i]);
        return s;
    }

    // 아이템 레벨·효과를 되돌림. 공장 배치는 SaveGame이 먼저 되돌리므로 여기선 확률만 맞추고 새로 굴리지 않음
    public void Restore(State s)
    {
        if (s.level == null) return;
        owned.Clear();
        for (int i = 0; i < items.Count && i < s.level.Length; i++)
        {
            var it = items[i];
            it.level = s.level[i];
            it.chance = s.chance[i];
            it.heal = s.heal[i];
            if (it.level <= 0) continue;
            if (it.eat) { it.eat.unlocked = true; it.eat.healPerCivilian = it.heal; }
            else it.set.RestoreChance(it.chance);
        }
        if (s.owned != null)
            foreach (int i in s.owned) if (i >= 0 && i < items.Count && items[i].level > 0 && !owned.Contains(items[i])) owned.Add(items[i]);
    }

    // ───────────── 길가 아이템 ─────────────
    // 거인 주변 거리 범위 안의 도로 한가운데 지점 찾기
    bool TryRoadPoint(Vector3 center, out Vector3 pos)
    {
        pos = Vector3.zero;
        if (!city) return false;
        float pitch = city.blockSize + city.roadWidth;
        float sizeX = city.blocksX * pitch + city.roadWidth, sizeZ = city.blocksZ * pitch + city.roadWidth;
        Vector3 origin = city.transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f;
        float half = city.roadWidth * 0.5f;

        for (int attempt = 0; attempt < 30; attempt++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f), dist = Random.Range(pickupDistance.x, pickupDistance.y);
            Vector3 p = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;
            // 가장 가까운 세로/가로 도로 중심선으로 붙임
            float rx = origin.x + half + Mathf.Round((p.x - origin.x - half) / pitch) * pitch;
            float rz = origin.z + half + Mathf.Round((p.z - origin.z - half) / pitch) * pitch;
            if (Mathf.Abs(p.x - rx) < Mathf.Abs(p.z - rz)) p.x = rx; else p.z = rz;
            if (p.x < origin.x || p.x > origin.x + sizeX || p.z < origin.z || p.z > origin.z + sizeZ) continue;
            if (city.OnClosedRoad(p)) continue; // 블록을 붙여 없어진 도로 자리(건물이 들어섬)
            if (ctrl && ctrl.areaHalfExtent.x > 0 && (Mathf.Abs(p.x) > ctrl.areaHalfExtent.x || Mathf.Abs(p.z) > ctrl.areaHalfExtent.y)) continue;
            Vector3 d = p - center; d.y = 0f;
            if (d.magnitude < pickupDistance.x * 0.8f) continue;
            bool crowded = false;
            foreach (var q in pickups) if (q && (q.position - p).sqrMagnitude < 80f * 80f) { crowded = true; break; }
            if (crowded) continue;
            pos = new Vector3(p.x, 0.5f, p.z);
            return true;
        }
        return false;
    }

    // 아이템 상자: 빛나는 보석 + 하늘로 뻗는 빛기둥 + 바닥 원
    Transform CreatePickup(Vector3 pos)
    {
        if (!ringMesh)
        {
            Color c = pickupColor;
            ringMesh = WarningZone.Annulus(0.8f, 1f, new Color(c.r, c.g, c.b, 0.9f), new Color(c.r, c.g, c.b, 0.9f));
            beamMesh = WarningZone.Wall(new Color(c.r, c.g, c.b, 0.55f), new Color(c.r, c.g, c.b, 0f));
        }
        var root = new GameObject("ItemPickup").transform;
        root.position = pos;

        var gem = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(gem.GetComponent<Collider>());
        gem.name = "Gem";
        gem.transform.SetParent(root, false);
        gem.transform.localScale = Vector3.one * 9f;
        var gr = gem.GetComponent<MeshRenderer>();
        gr.sharedMaterial = WarningZone.material;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_Color", Color.Lerp(pickupColor, Color.white, 0.25f));
        gr.SetPropertyBlock(mpb);
        gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Part(root, "Beam", beamMesh, new Vector3(5f, 140f, 5f), Vector3.zero);
        Part(root, "Ring", ringMesh, new Vector3(14f, 1f, 14f), Vector3.up * 0.3f);
        return root;
    }

    static void Part(Transform parent, string name, Mesh mesh, Vector3 scale, Vector3 localPos)
    {
        var p = new GameObject(name);
        p.transform.SetParent(parent, false);
        p.transform.localPosition = localPos;
        p.transform.localScale = scale;
        p.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = p.AddComponent<MeshRenderer>();
        mr.sharedMaterial = WarningZone.material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // ───────────── 화면 ─────────────
    void Toast(string text, Color c) { toast = text; toastColor = c; toastUntil = Time.unscaledTime + 3f; }

    void OnGUI()
    {
        if (GameStartMenu.InMenu || GameStartMenu.HideHud) return; // 시작 메뉴 중이거나 HUD 숨김이면 그리지 않음
        DrawOwned();
        float left = toastUntil - Time.unscaledTime;
        if (!CardDraft.IsOpen && !string.IsNullOrEmpty(toast) && left > 0f)
        {
            var c = toastColor; c.a = Mathf.Clamp01(left / 0.5f);
            CardDraft.ShadowLabel(new Rect(0, Screen.height * 0.16f, Screen.width, 34), toast, CardDraft.Style(22, FontStyle.Bold, TextAnchor.MiddleCenter), c);
        }
    }

    // 장착한 패시브 아이템 (HP/스테미나/공격력 아래, 한 줄에 5개씩)
    void DrawOwned()
    {
        const float size = 46f, gap = 8f, x0 = 20f, y0 = 92f;
        const int perRow = 5;
        for (int i = 0; i < owned.Count; i++)
        {
            var it = owned[i];
            var r = new Rect(x0 + (i % perRow) * (size + gap), y0 + (i / perRow) * (size + gap), size, size);
            CardDraft.Box(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0, 0, 0, 0.6f));
            CardDraft.Box(r, new Color(it.color.r * 0.45f, it.color.g * 0.45f, it.color.b * 0.45f, 0.95f));
            CardDraft.ShadowLabel(new Rect(r.x + 3, r.y + 1, r.width - 3, 13), $"Lv{it.level}", CardDraft.Style(10, FontStyle.Bold, TextAnchor.MiddleLeft), new Color(1f, 1f, 1f, 0.8f));
            CardDraft.ShadowLabel(new Rect(r.x, r.y + 6, r.width, r.height - 18), it.icon, CardDraft.Style(14, FontStyle.Bold, TextAnchor.MiddleCenter), Color.white);
            CardDraft.ShadowLabel(new Rect(r.x, r.yMax - 16, r.width - 3, 15), it.eat ? $"+{it.heal:0}" : Pct(it.chance), CardDraft.Style(11, FontStyle.Bold, TextAnchor.MiddleRight), it.color);
        }
    }
}
