using UnityEngine;

// 거인 조작: WASD/방향키 이동, Shift 달리기. 모델 스케일에 맞춰 속도/중력 자동 조절.
[RequireComponent(typeof(CharacterController))]
public class GiantController : MonoBehaviour
{
    [Tooltip("원래 크기(1배) 기준 걷기 속도 m/s")] public float walkSpeed = 1.4f;
    [Tooltip("원래 크기(1배) 기준 달리기 속도 m/s")] public float runSpeed = 3.8f;
    [Tooltip("노란 건물 무한 달리기 중 달리기 속도 배율 (Shift 없이 자동으로 달림)")] public float boostRunMultiplier = 1.5f;
    public float turnSpeed = 360f;
    public float gravity = 9.81f;
    public Transform cameraTransform;
    [Tooltip("이동 가능 범위 (0이면 제한 없음)")] public Vector2 areaHalfExtent;
    [Tooltip("게임 시작 시 거인 크기 배율 (씬에 놓인 모델 크기 기준, 남녀 모델 모두 적용). 속도·발 반경·카메라 등도 함께 비례")] public float startSizeMultiplier = 0.63f;

    [HideInInspector] public Vector2 externalInput;
    [HideInInspector] public bool externalRun;
    [HideInInspector] public bool inputLocked;
    // 0보다 크면 크기와 상관없이 이 속도(m/s, 달리기 기준)로 이동 (UFO 축소빔으로 작아졌을 때)
    [HideInInspector] public float speedOverride;
    // 값이 있으면 입력을 무시하고 그 지점까지 스스로 달려감 (도착하면 null)
    [HideInInspector] public Vector3? autoMoveTarget;

    public static GiantController Instance { get; private set; }
    public bool IsRunning { get; private set; }
    public Vector3 Velocity { get; private set; }
    // 이번 프레임 이동 전 위치 (빨리 달려도 지나간 길 위의 아이템을 놓치지 않게 ItemDrops.PathDistance에서 씀)
    public Vector3 PrevPosition { get; private set; }

    CharacterController cc;
    Animator anim;
    GiantStamina stamina;
    Vector3 lastPos; bool hasLastPos;
    float verticalVel, animSpeed;
    // 모델 크기 1당 CharacterController 치수 (크기를 바꿀 때 비례해서 맞춤)
    float ccHeightPer, ccRadiusPer, ccCenterYPer, ccStepPer, ccHeightBase, ccCenterBase;
    static readonly int SpeedHash = Animator.StringToHash("Speed");

    // 얼굴이 빨개지는 문제: 얼굴 피부 MToon 재질의 그림자색(_ShadeColor)이 분홍빛이라, 빛을 등지거나 그늘이 지면
    // 볼·코 주변이 붉은 띠처럼 보임 → 그림자색을 살짝 어두운 살색으로 바꿈. 앞머리 그림자도 얼굴엔 안 지게 함.
    static readonly Color FaceShadeColor = new Color(0.93f, 0.86f, 0.84f);

    void FixFaceShadows()
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r.name != "Face") continue;
            r.receiveShadows = false;
            foreach (var m in r.materials)
                if (m && m.HasProperty("_ShadeColor") && m.GetColor("_ShadeColor") != Color.white) // 눈·눈썹 등 흰색은 그대로
                    m.SetColor("_ShadeColor", FaceShadeColor);
        }
    }

    // 캐릭터 윤곽선 (MToon 외곽선, 건물 윤곽선처럼 진한 색). 월드 좌표 두께라서 거인 크기에 비례해서 굵게
    // (MToon의 화면 좌표 모드는 카메라가 멀면 가늘어져 이 게임의 먼 카메라에선 안 보임)
    [Header("윤곽선")]
    [Tooltip("윤곽선 두께 = 거인 크기(GameScale) × 이 값 (m). 0이면 윤곽선 없음")] public float outlineWidthPerScale = 0.011f;
    public Color outlineColor = new Color(0.12f, 0.1f, 0.12f, 1f);

    void ApplyOutline(float gameScale)
    {
        float w = outlineWidthPerScale * gameScale;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.materials)
            {
                if (!m || !m.HasProperty("_OutlineWidthMode") || !(m.name.Contains("_SKIN") || m.name.Contains("_HAIR"))) continue; // 피부·머리카락만 (눈·입 등은 제외)
                bool on = w > 0f;
                m.SetFloat("_OutlineWidthMode", on ? 1f : 0f); // 1 = 월드 좌표
                m.SetFloat("_OutlineWidth", w);
                m.SetColor("_OutlineColor", outlineColor);
                if (m.HasProperty("_OutlineLightingMix")) m.SetFloat("_OutlineLightingMix", 0f); // 그늘에서도 같은 색
                if (on) m.EnableKeyword("_MTOON_OUTLINE_WORLD"); else m.DisableKeyword("_MTOON_OUTLINE_WORLD");
                m.DisableKeyword("_MTOON_OUTLINE_SCREEN");
            }
    }

    // 잠깐 dir 쪽으로 돌아서게 함 (seconds = 게임 시간 초)
    Vector3 faceDir = Vector3.forward; float faceUntil;
    public void FaceTowards(Vector3 dir, float seconds)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        faceDir = dir.normalized; faceUntil = Time.time + seconds;
    }

    void Awake()
    {
        Instance = this;
        PrevPosition = transform.position;
        FixFaceShadows();
        cc = GetComponent<CharacterController>();
        anim = GetComponentInChildren<Animator>();
        stamina = GetComponent<GiantStamina>();
        if (anim) anim.applyRootMotion = false;
        float s0 = anim ? anim.transform.localScale.y : 1f;
        ccHeightPer = cc.height / s0; ccRadiusPer = cc.radius / s0; ccCenterYPer = cc.center.y / s0; ccStepPer = cc.stepOffset / s0;
        ccHeightBase = ccHeightPer; ccCenterBase = ccCenterYPer;
        // 시작 크기: 모델 크기 1당 치수를 잰 뒤에 줄여야 충돌 캡슐도 비례해서 맞춰짐 (실제 적용은 성별 선택 Rebind 때)
        if (startSizeMultiplier > 0f && !Mathf.Approximately(startSizeMultiplier, 1f))
        {
            foreach (var a in GetComponentsInChildren<Animator>(true)) a.transform.localScale *= startSizeMultiplier;
            SetModelScale(ModelScale);
        }
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
        // 거인은 건물/잔해에 막히지 않고 통과 (대신 GiantStomp가 부숨)
        int g = LayerMask.NameToLayer("Giant"), b = LayerMask.NameToLayer("Building"), d = LayerMask.NameToLayer("Debris");
        if (g >= 0 && b >= 0) Physics.IgnoreLayerCollision(g, b, true);
        if (g >= 0 && d >= 0) Physics.IgnoreLayerCollision(g, d, true);
    }

    // 거인 모델 크기 (기본 32). 발 반경·속도·카메라 거리 등은 모두 이 크기에 비례해서 자동으로 바뀜
    public float ModelScale => anim ? anim.transform.localScale.y : 1f;

    // 모델마다 원본 키가 달라서(남자 모델이 더 큼) 화면상 키를 맞추려고 모델 크기를 다르게 줌.
    // 게임 수치는 이 보정값을 곱한 GameScale 기준이라 성별에 관계없이 완전히 동일.
    [HideInInspector] public float scaleCompensation = 1f;
    public float GameScale => (anim ? anim.transform.lossyScale.y : 1f) * scaleCompensation;

    public void SetModelScale(float s)
    {
        if (!anim || s <= 0f) return;
        anim.transform.localScale = Vector3.one * s;
        float gs = s * scaleCompensation; // 충돌 캡슐도 게임 기준 크기로 (성별 무관 동일)
        cc.height = ccHeightPer * gs;
        cc.radius = ccRadiusPer * gs;
        cc.center = new Vector3(cc.center.x, ccCenterYPer * gs, cc.center.z);
        cc.stepOffset = Mathf.Min(ccStepPer * gs, cc.height * 0.5f);
        cc.skinWidth = Mathf.Clamp(cc.radius * 0.1f, 0.0005f, 0.08f); // 아주 작아졌을 때(축소빔) 캡슐보다 두꺼운 스킨 방지. 보통 크기에선 기본값 0.08 그대로
        cc.minMoveDistance = Mathf.Min(0.001f, cc.radius * 0.01f);
        ApplyOutline(gs);
    }

    // 모델이 바뀌면(성별 선택) 애니메이터 다시 찾고, 모델 키에 맞춰 충돌 캡슐 높이 조정
    public void Rebind(float heightFactor = 1f)
    {
        anim = GetComponentInChildren<Animator>();
        if (anim) anim.applyRootMotion = false;
        FixFaceShadows();
        ccHeightPer = ccHeightBase * heightFactor;
        ccCenterYPer = ccCenterBase * heightFactor;
        SetModelScale(ModelScale);
    }

    void Update()
    {
        PrevPosition = transform.position;
        float scale = GameScale;
        Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")) + externalInput;
        input = Vector2.ClampMagnitude(input, 1f);
        if (inputLocked) input = Vector2.zero; // 인질 줍기 동작 중 등: 이동 입력 무시 (제자리에서 가만히 서서 동작)
        // 기본 이동은 달리기, Shift를 누르고 있으면 걷기
        bool wantRun = externalRun || !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        bool boosted = stamina && stamina.Infinite; // 노란 건물 효과: 자동 달리기 + 더 빠르게
        if (boosted) wantRun = true;

        Vector3 fwd = Vector3.forward, right = Vector3.right;
        if (cameraTransform)
        {
            // 카메라가 거의 수직으로 내려다보면(탑뷰) forward가 바닥과 수직이라 앞뒤 방향이 사라짐 → 그땐 카메라 위쪽(up)을 앞으로 씀
            Vector3 f = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
            if (f.sqrMagnitude < 0.04f) f = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up);
            fwd = f.normalized;
            right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        }
        Vector3 moveDir = fwd * input.y + right * input.x;
        // 자동 이동 (축소빔에 맞아 인도로 피할 때 등): 입력 대신 목표 지점으로 달려감
        if (autoMoveTarget.HasValue)
        {
            Vector3 to = autoMoveTarget.Value - transform.position; to.y = 0f;
            if (to.magnitude < Mathf.Max(0.05f, cc.radius)) autoMoveTarget = null;
            else { moveDir = to.normalized; input = Vector2.up; wantRun = true; }
        }
        bool moving = moveDir.sqrMagnitude > 0.001f;
        // 스테미나: 남아 있고 지치지 않았을 때만 달리기
        IsRunning = wantRun && moving && (stamina == null || stamina.CanRun);
        if (stamina) stamina.Tick(IsRunning, moving, Time.deltaTime);
        float mult = stamina ? stamina.SpeedMultiplier : 1f;
        float speed = moving ? (IsRunning ? runSpeed * (boosted ? boostRunMultiplier : 1f) : walkSpeed * mult) * input.magnitude : 0f;

        if (Time.time < faceUntil) // 패링 등: 잠깐 정해진 방향으로 빠르게 돌아섬 (이동 방향보다 우선)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(faceDir), 1800f * Time.deltaTime);
        else if (moving)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(moveDir.normalized), turnSpeed * Time.deltaTime);

        if (cc.isGrounded && verticalVel < 0) verticalVel = -1f * scale;
        verticalVel -= gravity * scale * Time.deltaTime;

        float hSpeed = speedOverride > 0f ? (moving ? speedOverride * (IsRunning ? 1f : 0.5f) * input.magnitude : 0f) : speed * scale;
        Vector3 vel = (moving ? moveDir.normalized : Vector3.zero) * hSpeed;
        vel.y = verticalVel;
        cc.Move(vel * Time.deltaTime);
        Velocity = new Vector3(vel.x, 0, vel.z);

        if (areaHalfExtent.x > 0 && areaHalfExtent.y > 0 && !(GiantHome.Instance && GiantHome.Instance.Allows(transform.position))) // 거인의 집 마당은 도시 밖이어도 허용
        {
            var p = transform.position;
            p.x = Mathf.Clamp(p.x, -areaHalfExtent.x, areaHalfExtent.x);
            p.z = Mathf.Clamp(p.z, -areaHalfExtent.y, areaHalfExtent.y);
            if (p != transform.position) { cc.enabled = false; transform.position = p; cc.enabled = true; }
        }

        // 실제로 움직인 속도 (경계에 막히면 0). 전투기 폭격 예측 등에 쓰임
        if (Time.deltaTime > 0f && hasLastPos)
        {
            Vector3 actual = (transform.position - lastPos) / Time.deltaTime; actual.y = 0f;
            Velocity = actual;
        }
        lastPos = transform.position; hasLastPos = true;

        float target = !moving ? 0f : (IsRunning ? 1f : 0.5f);
        // 줍는 동작 중(입력 잠금)엔 바로 서 있는 자세로: 달리던 다리가 들린 채 굳어 팔처럼 보이지 않게
        animSpeed = Mathf.MoveTowards(animSpeed, target, Time.deltaTime * (inputLocked ? 14f : 3f));
        if (anim)
        {
            anim.SetFloat(SpeedHash, animSpeed);
            // 지친 상태로 걸을 때는 걷기 애니메이션도 느리게 재생 (발 미끄러짐 방지 + 지친 느낌)
            anim.speed = (moving && !IsRunning) ? mult : (IsRunning && boosted ? boostRunMultiplier : 1f);
        }
    }
}
