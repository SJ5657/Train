using UnityEngine;

// 거인의 발/정강이 주변에 닿는 건물을 매 프레임 검사해서 공격력만큼 피해를 준다 (지나갈 때마다 1번).
public class GiantStomp : MonoBehaviour
{
    [Tooltip("모델 스케일 기준 발 반경")] public float footRadius = 0.08f;
    [Tooltip("모델 스케일 기준 다리 반경")] public float legRadius = 0.06f;
    [Tooltip("이동 중 몸 앞/아래 쪽을 쓸고 지나가는 반경 (모델 스케일 기준). 0이면 발/다리만")] public float bodyRadius = 0.12f;
    [Tooltip("몸 쓸기 높이 (모델 스케일 기준)")] public float bodyHeight = 0.35f;
    [Header("공격력")]
    [Tooltip("건물을 지나갈 때마다 주는 피해")] public float attackPower = 100f;
    public LayerMask buildingMask;
    public LayerMask militaryMask;

    Animator anim;
    GiantController controller;
    Transform lFoot, rFoot, lToe, rToe, lKnee, rKnee;
    readonly Collider[] hits = new Collider[32];

    void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        controller = GetComponent<GiantController>();
        if (buildingMask == 0) buildingMask = LayerMask.GetMask("Building");
        if (militaryMask == 0) militaryMask = LayerMask.GetMask("Military");
        Rebind();
    }

    // 모델이 바뀌면(성별 선택) 애니메이터와 뼈 다시 찾기
    GiantController gc;

    public void Rebind()
    {
        anim = GetComponentInChildren<Animator>();
        if (!anim) return;
        lFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
        rFoot = anim.GetBoneTransform(HumanBodyBones.RightFoot);
        lToe = anim.GetBoneTransform(HumanBodyBones.LeftToes);
        rToe = anim.GetBoneTransform(HumanBodyBones.RightToes);
        lKnee = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        rKnee = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
    }

    void LateUpdate() // 애니메이션 적용 후 뼈 위치 기준
    {
        if (GameStartMenu.InMenu || !anim || GiantShrink.Active) return; // 축소빔으로 작아진 동안은 아무것도 부수거나 밟지 못함
        if (!gc) gc = GetComponent<GiantController>();
        float s = gc ? gc.GameScale : anim.transform.lossyScale.y;
        Vector3 push = controller ? controller.Velocity : transform.forward;
        Check(lFoot, footRadius * s, push); Check(rFoot, footRadius * s, push);
        Check(lToe, footRadius * s, push); Check(rToe, footRadius * s, push);
        CheckLeg(lKnee, lFoot, legRadius * s, push); CheckLeg(rKnee, rFoot, legRadius * s, push);

        // 이동 중이면 하체 전체 영역을 쓸어서 지나간 길의 건물을 모두 부숨
        if (bodyRadius > 0 && push.sqrMagnitude > 1f)
        {
            float r = bodyRadius * s;
            Vector3 a = transform.position + Vector3.up * r;
            Vector3 b = transform.position + Vector3.up * Mathf.Max(r, bodyHeight * s);
            int n = Physics.OverlapCapsuleNonAlloc(a, b, r, hits, buildingMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (hits[i]) BuildingDestruction.Hit(hits[i].gameObject, transform.position, push, attackPower);
        }
    }

    void Check(Transform t, float r, Vector3 push)
    {
        if (!t) return;
        Hit(t.position, r, push);
    }

    void CheckLeg(Transform knee, Transform foot, float r, Vector3 push)
    {
        if (!knee || !foot) return;
        for (int i = 0; i <= 2; i++) Hit(Vector3.Lerp(knee.position, foot.position, i / 2f), r, push);
    }

    void Hit(Vector3 p, float r, Vector3 push)
    {
        int n = Physics.OverlapSphereNonAlloc(p, r, hits, buildingMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (hits[i]) BuildingDestruction.Hit(hits[i].gameObject, p, push, attackPower);

        // 탱크 밟기
        n = Physics.OverlapSphereNonAlloc(p, r, hits, militaryMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var tank = hits[i] ? hits[i].GetComponentInParent<TankAI>() : null;
            if (tank) tank.Crush();
            var police = hits[i] ? hits[i].GetComponentInParent<PoliceUnit>() : null;
            if (police) police.Crush();
            var trafficCar = hits[i] ? hits[i].GetComponentInParent<TrafficCar>() : null;
            if (trafficCar) trafficCar.Crush();
        }
    }

    void OnGUI()
    {
        if (GameStartMenu.InMenu || GameStartMenu.HideHud) return; // 시작 메뉴 중이거나 HUD 숨김이면 그리지 않음
        var style = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        var r = new Rect(20, 64, 260, 22);
        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), $"공격력  {attackPower:0}", style);
        GUI.color = new Color(1f, 0.6f, 0.35f);
        GUI.Label(r, $"공격력  {attackPower:0}", style);
        GUI.color = Color.white;
    }
}
