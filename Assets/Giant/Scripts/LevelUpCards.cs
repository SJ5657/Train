using System;
using System.Collections.Generic;
using UnityEngine;

// 메가봉크 스타일 레벨업: 레벨이 오르면 능력치 강화 카드 3장 중 1장을 고름 (CardDraft로 표시).
// 카드는 능력치 강화 + 보유한 패시브 아이템의 강화(PassiveItems). 희귀도가 높을수록 강화 폭이 큼.
// 한 번에 여러 레벨이 오르면 그 횟수만큼 연속으로 고름.
// chooseCards를 끄면 카드 없이 레벨업마다 HP·몸 크기·공격력이 자동으로 오름.
public class LevelUpCards : MonoBehaviour
{
    [Header("레벨업 방식")]
    [Tooltip("켜면 능력치 카드 3장 중 고르기, 끄면 아래 자동 성장")] public bool chooseCards = false;
    [Tooltip("자동 성장: 레벨업마다 최대 HP 증가 (늘어난 만큼 회복)")] public float autoHp = 100f;
    [Tooltip("자동 성장: 레벨업마다 몸 크기 증가 (처음 크기 기준 비율)")] public float autoSize = 0.03f;
    [Tooltip("자동 성장: 레벨업마다 공격력 증가")] public float autoAttack = 8f;

    [Header("몸 크기 강화")]
    [Tooltip("몸 크기 최대 배율 (처음 크기 기준)")] public float maxSizeMultiplier = 2.5f;
    [Tooltip("카드 선택 후 커지는 데 걸리는 시간(초)")] public float growTime = 0.8f;

    class Stat
    {
        public string name, stat, desc;
        public Color color;
        public float baseAmount;
        public Func<float, string> amountText;  // 카드 가운데 큰 글씨 (예: +12%)
        public Func<string> current;            // 현재 값
        public Func<float, string> preview;     // 고른 뒤 값
        public Action<float> apply;
        public Func<bool> available;            // 상한 도달 시 카드에서 제외
        public bool capAmount;                  // 감소형(방어력/지구력): 한 번에 60%까지만
        public int times;                       // 지금까지 고른 횟수
    }
    readonly List<Stat> stats = new List<Stat>();

    GiantController ctrl; GiantHealth hp; GiantStamina st; GiantStomp stomp; LevelSystem lv;
    float baseWalk, baseRun, baseDrain, baseWalkRegen, baseIdleRegen, baseModelScale = 1f;
    float moveMult = 1f, staminaRegenMult = 1f, drainMult = 1f, sizeMult = 1f, shownSizeMult = 1f;
    bool sizeBaseSet;

    void Start()
    {
        lv = LevelSystem.Instance ? LevelSystem.Instance : FindObjectOfType<LevelSystem>();
        ctrl = FindObjectOfType<GiantController>();
        hp = GiantHealth.Instance ? GiantHealth.Instance : FindObjectOfType<GiantHealth>();
        st = FindObjectOfType<GiantStamina>();
        stomp = FindObjectOfType<GiantStomp>();
        if (ctrl) { baseWalk = ctrl.walkSpeed; baseRun = ctrl.runSpeed; }
        if (st) { baseDrain = st.runDrain; baseWalkRegen = st.walkRegen; baseIdleRegen = st.idleRegen; }
        BuildStats();
        if (lv) lv.OnLevelUp += HandleLevelUp;
    }

    void OnDestroy() { if (lv) lv.OnLevelUp -= HandleLevelUp; }

    // ───────────── 능력치 목록 ─────────────
    static string Pct(float v) => $"+{v * 100f:0.#}%";

    void BuildStats()
    {
        if (stomp)
            Add("파괴의 주먹", "공격력", "건물에 주는 피해 증가.\n단단한 건물도 빨리 무너짐.", new Color(1f, 0.55f, 0.3f), 15f,
                a => $"+{a:0}", () => $"{stomp.attackPower:0}", a => $"{stomp.attackPower + a:0}",
                a => stomp.attackPower += a);
        if (hp)
        {
            Add("거인의 심장", "최대 체력", "최대 HP 증가.\n늘어난 만큼 즉시 회복.", new Color(0.95f, 0.3f, 0.35f), 200f,
                a => $"+{a:0}", () => $"{hp.maxHP:0}", a => $"{hp.maxHP + a:0}",
                a => hp.AddMaxHP(a));
            Add("트롤의 피", "체력 재생", "매초 HP가 자동으로 회복.", new Color(0.4f, 0.85f, 0.45f), 4f,
                a => $"+{a:0.#}/초", () => $"{hp.hpRegen:0.#}/초", a => $"{hp.hpRegen + a:0.#}/초",
                a => hp.hpRegen += a);
            Add("강철 피부", "방어력", "군대에게 받는 피해 감소.", new Color(0.65f, 0.72f, 0.8f), 0.05f,
                a => $"-{a * 100f:0.#}%", () => $"피해 {hp.damageTakenMultiplier * 100f:0}%",
                a => $"피해 {hp.damageTakenMultiplier * (1f - a) * 100f:0}%",
                a => hp.damageTakenMultiplier *= (1f - a),
                () => hp.damageTakenMultiplier > 0.25f);
        }
        if (ctrl)
        {
            Add("바람의 샌들", "이동 속도", "걷기·달리기 속도 증가.", new Color(0.45f, 0.85f, 1f), 0.06f,
                Pct, () => $"{moveMult * 100f:0}%", a => $"{(moveMult + a) * 100f:0}%",
                a => { moveMult += a; ctrl.walkSpeed = baseWalk * moveMult; ctrl.runSpeed = baseRun * moveMult; });
        }
        if (st)
        {
            Add("무쇠 폐", "최대 스테미나", "스테미나 최대치 증가.\n늘어난 만큼 즉시 채움.", new Color(0.95f, 0.8f, 0.25f), 15f,
                a => $"+{a:0}", () => $"{st.maxStamina:0}", a => $"{st.maxStamina + a:0}",
                a => st.AddMaxStamina(a));
            Add("숨고르기", "스테미나 회복", "걷거나 멈췄을 때\n스테미나 회복 속도 증가.", new Color(1f, 0.6f, 0.75f), 0.12f,
                Pct, () => $"{staminaRegenMult * 100f:0}%", a => $"{(staminaRegenMult + a) * 100f:0}%",
                a => { staminaRegenMult += a; st.walkRegen = baseWalkRegen * staminaRegenMult; st.idleRegen = baseIdleRegen * staminaRegenMult; });
            Add("철인의 다리", "지구력", "달릴 때 스테미나 소모 감소.", new Color(0.85f, 0.55f, 0.3f), 0.08f,
                a => $"-{a * 100f:0.#}%", () => $"소모 {drainMult * 100f:0}%", a => $"소모 {drainMult * (1f - a) * 100f:0}%",
                a => { drainMult *= (1f - a); st.runDrain = baseDrain * drainMult; },
                () => drainMult > 0.25f);
        }
        if (ctrl)
        {
            Add("거대화", "몸 크기", "몸이 커져 더 넓게 부수고\n보폭이 커져 더 빨라짐.", new Color(0.8f, 0.45f, 1f), 0.05f,
                Pct, () => $"{sizeMult * 100f:0}%", a => $"{Mathf.Min(sizeMult + a, maxSizeMultiplier) * 100f:0}%",
                a => { if (!sizeBaseSet) { baseModelScale = ctrl.ModelScale; sizeBaseSet = true; } sizeMult = Mathf.Min(sizeMult + a, maxSizeMultiplier); }, // 성별 선택 후의 모델 크기 기준
                () => sizeMult < maxSizeMultiplier - 0.001f, capAmount: false);
        }
        if (lv)
        {
            Add("현자의 눈", "경험치 획득", "얻는 경험치 증가.\n레벨업이 빨라짐.", new Color(0.4f, 0.65f, 1f), 0.08f,
                Pct, () => $"{lv.xpMultiplier * 100f:0}%", a => $"{(lv.xpMultiplier + a) * 100f:0}%",
                a => lv.xpMultiplier += a);
        }
    }

    void Add(string name, string stat, string desc, Color color, float baseAmount, Func<float, string> amountText,
             Func<string> current, Func<float, string> preview, Action<float> apply, Func<bool> available = null, bool capAmount = true)
    {
        stats.Add(new Stat
        {
            name = name, stat = stat, desc = desc, color = color, baseAmount = baseAmount, amountText = amountText,
            current = current, preview = preview, apply = apply, available = available,
            capAmount = available != null && capAmount
        });
    }

    // ───────────── 레벨업 ─────────────
    void HandleLevelUp(int level)
    {
        if (!chooseCards) { AutoGrow(); return; }
        CardDraft.Enqueue(new CardDraft.Request
        {
            title = $"LEVEL UP!  LV {level}",
            subtitle = "강화할 능력치를 고르세요",
            build = BuildCards
        });
    }

    // 후보 = 능력치 + 보유한 패시브 아이템 강화. 섞어서 3장, 카드마다 희귀도를 따로 굴림
    List<CardDraft.Card> BuildCards()
    {
        var offers = new List<Func<int, CardDraft.Card>>();
        foreach (var s in stats)
            if (s.available == null || s.available()) { var stat = s; offers.Add(r => StatCard(stat, r)); }
        if (PassiveItems.Instance) offers.AddRange(PassiveItems.Instance.UpgradeOffers());

        var result = new List<CardDraft.Card>();
        if (offers.Count == 0) return result;
        for (int i = offers.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (offers[i], offers[j]) = (offers[j], offers[i]); }
        for (int i = 0; i < 3; i++)
            result.Add(offers[i % offers.Count](CardDraft.RollRarity())); // 후보가 3개보다 적으면 같은 항목이 다른 희귀도로 나올 수 있음
        return result;
    }

    CardDraft.Card StatCard(Stat s, int r)
    {
        float amount = s.baseAmount * CardDraft.RarityPower[r];
        if (s.capAmount) amount = Mathf.Min(amount, 0.6f);
        return new CardDraft.Card
        {
            badge = "능력치 강화", badgeColor = new Color(0.55f, 0.9f, 1f),
            name = s.name, stat = s.stat, color = s.color, rarity = r,
            amount = s.amountText(amount), desc = s.desc,
            current = s.current(), preview = s.preview(amount),
            footer = s.times > 0 ? $"지금까지 {s.times}번 강화" : "",
            pick = () => { s.apply(amount); s.times++; }
        };
    }

    void Update()
    {
        // 몸 크기 강화: 카드 선택 후 게임이 다시 진행되면 부드럽게 커짐
        if (CardDraft.IsOpen || !ctrl || !sizeBaseSet || Mathf.Approximately(shownSizeMult, sizeMult)) return;
        float step = Mathf.Max(0.01f, sizeMult - 1f) / Mathf.Max(0.05f, growTime) * Time.deltaTime;
        shownSizeMult = Mathf.MoveTowards(shownSizeMult, sizeMult, Mathf.Max(step, 0.3f * Time.deltaTime));
        ctrl.SetModelScale(baseModelScale * shownSizeMult);
    }

    // ───────────── 세이브 (SaveGame) ─────────────
    [Serializable]
    public class State
    {
        public float attack, maxHP, hpRegen, damageTaken, maxStamina, xpMultiplier;
        public float moveMult = 1f, staminaRegenMult = 1f, drainMult = 1f, sizeMult = 1f;
        public int[] times;
    }

    public State Capture()
    {
        var s = new State
        {
            moveMult = moveMult, staminaRegenMult = staminaRegenMult, drainMult = drainMult, sizeMult = sizeMult,
            times = new int[stats.Count]
        };
        for (int i = 0; i < stats.Count; i++) s.times[i] = stats[i].times;
        if (stomp) s.attack = stomp.attackPower;
        if (hp) { s.maxHP = hp.maxHP; s.hpRegen = hp.hpRegen; s.damageTaken = hp.damageTakenMultiplier; }
        if (st) s.maxStamina = st.maxStamina;
        if (lv) s.xpMultiplier = lv.xpMultiplier;
        return s;
    }

    // 강화된 능력치를 그대로 되돌림 (HP는 SaveGame이 최대 HP를 맞춘 뒤 따로 되돌림)
    public void Restore(State s)
    {
        if (s.times != null) for (int i = 0; i < stats.Count && i < s.times.Length; i++) stats[i].times = s.times[i];
        if (stomp && s.attack > 0f) stomp.attackPower = s.attack;
        if (hp && s.maxHP > 0f) { hp.maxHP = s.maxHP; hp.hpRegen = s.hpRegen; hp.damageTakenMultiplier = s.damageTaken; }
        if (lv && s.xpMultiplier > 0f) lv.xpMultiplier = s.xpMultiplier;
        if (ctrl)
        {
            moveMult = s.moveMult;
            ctrl.walkSpeed = baseWalk * moveMult; ctrl.runSpeed = baseRun * moveMult;
        }
        if (st)
        {
            if (s.maxStamina > 0f) st.AddMaxStamina(s.maxStamina - st.maxStamina);
            staminaRegenMult = s.staminaRegenMult; drainMult = s.drainMult;
            st.walkRegen = baseWalkRegen * staminaRegenMult; st.idleRegen = baseIdleRegen * staminaRegenMult;
            st.runDrain = baseDrain * drainMult;
        }
        if (ctrl && s.sizeMult > 1f)
        {
            if (!sizeBaseSet) { baseModelScale = ctrl.ModelScale; sizeBaseSet = true; }
            sizeMult = shownSizeMult = Mathf.Min(s.sizeMult, maxSizeMultiplier);
            ctrl.SetModelScale(baseModelScale * sizeMult);
        }
    }

    // ───────────── 자동 성장 (카드 끔) ─────────────
    string growText; float growUntil;

    void AutoGrow()
    {
        var parts = new List<string>();
        if (hp && autoHp > 0f) { hp.AddMaxHP(autoHp); parts.Add($"HP +{autoHp:0}"); }
        if (ctrl && autoSize > 0f && sizeMult < maxSizeMultiplier - 0.001f)
        {
            if (!sizeBaseSet) { baseModelScale = ctrl.ModelScale; sizeBaseSet = true; } // 성별 선택 후의 모델 크기 기준
            sizeMult = Mathf.Min(sizeMult + autoSize, maxSizeMultiplier);
            parts.Add($"크기 +{autoSize * 100f:0.#}%");
        }
        if (stomp && autoAttack > 0f) { stomp.attackPower += autoAttack; parts.Add($"공격력 +{autoAttack:0}"); }
        growText = string.Join("   ·   ", parts);
        growUntil = Time.time + 2.5f;
    }

    // 레벨업 알림(LevelSystem) 바로 아래에 오른 능력치 표시
    void OnGUI()
    {
        if (GameStartMenu.InMenu || string.IsNullOrEmpty(growText) || Time.time >= growUntil) return;
        float a = Mathf.Clamp01((growUntil - Time.time) / 0.5f);
        float y = Screen.height - 26f - 14f - 26f;
        CardDraft.ShadowLabel(new Rect(0, y, Screen.width, 24), growText, CardDraft.Style(17, FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1f, 0.85f, 0.4f, a));
    }
}
