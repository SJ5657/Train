using UnityEngine;

// 점수: 화면 상단 중앙에 표시. 건물 파괴/탱크 밟기/헬기·전투기 격추 시 점수 획득.
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("점수")]
    public int buildingPoints = 100;
    public int tankPoints = 500;
    public int heliPoints = 800;
    public int jetPoints = 1500;

    public int Score { get; private set; }
    float displayScore, pulse;
    string lastGain; float lastGainUntil;

    void Awake() { Instance = this; }

    public static void Add(int points, string label = null)
    {
        if (!Instance) return;
        Instance.Score += points;
        if (LevelSystem.Instance) LevelSystem.Instance.AddXp(points); // 점수 = 경험치
        Instance.pulse = 1f;
        if (!string.IsNullOrEmpty(label))
        {
            Instance.lastGain = $"+{points} {label}";
            Instance.lastGainUntil = Time.time + 1.2f;
        }
    }

    // 세이브 불러오기: 경험치는 LevelSystem이 따로 되돌림
    public void Restore(int score) { Score = Mathf.Max(0, score); displayScore = Score; }

    void Update()
    {
        // 숫자가 부드럽게 올라가는 연출
        displayScore = Mathf.MoveTowards(displayScore, Score, Mathf.Max(50f, (Score - displayScore) * 8f) * Time.deltaTime);
        pulse = Mathf.MoveTowards(pulse, 0f, Time.deltaTime * 4f);
    }

    void OnGUI()
    {
        if (GameStartMenu.InMenu || GameStartMenu.HideHud) return; // 시작 메뉴 중이거나 HUD 숨김이면 그리지 않음
        float W = Screen.width;
        var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(30 + pulse * 6), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        string text = $"SCORE  {Mathf.RoundToInt(displayScore):N0}";
        var rect = new Rect(0, 12, W, 44);
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), text, style);
        GUI.color = Color.Lerp(Color.white, new Color(1f, 0.85f, 0.3f), pulse);
        GUI.Label(rect, text, style);

        if (Time.time < lastGainUntil && !string.IsNullOrEmpty(lastGain))
        {
            var small = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            float a = Mathf.Clamp01((lastGainUntil - Time.time) / 0.4f);
            GUI.color = new Color(0, 0, 0, 0.6f * a);
            GUI.Label(new Rect(1, 57, W, 26), lastGain, small);
            GUI.color = new Color(1f, 0.9f, 0.4f, a);
            GUI.Label(new Rect(0, 56, W, 26), lastGain, small);
        }
        GUI.color = Color.white;
    }
}
