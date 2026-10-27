using UnityEngine;

// 돈(재화): 거인이 건물을 부술 때마다 들어옴(BuildingDestruction). 거인의 집 상점(GiantHome)에서 패시브 아이템 구매에 사용.
// 화면 오른쪽 위에 표시.
public class Money : MonoBehaviour
{
    public static Money Instance { get; private set; }

    [Tooltip("건물 한 채를 부술 때 얻는 돈")] public int perBuilding = 10;
    [Tooltip("시작할 때 가진 돈")] public int startMoney = 0;

    public int Amount { get; private set; }
    float pulse, shown;

    void Awake() { Instance = this; Amount = startMoney; shown = Amount; }

    public static void Add(int amount)
    {
        if (!Instance || amount <= 0) return;
        Instance.Amount += amount;
        Instance.pulse = 1f;
    }

    public static bool TrySpend(int amount)
    {
        if (!Instance || Instance.Amount < amount) return false;
        Instance.Amount -= amount;
        return true;
    }

    public void Restore(int amount) { Amount = Mathf.Max(0, amount); shown = Amount; } // 세이브 불러오기

    public static void AddForBuilding() { if (Instance) Add(Instance.perBuilding); }

    void Update()
    {
        shown = Mathf.MoveTowards(shown, Amount, Mathf.Max(20f, Mathf.Abs(Amount - shown) * 4f) * Time.unscaledDeltaTime);
        pulse = Mathf.Max(0f, pulse - Time.unscaledDeltaTime * 3f);
    }

    void OnGUI()
    {
        if (GameStartMenu.InMenu) return; // 시작 메뉴 중에는 HUD 숨김 (돈은 HideHud와 상관없이 항상 표시)
        var s = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(22 + pulse * 4), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
        var r = new Rect(Screen.width - 260, 16, 240, 34);
        string text = $"돈  {Mathf.RoundToInt(shown):N0}";
        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, s);
        GUI.color = Color.Lerp(new Color(1f, 0.85f, 0.3f), Color.white, pulse);
        GUI.Label(r, text, s);
        GUI.color = Color.white;
    }
}
