using System;
using System.Collections.Generic;
using UnityEngine;

// 레벨업 스킬 선택: 레벨이 오를 때마다 스킬 카드 3장 중 1장을 고름 (CardDraft로 표시). 능력치는 오르지 않음.
// 한 번에 여러 레벨이 오르면 그 횟수만큼 연속으로 고름.
// 지금 목록은 임시 카드. 스킬이 정해지면 인스펙터의 skills를 고치고, 고른 스킬은 OnSkillPicked 이벤트로 받아 구현하면 됨.
public class SkillCards : MonoBehaviour
{
    public enum Effect { None, Grow }

    [Serializable]
    public class Skill
    {
        public string name = "새 스킬";
        [TextArea(1, 3)] public string desc = "";
        public Color color = Color.white;
        [Tooltip("스킬을 고르면 실제로 적용되는 효과 (None = 아직 효과 없는 임시 스킬)")] public Effect effect = Effect.None;
        [Tooltip("Grow: 단계마다 커지는 비율 (0.2 = 현재 크기의 20%)")] public float amount = 0.2f;
    }

    public static SkillCards Instance { get; private set; }

    [Tooltip("레벨업 때 나올 수 있는 스킬 카드 목록")]
    public List<Skill> skills = new List<Skill>
    {
        new Skill { name = "거대화", desc = "몸이 현재 크기의 20%만큼 커짐.\n단계마다 누적.", color = new Color(0.8f, 0.45f, 1f), effect = Effect.Grow, amount = 0.2f },
    };
    [Tooltip("같은 스킬을 다시 골랐을 때 올릴 수 있는 최대 단계 (이 단계에 이른 스킬은 카드에서 제외)")] [Min(1)] public int maxSkillLevel = 99;

    // 고른 스킬 (이름 → 단계)
    readonly Dictionary<string, int> owned = new Dictionary<string, int>();
    public int LevelOf(string skillName) => owned.TryGetValue(skillName, out int l) ? l : 0;

    // 스킬을 골랐을 때 (스킬, 새 단계). 스킬 효과는 여기서 받아 구현
    public event Action<Skill, int> OnSkillPicked;

    LevelSystem lv;

    [Header("레벨업 방식")]
    [Tooltip("켜면 레벨업마다 스킬 카드 3장 중 고르기, 끄면 아래 보상이 자동으로 적용됨")] public bool showCards = false;
    [Tooltip("자동 보상: 레벨업마다 늘어나는 최대 HP (늘어난 만큼 바로 채워짐)")] public float maxHpStep = 50f;
    [Tooltip("자동 보상: 레벨업마다 회복하는 HP (최대 HP 대비 비율)")] [Range(0f, 1f)] public float healRatio = 0.3f;
    [Tooltip("자동 보상: 레벨업마다 커지는 비율 (현재 크기에 곱함, 0.07 = 7%씩 곱해서 커짐)")] public float growPerLevel = 0.05f;
    [Tooltip("몸 크기 최대 배율 (처음 크기 기준, 0 = 제한 없음)")] public float maxSizeMultiplier = 0f;

    // 거대화: 처음 크기 기준 배율. 고른 뒤 게임이 진행되면 부드럽게 커짐
    [Tooltip("카드 선택 후 커지는 데 걸리는 시간(초)")] public float growTime = 0.8f;
    GiantController ctrl;
    float baseModelScale = 1f, sizeMult = 1f, shownSizeMult = 1f;
    bool sizeBaseSet;

    void Awake()
    {
        Instance = this;
        OnSkillPicked += ApplyEffect;
    }

    void ApplyEffect(Skill s, int level)
    {
        if (s.effect == Effect.Grow) Grow(s.amount);
    }

    void Grow(float amount)
    {
        if (!ctrl) ctrl = FindObjectOfType<GiantController>();
        if (!ctrl) return;
        if (!sizeBaseSet) { baseModelScale = GiantShrink.Active ? GiantShrink.SavedScale : ctrl.ModelScale; sizeBaseSet = true; } // 성별 선택 후의 모델 크기 기준
        sizeMult *= 1f + amount; // 곱하기 방식: 현재 크기의 amount만큼 더 커짐
        if (maxSizeMultiplier > 0f) sizeMult = Mathf.Min(sizeMult, maxSizeMultiplier);
    }

    // 카드 없이 레벨업마다 최대 HP 증가 + HP 회복 + 몸이 커짐
    void AutoReward(int level)
    {
        var hp = GiantHealth.Instance;
        if (hp)
        {
            if (maxHpStep > 0f) hp.AddMaxHP(maxHpStep); // 레벨업마다 최대 HP가 같은 양씩 늘어남
            if (healRatio > 0f) hp.Heal(hp.maxHP * healRatio);
        }
        if (growPerLevel > 0f) Grow(growPerLevel);
    }

    void Update()
    {
        if (CardDraft.IsOpen || GiantShrink.Active || !ctrl || !sizeBaseSet || Mathf.Approximately(shownSizeMult, sizeMult)) return;
        float step = Mathf.Max(0.01f, sizeMult - 1f) / Mathf.Max(0.05f, growTime) * Time.deltaTime;
        shownSizeMult = Mathf.MoveTowards(shownSizeMult, sizeMult, Mathf.Max(step, 0.3f * Time.deltaTime));
        ctrl.SetModelScale(baseModelScale * shownSizeMult);
    }

    void Start()
    {
        lv = LevelSystem.Instance ? LevelSystem.Instance : FindObjectOfType<LevelSystem>();
        if (lv) lv.OnLevelUp += HandleLevelUp;
    }

    void OnDestroy()
    {
        if (lv) lv.OnLevelUp -= HandleLevelUp;
        if (Instance == this) Instance = null;
    }

    void HandleLevelUp(int level)
    {
        if (!showCards) { AutoReward(level); return; }
        CardDraft.Enqueue(new CardDraft.Request
        {
            title = $"LEVEL UP!  LV {level}",
            subtitle = "스킬을 고르세요",
            build = BuildCards
        });
    }

    // 후보를 섞어서 3장 (후보가 3개보다 적으면 같은 스킬이 다른 희귀도로 나올 수 있음)
    List<CardDraft.Card> BuildCards()
    {
        var pool = new List<Skill>();
        foreach (var s in skills) if (LevelOf(s.name) < maxSkillLevel) pool.Add(s);
        var result = new List<CardDraft.Card>();
        if (pool.Count == 0) return result;
        for (int i = pool.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
        for (int i = 0; i < 3; i++) result.Add(MakeCard(pool[i % pool.Count], CardDraft.RollRarity()));
        return result;
    }

    CardDraft.Card MakeCard(Skill s, int rarity)
    {
        int now = LevelOf(s.name);
        return new CardDraft.Card
        {
            badge = now == 0 ? "새 스킬" : "스킬 강화", badgeColor = new Color(1f, 0.85f, 0.3f),
            name = s.name, stat = "스킬", color = s.color, rarity = rarity,
            amount = $"Lv {now + 1}", desc = s.desc,
            current = $"Lv {now}", preview = $"Lv {now + 1}",
            footer = now > 0 ? $"현재 {now}단계" : "",
            pick = () =>
            {
                int next = LevelOf(s.name) + 1;
                owned[s.name] = next;
                OnSkillPicked?.Invoke(s, next);
            }
        };
    }
}
