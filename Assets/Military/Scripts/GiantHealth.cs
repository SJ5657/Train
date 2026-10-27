using UnityEngine;
using UnityEngine.SceneManagement;

// 거인 체력. 군대 공격을 받으면 감소, 0이 되면 패배 (R로 재시작).
public class GiantHealth : MonoBehaviour
{
    public static GiantHealth Instance { get; private set; }
    public float maxHP = 400f;
    [Tooltip("테스트용: 켜면 HP가 0이 되어도 죽지 않음")] public bool testNoDeath = false;
    [Header("능력치")]
    [Tooltip("초당 HP 자동 회복량")] public float hpRegen = 0f;
    [Tooltip("받는 피해 배율 (1 = 100%, 낮을수록 방어력 높음)")] public float damageTakenMultiplier = 1f;
    public float HP { get; private set; }
    // 무한모드(시작 화면 버튼) 또는 테스트 옵션이면 HP가 0이 되어도 죽지 않음
    bool NoDeath => testNoDeath || GameStartMenu.InfiniteMode;
    public bool IsDead => forcedDead || (HP <= 0f && !NoDeath);
    bool forcedDead;          // 즉사 (축소된 채 밟힘 등): 무한모드·테스트 옵션과 상관없이 게임 오버
    public string DeathReason { get; private set; }

    // 즉사시키고 게임 오버 (reason은 DEFEATED 아래에 표시)
    public void Kill(string reason)
    {
        if (IsDead) return;
        forcedDead = true; HP = 0f; DeathReason = reason;
        hitFlash = 0.25f;
        if (ctrl) ctrl.enabled = false;
        if (anim) anim.SetFloat("Speed", 0f);
    }

    CharacterController cc;
    Animator anim;
    GiantController ctrl;
    float hitFlash, healGlow;

    static readonly HumanBodyBones[] AimBones =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Chest,
        HumanBodyBones.Head, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg
    };

    void Awake()
    {
        Instance = this;
        HP = maxHP;
        cc = GetComponent<CharacterController>();
        anim = GetComponentInChildren<Animator>();
        ctrl = GetComponent<GiantController>();
    }

    // 모델이 바뀌면(성별 선택) 애니메이터 다시 찾기
    public void Rebind() { anim = GetComponentInChildren<Animator>(); }

    public float Scale => ctrl ? ctrl.GameScale : (anim ? anim.transform.lossyScale.y : 1f);
    public Vector3 Velocity => ctrl ? ctrl.Velocity : Vector3.zero;
    public Vector3 Center => transform.TransformPoint(cc.center);

    // 어깨 높이(월드 Y). 헬기·전투기 비행 고도에 사용.
    // 모델 뼈 대신 충돌 캡슐 기준으로 계산해서 남자/여자 거인이 완전히 같은 값이 되게 함.
    public float ShoulderY => transform.position.y + cc.height * 0.837f;

    // 거인 키(월드 m): 충돌 캡슐 높이. 남자/여자 거인이 같은 값
    public float Height => cc ? cc.height : 0f;

    public Transform Bone(HumanBodyBones b) => anim && anim.isHuman ? anim.GetBoneTransform(b) : null;

    // 공격 목표 지점 (몸의 랜덤한 부위)
    public Vector3 GetAimPoint()
    {
        if (anim && anim.isHuman)
        {
            var t = anim.GetBoneTransform(AimBones[Random.Range(0, AimBones.Length)]);
            if (t) return t.position;
        }
        return Center;
    }

    // 점 p에서 거인 몸(캡슐) 표면까지 거리 (안쪽이면 0)
    public float DistanceToBody(Vector3 p)
    {
        Vector3 c = Center;
        float half = Mathf.Max(0, cc.height * 0.5f - cc.radius);
        Vector3 a = c + Vector3.up * half, b = c - Vector3.up * half;
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
        return Mathf.Max(0f, Vector3.Distance(p, a + ab * t) - cc.radius);
    }

    // 점 p가 거인 몸(캡슐) 안에 있는지
    public bool IsInsideBody(Vector3 p, float extra = 0f)
    {
        Vector3 c = Center;
        float half = Mathf.Max(0, cc.height * 0.5f - cc.radius);
        Vector3 a = c + Vector3.up * half, b = c - Vector3.up * half;
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
        return (p - (a + ab * t)).sqrMagnitude < (cc.radius + extra) * (cc.radius + extra);
    }

    static readonly HumanBodyBones[] TouchBones =
    {
        HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg
    };

    // 점 p(반경 r)가 거인 몸 어디든(몸통 캡슐 + 팔/머리/다리 뼈) 닿았는지
    public bool IsTouching(Vector3 p, float r)
    {
        if (IsInsideBody(p, r)) return true;
        if (!anim || !anim.isHuman) return false;
        float boneR = 0.12f * Scale + r;
        foreach (var b in TouchBones)
        {
            var t = anim.GetBoneTransform(b);
            if (t && (t.position - p).sqrMagnitude < boneR * boneR) return true;
        }
        return false;
    }

    // 최대 HP 증가 + 늘어난 만큼 즉시 회복
    public void AddMaxHP(float amount)
    {
        maxHP += amount;
        HP = Mathf.Min(maxHP, HP + amount);
    }

    // 세이브 불러오기: HP를 그대로 되돌림 (최대 HP를 먼저 되돌린 뒤 호출)
    public void SetHP(float value) => HP = Mathf.Clamp(value, 1f, maxHP);

    // HP 회복 (초록 회복 영역)
    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;
        HP = Mathf.Min(maxHP, HP + amount);
        healGlow = 0.2f;
    }

    public void TakeDamage(float dmg) => TakeDamage(dmg, true);

    // shake = false: 권총처럼 약하고 잦은 공격은 화면을 흔들지 않음
    public void TakeDamage(float dmg, bool shake)
    {
        if (IsDead) return;
        if (GiantHome.GiantSafe) return; // 거인의 집 안에서는 날아오던 공격도 피해 없음
        if (GiantShrink.Active) return;  // 축소빔으로 작아진 동안은 군대 공격을 받지 않음 (대신 사람·차에 밟히면 즉사)
        dmg *= damageTakenMultiplier;
        HP = Mathf.Max(0, HP - dmg);
        hitFlash = shake ? 0.25f : Mathf.Max(hitFlash, 0.08f);
        if (shake && GiantCamera.Instance) GiantCamera.Instance.Shake(0.12f);
        if (IsDead)
        {
            if (ctrl) ctrl.enabled = false;
            if (anim) anim.SetFloat("Speed", 0f);
        }
    }

    void Update()
    {
        hitFlash = Mathf.Max(0, hitFlash - Time.deltaTime);
        healGlow = Mathf.Max(0, healGlow - Time.deltaTime);
        if (!IsDead && hpRegen > 0f) HP = Mathf.Min(maxHP, HP + hpRegen * Time.deltaTime);
        if (IsDead && Input.GetKeyDown(KeyCode.R))
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // 거인 머리 위에 작게 표시하는 HP 바
    void DrawOverheadBar()
    {
        var cam = Camera.main;
        if (!cam || !cc || IsDead) return;
        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * (cc.height * 1.08f));
        if (sp.z <= 0f) return;
        float w = 70, h = 8;
        float x = sp.x - w * 0.5f, y = Screen.height - sp.y - h - 4f;
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.DrawTexture(new Rect(x - 1, y - 1, w + 2, h + 2), Texture2D.whiteTexture);
        bool tiny = GiantShrink.Active;
        GUI.color = (hitFlash > 0 || (tiny && GiantShrink.TinyHitFlash)) ? new Color(1f, 0.9f, 0.9f) : new Color(0.9f, 0.2f, 0.25f);
        GUI.DrawTexture(new Rect(x, y, w * Mathf.Clamp01(ShownHP / ShownMaxHP), h), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    // 축소빔으로 작아진 동안은 소인 HP를 보여줌 (원래 HP는 그대로 보존)
    float ShownHP => GiantShrink.Active ? GiantShrink.TinyHP : HP;
    float ShownMaxHP => GiantShrink.Active ? Mathf.Max(1f, GiantShrink.TinyMaxHP) : maxHP;

    void OnGUI()
    {
        if (GameStartMenu.InMenu) return; // 시작 메뉴 중에는 HUD 숨김
        DrawOverheadBar();
        float w = 260, h = 20, x = 20, y = 20;
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.DrawTexture(new Rect(x - 3, y - 3, w + 6, h + 6), Texture2D.whiteTexture);
        GUI.color = hitFlash > 0 ? new Color(1f, 0.9f, 0.9f) : healGlow > 0 ? Color.Lerp(new Color(0.9f, 0.2f, 0.25f), new Color(0.3f, 1f, 0.45f), 0.5f + 0.5f * Mathf.Sin(Time.time * 10f)) : new Color(0.9f, 0.2f, 0.25f);
        if (GiantShrink.Active && GiantShrink.TinyHitFlash) GUI.color = new Color(1f, 0.9f, 0.9f);
        GUI.DrawTexture(new Rect(x, y, w * Mathf.Clamp01(ShownHP / ShownMaxHP), h), Texture2D.whiteTexture);
        GUI.color = Color.white;
        var style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        string label = GiantShrink.Active ? "소인 HP" : "GIANT HP";
        GUI.Label(new Rect(x, y, w, h), $"{label}  {Mathf.CeilToInt(ShownHP)} / {ShownMaxHP:0}" + (GameStartMenu.InfiniteMode ? "  (무한모드)" : testNoDeath ? "  (TEST)" : ""), style);
        if (IsDead)
        {
            var big = new GUIStyle(GUI.skin.label) { fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 60), "DEFEATED", big);
            if (!string.IsNullOrEmpty(DeathReason)) GUI.Label(new Rect(0, Screen.height * 0.4f + 50, Screen.width, 30), DeathReason, style);
            GUI.Label(new Rect(0, Screen.height * 0.4f + 80, Screen.width, 30), "Press R to restart", style);
        }
    }
}
