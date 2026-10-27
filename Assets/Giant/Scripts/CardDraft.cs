using System;
using System.Collections.Generic;
using UnityEngine;

// 카드 3장 중 1장 고르기 화면 (메가봉크 스타일). 레벨업 능력치 카드(LevelUpCards)와 아이템 카드(PassiveItems)가 함께 사용.
// 요청이 여러 개 쌓이면 순서대로 보여줌. 보여주는 동안 게임 일시정지, 마우스 클릭 또는 1/2/3 키로 선택.
public class CardDraft : MonoBehaviour
{
    public static CardDraft Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.current != null;

    public static readonly string[] RarityNames = { "일반", "고급", "희귀", "영웅", "전설" };
    public static readonly Color[] RarityColors =
    {
        new Color(0.72f, 0.74f, 0.78f),
        new Color(0.35f, 0.85f, 0.40f),
        new Color(0.30f, 0.62f, 1.00f),
        new Color(0.74f, 0.42f, 1.00f),
        new Color(1.00f, 0.76f, 0.20f)
    };
    public static readonly float[] RarityPower = { 1f, 1.5f, 2.2f, 3.2f, 5f };

    [Header("희귀도 가중치 (일반 / 고급 / 희귀 / 영웅 / 전설)")]
    public float[] rarityWeights = { 60f, 25f, 10f, 4f, 1f };
    [Tooltip("레벨이 1 오를 때마다 높은 희귀도 가중치가 늘어나는 비율")] public float luckPerLevel = 0.04f;
    [Tooltip("카드가 뜬 직후 입력을 무시하는 시간(초) — 이동 중 실수로 고르는 것 방지")] public float inputDelay = 0.4f;

    public class Card
    {
        public string badge, name, stat, amount, desc, current, preview, footer;
        public Color color = Color.white, badgeColor = new Color(1f, 0.85f, 0.3f);
        public int rarity;
        public Action pick;
    }

    public class Request
    {
        public string title, subtitle;
        public Color titleColor = new Color(0.55f, 0.9f, 1f);
        public Func<List<Card>> build; // 보여주는 순간에 만듦 (현재 수치가 최신이 되도록)
    }

    readonly Queue<Request> queue = new Queue<Request>();
    Request current;
    List<Card> cards;
    float openedAt, prevTimeScale = 1f;
    CursorLockMode prevLock; bool prevVisible;

    void Awake() { Instance = this; }

    void OnDestroy()
    {
        if (current != null) Time.timeScale = 1f; // 고르는 중에 씬이 바뀌어도 게임이 멈춘 채로 남지 않게
        if (Instance == this) Instance = null;
    }

    public static void Enqueue(Request r)
    {
        if (!Instance || r == null) return;
        Instance.queue.Enqueue(r);
        if (Instance.current == null) Instance.ShowNext();
    }

    public static int RollRarity()
    {
        var self = Instance;
        float[] baseW = self ? self.rarityWeights : new[] { 60f, 25f, 10f, 4f, 1f };
        float luck = self ? self.luckPerLevel : 0.04f;
        int level = LevelSystem.Instance ? LevelSystem.Instance.Level : 1;
        var w = new float[RarityPower.Length];
        float total = 0f;
        for (int i = 0; i < w.Length; i++)
        {
            w[i] = (i < baseW.Length ? baseW[i] : 0f) * (1f + luck * (level - 1) * i); // 높은 희귀도일수록 레벨 보정 큼
            total += w[i];
        }
        float roll = UnityEngine.Random.value * total;
        for (int i = 0; i < w.Length; i++) { roll -= w[i]; if (roll <= 0f) return i; }
        return 0;
    }

    void ShowNext()
    {
        while (queue.Count > 0)
        {
            var r = queue.Dequeue();
            var list = r.build != null ? r.build() : null;
            if (list == null || list.Count == 0) continue;
            if (current == null)
            {
                prevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
                prevLock = Cursor.lockState; prevVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            }
            current = r; cards = list;
            openedAt = Time.unscaledTime;
            return;
        }
        if (current != null)
        {
            current = null; cards = null;
            Time.timeScale = prevTimeScale;
            Cursor.lockState = prevLock; Cursor.visible = prevVisible;
        }
    }

    void Pick(int i)
    {
        if (current == null || i < 0 || i >= cards.Count) return;
        if (Time.unscaledTime - openedAt < inputDelay) return;
        cards[i].pick?.Invoke();
        ShowNext();
    }

    void Update()
    {
        if (current == null) return;
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) Pick(0);
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) Pick(1);
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) Pick(2);
    }

    // ───────────── 화면 ─────────────
    void OnGUI()
    {
        if (current == null) return;
        GUI.depth = -100;
        float W = Screen.width, H = Screen.height;
        float now = Time.unscaledTime - openedAt;
        var e = Event.current;

        Box(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.6f * Mathf.Clamp01(now / 0.2f)));

        int n = cards.Count;
        float cw = Mathf.Clamp(W * 0.2f, 190f, 270f), ch = cw * 1.45f, gap = cw * 0.14f;
        float total = n * cw + (n - 1) * gap;
        float x0 = (W - total) * 0.5f, y0 = H * 0.5f - ch * 0.5f + 24f;

        ShadowLabel(new Rect(0, y0 - cw * 0.42f, W, cw * 0.2f), current.title, Style(Mathf.RoundToInt(cw * 0.17f), FontStyle.Bold, TextAnchor.MiddleCenter), current.titleColor);
        string sub = current.subtitle + "   ( 클릭 또는 1 / 2 / 3 )" + (queue.Count > 0 ? $"   ·   남은 선택 {queue.Count}" : "");
        ShadowLabel(new Rect(0, y0 - cw * 0.2f, W, 24), sub, Style(15, FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.85f));

        for (int i = 0; i < n; i++)
        {
            // 등장 애니메이션 (순서대로 아래에서 올라옴)
            float t = Mathf.Clamp01((now - i * 0.07f) / 0.28f);
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            var r = new Rect(x0 + i * (cw + gap), y0 + (1f - ease) * 70f, cw, ch);

            bool ready = now >= inputDelay;
            bool hover = ready && r.Contains(e.mousePosition);
            if (hover) r = new Rect(r.x - 5, r.y - 12, r.width + 10, r.height + 10);

            DrawCard(r, cards[i], i, ease, hover);

            if (ready && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                e.Use();
                Pick(i);
                return;
            }
        }
    }

    static void DrawCard(Rect r, Card c, int index, float a, bool hover)
    {
        bool noRarity = c.rarity < 0; // 등급 없는 카드 (새 패시브 아이템): 아이템 색으로 표시
        Color rc = noRarity ? c.color : RarityColors[Mathf.Min(c.rarity, RarityColors.Length - 1)];
        // 전설은 테두리가 반짝임
        Color border = c.rarity == 4 ? Color.Lerp(rc, Color.white, Mathf.PingPong(Time.unscaledTime * 1.6f, 0.6f)) : rc;
        border.a = a * (hover ? 1f : 0.85f);

        float bw = hover ? 4f : 3f;
        Box(new Rect(r.x - bw, r.y - bw, r.width + bw * 2, r.height + bw * 2), border);
        Box(r, new Color(0.09f, 0.10f, 0.13f, 0.97f * a));

        float cw = r.width;
        // 희귀도 띠
        float bandH = cw * 0.14f;
        Box(new Rect(r.x, r.y, r.width, bandH), new Color(rc.r * 0.55f, rc.g * 0.55f, rc.b * 0.55f, a));
        ShadowLabel(new Rect(r.x, r.y, r.width, bandH), noRarity ? "패시브 아이템" : RarityNames[Mathf.Min(c.rarity, RarityNames.Length - 1)],
            Style(Mathf.RoundToInt(cw * 0.065f), FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1, 1, 1, a));

        // 배지 (능력치 강화 / 새 아이템 / 아이템 강화)
        float y = r.y + bandH + cw * 0.05f;
        var bc = c.badgeColor; bc.a = a;
        ShadowLabel(new Rect(r.x, y, r.width, cw * 0.08f), c.badge, Style(Mathf.RoundToInt(cw * 0.055f), FontStyle.Bold, TextAnchor.MiddleCenter), bc);

        // 이름 + 능력치
        y += cw * 0.09f;
        ShadowLabel(new Rect(r.x + 8, y, r.width - 16, cw * 0.13f), c.name,
            Style(Mathf.RoundToInt(cw * 0.09f), FontStyle.Bold, TextAnchor.MiddleCenter), new Color(c.color.r, c.color.g, c.color.b, a));
        y += cw * 0.13f;
        GUI.color = new Color(0.75f, 0.77f, 0.82f, a);
        GUI.Label(new Rect(r.x, y, r.width, cw * 0.08f), c.stat, Style(Mathf.RoundToInt(Mathf.Clamp(cw * 0.055f, 12, 15)), FontStyle.Bold, TextAnchor.MiddleCenter));

        // 강화량 (크게)
        y += cw * 0.09f;
        ShadowLabel(new Rect(r.x, y, r.width, cw * 0.22f), c.amount,
            Style(Mathf.RoundToInt(cw * 0.15f), FontStyle.Bold, TextAnchor.MiddleCenter), new Color(rc.r, rc.g, rc.b, a));

        // 설명
        y += cw * 0.25f;
        var desc = Style(Mathf.RoundToInt(Mathf.Clamp(cw * 0.052f, 12, 14)), FontStyle.Normal, TextAnchor.UpperCenter);
        desc.wordWrap = true;
        GUI.color = new Color(0.82f, 0.84f, 0.88f, a);
        GUI.Label(new Rect(r.x + 12, y, r.width - 24, cw * 0.26f), c.desc, desc);

        // 현재 → 고른 후
        float by = r.yMax - cw * 0.36f;
        Box(new Rect(r.x + 10, by, r.width - 20, 1), new Color(1, 1, 1, 0.12f * a));
        var cmp = Style(Mathf.RoundToInt(Mathf.Clamp(cw * 0.058f, 12, 16)), FontStyle.Bold, TextAnchor.MiddleCenter);
        GUI.color = new Color(1, 1, 1, a);
        GUI.Label(new Rect(r.x, by + 6, r.width, 24), $"{c.current}  →  <color=#{ColorUtility.ToHtmlStringRGB(rc)}>{c.preview}</color>", cmp);

        // 안내 + 단축키
        var small = Style(12, FontStyle.Normal, TextAnchor.MiddleCenter);
        GUI.color = new Color(0.7f, 0.72f, 0.78f, a);
        GUI.Label(new Rect(r.x, by + 30, r.width, 18), c.footer ?? "", small);
        var key = Style(Mathf.RoundToInt(cw * 0.07f), FontStyle.Bold, TextAnchor.MiddleCenter);
        GUI.color = new Color(1, 1, 1, (hover ? 1f : 0.6f) * a);
        GUI.Label(new Rect(r.x, r.yMax - cw * 0.12f, r.width, cw * 0.1f), $"[ {index + 1} ]", key);
        GUI.color = Color.white;
    }

    public static GUIStyle Style(int size, FontStyle fs, TextAnchor anchor)
        => new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = anchor, richText = true };

    public static void Box(Rect r, Color c)
    {
        var old = GUI.color; GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    public static void ShadowLabel(Rect r, string text, GUIStyle s, Color c)
    {
        GUI.color = new Color(0, 0, 0, 0.7f * c.a);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, s);
        GUI.color = c;
        GUI.Label(r, text, s);
        GUI.color = Color.white;
    }
}
