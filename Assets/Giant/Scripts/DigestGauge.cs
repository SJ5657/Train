using UnityEngine;

// 소화 게이지: 시민 먹기(거인의 식욕)를 얻으면 나타남. 시민을 먹을 때마다 차고, 가득 차면 방귀 → 주변에 가스(GasCloud).
[RequireComponent(typeof(GiantController))]
public class DigestGauge : MonoBehaviour
{
    [Tooltip("시민 한 명을 먹을 때 차는 양 (1 = 가득)")] public float perCivilian = 0.2f;
    [Header("방귀 가스")]
    [Tooltip("가스 반경 (거인 크기 기준)")] public float gasRadius = 3f;
    [Tooltip("가스 유지 시간(초)")] public float gasDuration = 10f;
    [Tooltip("가스 안의 적이 받는 초당 피해")] public float gasDps = 12f;
    [Tooltip("가스 연기 재질 (비우면 건물 먼지 재질 사용)")] public Material gasMaterial;

    public float Value { get; private set; }
    GiantController ctrl;
    GiantEat eat;
    float shown, fullFlash, fartTextUntil;

    void Awake() { ctrl = GetComponent<GiantController>(); eat = GetComponent<GiantEat>(); }

    bool Visible => eat && eat.unlocked;

    // GiantEat이 시민을 먹을 때 호출
    public void AddCivilian()
    {
        Value = Mathf.Min(1f, Value + perCivilian);
        if (Value >= 1f) Fart();
    }

    void Fart()
    {
        Value = 0f;
        var mat = gasMaterial ? gasMaterial : (BuildingDestruction.Instance ? BuildingDestruction.Instance.dustMaterial : null);
        if (mat) GasCloud.Create(transform.position, gasRadius * ctrl.GameScale, gasDuration, gasDps, mat);
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.6f);
        fartTextUntil = Time.time + 1.8f;
        fullFlash = 1f;
    }

    void Update()
    {
        shown = Mathf.MoveTowards(shown, Value, Time.deltaTime * 2f);
        fullFlash = Mathf.Max(0f, fullFlash - Time.deltaTime * 1.5f);
    }

    void OnGUI()
    {
        if (GameStartMenu.InMenu || GameStartMenu.HideHud || !Visible) return;
        // 공격력 표시 오른쪽
        float x = 150f, y = 69f, w = 130f, h = 10f;
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, h + 4), Texture2D.whiteTexture);
        GUI.color = Color.Lerp(new Color(0.6f, 0.8f, 0.25f), Color.white, fullFlash);
        GUI.DrawTexture(new Rect(x, y, w * shown, h), Texture2D.whiteTexture);
        GUI.color = Color.white;
        var s = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(x, y - 4, w, h + 8), "소화", s);

        if (Time.time < fartTextUntil)
        {
            float a = Mathf.Clamp01((fartTextUntil - Time.time) / 0.5f);
            var big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            var r = new Rect(0, Screen.height * 0.24f, Screen.width, 50);
            GUI.color = new Color(0, 0, 0, 0.6f * a);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), "뿌웅~!", big);
            GUI.color = new Color(0.7f, 0.9f, 0.3f, a);
            GUI.Label(r, "뿌웅~!", big);
            GUI.color = Color.white;
        }
    }
}
