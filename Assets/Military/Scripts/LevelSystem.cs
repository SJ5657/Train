using System;
using UnityEngine;

// 레벨업 시스템: 점수를 얻을 때 같은 양의 경험치(XP)를 획득. 화면 하단에 경험치 게이지 표시.
// 레벨업 보상은 LevelUpCards가 OnLevelUp 이벤트를 받아 능력치 강화 카드로 제공.
public class LevelSystem : MonoBehaviour
{
    public static LevelSystem Instance { get; private set; }

    [Header("경험치 곡선")]
    public int baseXp = 400;           // 1 → 2레벨에 필요한 경험치
    public float growth = 1.16f;       // 레벨마다 필요 경험치 증가 배율
    public int maxLevel = 99;
    [Tooltip("경험치 획득 배율")] public float xpMultiplier = 1f;

    [Header("게이지")]
    [Range(0.2f, 1f)] public float barWidthRatio = 0.4f;
    public float barHeight = 12f;
    public float bottomMargin = 26f;

    public int Level { get; private set; } = 1;
    public int Xp { get; private set; }          // 현재 레벨에서 모은 경험치
    public int XpToNext => Mathf.RoundToInt(baseXp * Mathf.Pow(growth, Level - 1));
    public event Action<int> OnLevelUp;

    float displayFill, flash;
    float levelUpUntil;

    void Awake() { Instance = this; }

    public void AddXp(int amount)
    {
        if (Level >= maxLevel) return;
        Xp += Mathf.RoundToInt(amount * xpMultiplier);
        while (Level < maxLevel && Xp >= XpToNext)
        {
            Xp -= XpToNext;
            Level++;
            flash = 1f;
            levelUpUntil = Time.time + 2.2f;
            displayFill = 0f;
            OnLevelUp?.Invoke(Level);
        }
    }

    // 세이브 불러오기: 레벨업 이벤트 없이 레벨·경험치만 되돌림 (능력치는 LevelUpCards가 따로 되돌림)
    public void Restore(int level, int xp)
    {
        Level = Mathf.Clamp(level, 1, maxLevel);
        Xp = Mathf.Max(0, xp);
        displayFill = Level >= maxLevel ? 1f : (float)Xp / XpToNext;
    }

    void Update()
    {
        float target = Level >= maxLevel ? 1f : (float)Xp / XpToNext;
        displayFill = Mathf.MoveTowards(displayFill, target, Time.deltaTime * 1.5f);
        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 1.5f);
    }

    void OnGUI()
    {
        if (GameStartMenu.InMenu) return; // 시작 메뉴 중에는 HUD 숨김
        float W = Screen.width, H = Screen.height;
        float w = W * barWidthRatio, h = barHeight;
        float x = (W - w) * 0.5f, y = H - bottomMargin - h;

        // 배경 + 채움
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.DrawTexture(new Rect(x - 3, y - 3, w + 6, h + 6), Texture2D.whiteTexture);
        GUI.color = new Color(0.2f, 0.2f, 0.25f, 0.9f);
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = Color.Lerp(new Color(0.35f, 0.75f, 1f), Color.white, flash);
        GUI.DrawTexture(new Rect(x, y, w * displayFill, h), Texture2D.whiteTexture);

        // 레벨 표시 (게이지 왼쪽)
        var lvStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.Label(new Rect(x - 92 + 1, y - 8 + 1, 80, h + 16), $"LV {Level}", lvStyle);
        GUI.color = Color.Lerp(Color.white, new Color(0.5f, 0.85f, 1f), flash);
        GUI.Label(new Rect(x - 92, y - 8, 80, h + 16), $"LV {Level}", lvStyle);

        // 경험치 수치 (게이지 안)
        var xpStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.color = Color.white;
        string xpText = Level >= maxLevel ? "MAX" : $"{Xp:N0} / {XpToNext:N0} XP";
        GUI.Label(new Rect(x, y - 3, w, h + 6), xpText, xpStyle);

        // 레벨업 알림
        if (Time.time < levelUpUntil)
        {
            float a = Mathf.Clamp01((levelUpUntil - Time.time) / 0.5f);
            float s = 1f + flash * 0.25f;
            var big = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(34 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            var r = new Rect(0, y - 70, W, 50);
            GUI.color = new Color(0, 0, 0, 0.7f * a);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), $"LEVEL UP!  LV {Level}", big);
            GUI.color = new Color(0.55f, 0.9f, 1f, a);
            GUI.Label(r, $"LEVEL UP!  LV {Level}", big);
        }
        GUI.color = Color.white;
    }
}
