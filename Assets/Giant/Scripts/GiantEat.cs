using UnityEngine;

// 거인이 시민을 잡아먹기 (패시브 아이템 '거인의 식욕'을 얻어야 활성화, 달리는 중엔 안 함): 바로 앞까지 다가온 시민을 향해 허리를 숙이며 손을 뻗어 집고(Reach)
// → 몸을 일으키며 팔을 구부려 손바닥을 입으로 가져가고(Lift) → 오물오물 먹고(Eat) → 팔을 내림(Return).
// 애니메이션 파일 없이 IK로 만듦: 손(손바닥) 위치 + 팔꿈치 방향 힌트 + 몸 숙이기(발은 제자리 고정) + 고개.
// 필요: 거인 애니메이터 컨트롤러 레이어의 IK Pass 켜짐.
[RequireComponent(typeof(GiantController))]
public class GiantEat : MonoBehaviour
{
    [Header("잡기")]
    [Tooltip("시민을 잡아먹을 수 있는지 (패시브 아이템 '거인의 식욕'을 얻으면 켜짐). 꺼져 있으면 시민은 밟히기만 함")] public bool unlocked;
    [Tooltip("이 거리 안의 시민을 잡음 (거인 크기 기준)")] public float grabRange = 0.45f;
    [Tooltip("다 먹고 나서 다음 시민을 잡기까지 대기(초)")] public float cooldown = 0.5f;

    [Header("동작 시간(초)")]
    public float reachTime = 0.45f;
    public float liftTime = 0.6f;
    public float eatTime = 0.5f;
    public float returnTime = 0.45f;

    [Header("몸 숙이기 (손을 뻗을 때)")]
    [Tooltip("엉덩이를 낮추는 정도 (모델 크기 비율)")] public float crouchDepth = 0.12f;
    [Tooltip("상체를 앞으로 숙이는 각도")] public float leanAngle = 60f;

    [Header("입 위치 (눈 사이 기준, 모델 크기 비율)")]
    public float mouthBelowEyes = 0.045f;
    public float mouthForward = 0.045f;

    [Header("보상")]
    [Tooltip("한 명 먹을 때 회복하는 HP (아이템 강화로 늘어남)")] public float healPerCivilian = 20f;
    [Tooltip("한 명 먹을 때 얻는 점수(=경험치)")] public int scorePerCivilian = 20;

    enum Phase { None, Reach, Lift, Eat, Return }
    Phase phase;
    float t, nextAt, carryBlend;
    Civilian target;
    Vector3 grabbedFrom, liftStart, civScale = Vector3.one;
    Vector3 gripOffset;               // 손목 → 손바닥(가운데 손가락 뿌리) 벡터 (직전 프레임)
    AvatarIKGoal goal;
    AvatarIKHint elbowHint;
    HumanBodyBones handBone, gripBone, upperArmBone;
    float side;                       // 오른손 +1, 왼손 -1

    GiantController ctrl;
    Animator anim;

    public bool IsEating => phase != Phase.None;

    void Awake() { ctrl = GetComponent<GiantController>(); }

    void Update()
    {
        // 성별 선택 등으로 모델이 바뀌면 새 모델에 IK 중계 붙이기
        var a = GetComponentInChildren<Animator>();
        if (a != anim)
        {
            anim = a;
            if (anim && !anim.GetComponent<GiantIKRelay>()) anim.gameObject.AddComponent<GiantIKRelay>().owner = this;
        }
        if (!anim || GameStartMenu.InMenu) return;
        float dt = Time.deltaTime;

        switch (phase)
        {
            case Phase.None:
                if (unlocked && !ctrl.IsRunning && !HostageEvent.HandBusy && Time.time >= nextAt && (!GiantHealth.Instance || !GiantHealth.Instance.IsDead)) TryGrab();
                break;
            case Phase.Reach:
                if (!target) { Begin(Phase.Return); break; } // 잡기 전에 사라짐
                t += dt / reachTime;
                if (t >= 1f)
                {
                    target.Grab();
                    grabbedFrom = target.transform.position;
                    civScale = target.transform.localScale;
                    liftStart = Hand();
                    carryBlend = 0f;
                    Begin(Phase.Lift);
                }
                break;
            case Phase.Lift:
                if (!target) { Begin(Phase.Return); break; }
                t += dt / liftTime;
                carryBlend = Mathf.Min(1f, carryBlend + dt / 0.12f); // 손바닥으로 쏙 들어옴
                if (t >= 1f) Begin(Phase.Eat);
                break;
            case Phase.Eat:
                t += dt / eatTime;
                if (target) target.transform.localScale = civScale * (1f - Smooth(t / 0.6f)); // 앞부분에 쏙 들어감, 나머지는 씹기
                if (t >= 1f)
                {
                    if (target) Destroy(target.gameObject);
                    target = null;
                    if (GiantHealth.Instance) GiantHealth.Instance.Heal(healPerCivilian);
                    if (scorePerCivilian > 0) ScoreManager.Add(scorePerCivilian, "냠!");
                    var digest = GetComponent<DigestGauge>(); if (digest) digest.AddCivilian(); // 소화 게이지 (가득 차면 방귀)
                    Begin(Phase.Return);
                }
                break;
            case Phase.Return:
                t += dt / returnTime;
                if (t >= 1f) { phase = Phase.None; nextAt = Time.time + cooldown; }
                break;
        }
    }

    void TryGrab()
    {
        var mgr = CivilianManager.Instance;
        if (!mgr) return;
        float range = grabRange * ctrl.GameScale;
        Civilian best = null; float bestD = range * range;
        foreach (var c in mgr.Civilians)
        {
            if (!c || !c.CanBeGrabbed) continue;
            Vector3 d = c.transform.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; best = c; }
        }
        if (!best) return;
        // 시민이 있는 쪽 손으로 (왼손에 새장을 들고 있으면 오른손, 오른손에 국기 게양대를 들고 있으면 왼손. 둘 다면 못 먹음)
        bool right = CageSkill.HoldingCage || Vector3.Dot(best.transform.position - transform.position, transform.right) >= 0f;
        if (Flagpoles.RightHandBusy) { if (CageSkill.HoldingCage) return; right = false; }
        target = best;
        side = right ? 1f : -1f;
        goal = right ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;
        elbowHint = right ? AvatarIKHint.RightElbow : AvatarIKHint.LeftElbow;
        handBone = right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand;
        gripBone = right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal;
        upperArmBone = right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm;
        gripOffset = Vector3.zero;
        Begin(Phase.Reach);
    }

    void Begin(Phase p) { phase = p; t = 0f; }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    float ModelScale => anim ? anim.transform.lossyScale.y : 1f;

    Vector3 Bone(HumanBodyBones b, Vector3 fallback)
    {
        var tr = anim ? anim.GetBoneTransform(b) : null;
        return tr ? tr.position : fallback;
    }

    Vector3 Hand() => Bone(handBone, transform.position);
    Vector3 Grip() { Vector3 h = Hand(); return Bone(gripBone, h); }
    float CivHalfHeight => 2f * civScale.y; // 로우폴리 캐릭터 키 ≈ 4m

    Vector3 Mouth()
    {
        float s = ModelScale;
        var le = anim ? anim.GetBoneTransform(HumanBodyBones.LeftEye) : null;
        var re = anim ? anim.GetBoneTransform(HumanBodyBones.RightEye) : null;
        if (le && re)
            return (le.position + re.position) * 0.5f - Vector3.up * mouthBelowEyes * s + transform.forward * mouthForward * s;
        // 눈 뼈가 없으면 머리 뼈 기준으로 대충
        return Bone(HumanBodyBones.Head, transform.position + Vector3.up * ctrl.GameScale * 1.5f) + transform.forward * 0.07f * s - Vector3.up * 0.01f * s;
    }

    // GiantIKRelay가 매 프레임 호출 (애니메이션 적용 직후)
    public void OnIK(Animator a)
    {
        float w = 0f, crouch = 0f;
        Vector3 palm = Mouth();
        switch (phase)
        {
            case Phase.Reach:
                w = Smooth(t); crouch = Smooth(t);
                if (target) palm = target.transform.position + Vector3.up * CivHalfHeight * 1.6f; // 시민 머리 위를 집음
                break;
            case Phase.Lift:
                w = 1f; crouch = 1f - Smooth(t / 0.7f); // 들어 올리면서 몸을 일으킴
                palm = Vector3.Lerp(liftStart + gripOffset, Mouth(), Smooth(t));
                break;
            case Phase.Eat: w = 1f; break;
            case Phase.Return: w = 1f - Smooth(t); break;
        }

        float s = ModelScale;
        // 몸 숙이기: 발은 애니메이션 위치에 고정한 채 엉덩이를 낮추고 상체를 앞으로 기울임
        if (crouch > 0.001f)
        {
            PinFoot(a, AvatarIKGoal.LeftFoot);
            PinFoot(a, AvatarIKGoal.RightFoot);
            a.bodyPosition = a.bodyPosition - Vector3.up * crouchDepth * s * crouch;
            a.bodyRotation = Quaternion.AngleAxis(leanAngle * crouch, transform.right) * a.bodyRotation;
        }
        else
        {
            a.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f); a.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
            a.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f); a.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
        }

        // 손: 손목이 아니라 손바닥이 목표에 오도록 손목 목표를 보정
        a.SetIKPositionWeight(goal, w);
        a.SetIKPosition(goal, palm - gripOffset);

        // 팔꿈치는 바깥·아래쪽으로 (팔이 몸 안쪽으로 꺾이지 않게)
        Vector3 shoulder = Bone(upperArmBone, transform.position + Vector3.up * s);
        a.SetIKHintPositionWeight(elbowHint, w * 0.8f);
        a.SetIKHintPosition(elbowHint, shoulder + transform.right * side * 0.25f * s - Vector3.up * 0.25f * s - transform.forward * 0.05f * s);

        // 고개: 잡을 땐 시민을, 먹을 땐 앞을 보며 오물오물
        if (phase == Phase.Reach && target)
        {
            a.SetLookAtWeight(Smooth(t) * 0.9f, 0.3f, 1f);
            a.SetLookAtPosition(target.transform.position);
        }
        else if (phase == Phase.Eat)
        {
            float chew = Mathf.Sin(t * Mathf.PI * 6f) * 0.08f * s;
            a.SetLookAtWeight(0.7f, 0f, 1f);
            a.SetLookAtPosition(Mouth() + transform.forward * s + Vector3.up * chew);
        }
        else a.SetLookAtWeight(0f);
    }

    static void PinFoot(Animator a, AvatarIKGoal foot)
    {
        Vector3 p = a.GetIKPosition(foot); Quaternion r = a.GetIKRotation(foot);
        a.SetIKPositionWeight(foot, 1f); a.SetIKRotationWeight(foot, 1f);
        a.SetIKPosition(foot, p); a.SetIKRotation(foot, r);
    }

    // 애니메이션·IK가 끝난 손바닥 위치에 잡힌 시민을 붙임
    void LateUpdate()
    {
        if (!anim || phase == Phase.None) return;
        gripOffset = Grip() - Hand();
        if (!target || (phase != Phase.Lift && phase != Phase.Eat)) return;
        Vector3 hold = Grip() - Vector3.up * CivHalfHeight * target.transform.localScale.y / Mathf.Max(0.001f, civScale.y);
        target.transform.position = Vector3.Lerp(grabbedFrom, hold, Smooth(carryBlend));
        Vector3 face = transform.position - target.transform.position; face.y = 0f;
        if (face.sqrMagnitude > 0.01f) target.transform.rotation = Quaternion.LookRotation(face);
    }
}
