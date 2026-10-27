using UnityEngine;

// 거인의 손바닥 치기 (슈퍼맨 패링): 왼팔을 살짝 당겼다가 목표 쪽으로 쭉 뻗으며 손바닥으로 침.
// 동작 에셋이 없어서 애니메이터 IK로 만듦 (GiantIKRelay가 OnIK를 넘겨줌). 게임 시간으로 진행 (패링 슬로모션이면 같이 느려짐).
public static class GiantPalmStrike
{
    const float PullTime = 0.08f, HitTime = 0.16f, HoldTime = 0.32f, EndTime = 0.55f; // 게임 시간(초)
    static float startAt = -99f;
    static Transform target;
    static Vector3 lastTarget;

    // 이 순간 시작: 손바닥이 HitTime 뒤에 목표에 닿음
    public static void Begin(Transform t) { target = t; lastTarget = t ? t.position : Vector3.zero; startAt = Time.time; }
    public static float HitDelay => HitTime;
    public static bool Active => Time.time - startAt < EndTime;

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    public static void OnIK(Animator a)
    {
        float t = Time.time - startAt;
        if (t >= EndTime || !a) return;
        var hp = GiantHealth.Instance;
        var shoulderT = a.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var lower = a.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        var hand = a.GetBoneTransform(HumanBodyBones.LeftHand);
        if (!hp || !shoulderT || !lower || !hand) return;
        if (target) lastTarget = target.position;

        Vector3 shoulder = shoulderT.position;
        float armLen = (lower.position - shoulder).magnitude + (hand.position - lower.position).magnitude;
        Vector3 toT = lastTarget - shoulder;
        Vector3 dir = toT.sqrMagnitude > 1e-4f ? toT.normalized : hp.transform.forward;
        Vector3 right = hp.transform.right;

        // 당기기(어깨 옆 뒤) → 쭉 뻗기(목표 쪽) → 잠깐 유지 → 원래 자세로
        Vector3 pulled = shoulder + dir * armLen * 0.35f - right * armLen * 0.25f + Vector3.up * armLen * 0.1f;
        Vector3 struck = shoulder + dir * armLen * 0.98f;
        Vector3 pos; float w;
        if (t < PullTime) { w = Smooth(t / PullTime); pos = pulled; }
        else if (t < HitTime) { w = 1f; pos = Vector3.Lerp(pulled, struck, Smooth((t - PullTime) / (HitTime - PullTime))); }
        else if (t < HoldTime) { w = 1f; pos = struck; }
        else { w = 1f - Smooth((t - HoldTime) / (EndTime - HoldTime)); pos = struck; }

        // 손바닥(왼손 -Y)이 목표를 보고, 손가락(왼손 -X)이 위를 향하게
        Vector3 n = dir, f = Vector3.ProjectOnPlane(Vector3.up, n);
        if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(hp.transform.forward, n);
        f.Normalize();
        Vector3 y = -n, z = Vector3.Cross(-f, y);
        Quaternion rot = Quaternion.LookRotation(z, y);
        Vector3 ik = HostageEvent.Instance ? HostageEvent.Instance.ikRotOffsetEuler : new Vector3(0f, 90f, 0f);
        Quaternion ikOff = Quaternion.Euler(ik.x, -ik.y, -ik.z); // 오른손 기준 보정값을 왼손용으로 거울 반전

        a.SetIKPositionWeight(AvatarIKGoal.LeftHand, w);
        a.SetIKPosition(AvatarIKGoal.LeftHand, pos);
        a.SetIKRotationWeight(AvatarIKGoal.LeftHand, w);
        a.SetIKRotation(AvatarIKGoal.LeftHand, rot * ikOff);
        a.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, w * 0.6f);
        a.SetIKHintPosition(AvatarIKHint.LeftElbow, shoulder - right * armLen * 0.3f - Vector3.up * armLen * 0.3f);
    }
}
