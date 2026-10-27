using System.Collections.Generic;
using UnityEngine;

// 물웅덩이: 건물을 부수면(ItemDrops가 고르면) 그 자리에 반짝이는 물웅덩이가 생김.
// 거인이 밟으면 미끄러져 넘어짐 (Soccer Tackle 모션: 앞으로 미끄러지며 넘어졌다가 일어남). 넘어지는 동안 조작 불가.
public class WaterPuddles : MonoBehaviour
{
    public static WaterPuddles Instance { get; private set; }
    public static bool Slipping => Instance && Instance.slipUntil > Time.time;

    [Tooltip("건물을 부술 때 후보로 나올지 (0 = 안 나옴, 0보다 크면 ItemDrops가 고를 수 있음)")] [Range(0f, 1f)] public float chance = 0.1f;
    public int maxPuddles = 3;
    [Tooltip("웅덩이 반경 = 거인 키 × 이 비율")] public float radiusRatio = 0.3f;
    [Tooltip("이 시간(초) 뒤 마름")] public float lifetime = 45f;
    [Tooltip("생기고 이 시간(초)이 지나야 미끄러짐")] public float armDelay = 0.5f;
    [Tooltip("넘어지는 모션 길이(초, 모션 원래 속도 기준)")] public float slipTime = 2.7f;
    [Tooltip("넘어지는 모션 재생 속도 배율 (클수록 빨리 넘어지고 빨리 일어남. 미끄러지는 거리는 그대로)")] public float slipSpeed = 1.5f;
    [Tooltip("미끄러지는 거리 배율 (1 = 모션만큼, 클수록 앞으로 멀리 미끄러짐)")] public float slideScale = 4.2f;
    [Tooltip("넘어지기 시작해 몸이 땅에 닿는 시점(초)")] public float impactTime = 1.0f;
    [Tooltip("땅에 닿을 때 충격 반경 = 거인 키 × 이 비율 (안의 건물·지상 적·사람이 부서지거나 죽음)")] public float impactRadiusRatio = 1.2f;
    public Color waterColor = new Color(0.1f, 0.3f, 0.78f, 0.82f);

    class Puddle { public GameObject go; public float born, radius; }
    readonly List<Puddle> puddles = new List<Puddle>();
    float slipUntil, slipStart; bool impacted;
    static Material mat;
    static readonly int SlipHash = Animator.StringToHash("Slip"), SlipSpeedHash = Animator.StringToHash("SlipSpeed");

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    public static void OnBuildingBroken(Vector3 groundPos) { if (Instance) Instance.TrySpawn(groundPos); }

    void TrySpawn(Vector3 pos)
    {
        if (ItemDrops.Skip(chance) || puddles.Count >= maxPuddles) return;
        var hp = GiantHealth.Instance;
        if (!hp || GiantShrink.Active) return;
        float r = Mathf.Max(2f, hp.Height * radiusRatio);
        if (!mat) mat = new Material(Shader.Find("Sprites/Default"));
        var go = new GameObject("WaterPuddle");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(pos.x, GroundY(pos) + 0.04f, pos.z);
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        go.transform.localScale = new Vector3(1f, 1f, Random.Range(0.7f, 1f)); // 살짝 타원
        go.AddComponent<MeshFilter>().sharedMesh = PuddleMesh(r);
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        puddles.Add(new Puddle { go = go, born = Time.time, radius = r });
        ItemDrops.Spawned++;
    }

    // 가장자리가 울퉁불퉁한 물웅덩이 (가운데 밝은 반사 + 가장자리 진한 물색)
    Mesh PuddleMesh(float r)
    {
        int seg = 40;
        var v = new Vector3[seg + 1]; var c = new Color[seg + 1]; var t = new int[seg * 3];
        v[0] = Vector3.zero; c[0] = Color.Lerp(waterColor, Color.white, 0.25f);
        float seed = Random.value * 10f;
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            float rr = r * (0.8f + 0.2f * Mathf.PerlinNoise(seed + Mathf.Cos(a) * 1.5f, seed + Mathf.Sin(a) * 1.5f) * 1.6f);
            v[i + 1] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rr;
            c[i + 1] = waterColor;
            t[i * 3] = 0; t[i * 3 + 1] = (i + 1) % seg + 1; t[i * 3 + 2] = i + 1;
        }
        var m = new Mesh { vertices = v, colors = c, triangles = t };
        m.RecalculateBounds();
        return m;
    }

    void Update()
    {
        var hp = GiantHealth.Instance;
        var ctrl = hp ? hp.GetComponent<GiantController>() : null;
        // 넘어지는 중: 모션에 맞춰 앞으로 미끄러짐
        if (ctrl && slipUntil > 0f)
        {
            float t = (Time.time - slipStart) * slipSpeed; // 모션 원래 속도 기준 시간
            if (t >= slipTime) { slipUntil = 0f; ctrl.inputLocked = false; }
            else
            {
                if (!impacted && t >= impactTime) { impacted = true; Impact(hp); }
                // 모션의 몸 이동(모델 크기 기준): 0~0.6초 빠르게, 0.6~1.2초 조금, 그 뒤 거의 멈춤
                float v = t < 0.6f ? 3.15f : t < 1.2f ? 2.4f : 0.2f;
                var cc = hp.GetComponent<CharacterController>();
                if (cc)
                {
                    BreakSolidAhead(hp, cc);
                    cc.Move(hp.transform.forward * v * slipSpeed * ctrl.GameScale * slideScale * Time.deltaTime);
                }
            }
        }

        for (int i = puddles.Count - 1; i >= 0; i--)
        {
            var p = puddles[i];
            if (!p.go) { puddles.RemoveAt(i); continue; }
            float age = Time.time - p.born;
            if (age > lifetime) { Destroy(p.go); puddles.RemoveAt(i); continue; }
            if (!hp || hp.IsDead || !ctrl || age < armDelay || Slipping || GiantShrink.Active || HostageEvent.HandBusy) continue;
            // 달리며 지나가도 놓치지 않게 지나간 길로 재고, 발이 웅덩이 가장자리만 밟아도 미끄러지게 거인 몸 반경도 더함
            var gcc = hp.GetComponent<CharacterController>();
            if (ItemDrops.PathDistance(p.go.transform.position) < p.radius * 0.85f + (gcc ? gcc.radius * 0.5f : 0f))
            {
                StartSlip(hp, ctrl);
                Destroy(p.go, slipTime / slipSpeed); puddles.RemoveAt(i); // 넘어지는 동작이 끝나면 웅덩이도 사라짐
            }
        }
    }

    void StartSlip(GiantHealth hp, GiantController ctrl)
    {
        slipStart = Time.time; slipUntil = Time.time + slipTime / slipSpeed; impacted = false;
        ctrl.inputLocked = true; ctrl.autoMoveTarget = null;
        var anim = hp.GetComponentInChildren<Animator>();
        if (anim) { anim.SetFloat(SlipSpeedHash, slipSpeed); anim.SetTrigger(SlipHash); }
        CombatFX.Muzzle(hp.transform.position + Vector3.up * 0.5f, Vector3.up, hp.Height * 0.25f);
        SupplyCrates.Notice("물웅덩이에 미끄러졌다!");
    }

    static float GroundY(Vector3 p) => ItemDrops.GroundY(p);

    // 미끄러지는 길을 막는 초고층(원래 부술 수 없는 단단한 빌딩)은 부딪히는 순간 부숨
    readonly Collider[] aheadHits = new Collider[64];
    void BreakSolidAhead(GiantHealth hp, CharacterController cc)
    {
        var tall = TallBuildings.Instance;
        if (!tall) return;
        Vector3 fwd = hp.transform.forward; fwd.y = 0f; fwd.Normalize();
        float r = cc.radius * 1.3f;
        Vector3 c = hp.transform.position + fwd * (cc.radius + r * 0.6f) + Vector3.up * Mathf.Max(r, cc.center.y * 0.6f);
        int n = Physics.OverlapSphereNonAlloc(c, r, aheadHits, LayerMask.GetMask(tall.solidLayerName), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Transform b = tall.SolidRoot(aheadHits[i]);
            if (!b) continue;
            tall.ForceBreak(b, aheadHits[i].ClosestPoint(c), fwd);
            if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.5f);
        }
    }

    // 몸이 땅에 쿵 닿는 충격: 주변의 부술 수 있는 건물은 무너지고, 지상 적(탱크·경찰차)·자동차는 찌그러지고, 사람은 죽음
    readonly Collider[] hits = new Collider[256];
    void Impact(GiantHealth hp)
    {
        float r = Mathf.Max(5f, hp.Height * impactRadiusRatio);
        Vector3 c = hp.transform.position + hp.transform.forward * hp.Height * 0.4f; // 몸이 떨어지는 앞쪽이 중심
        c.y = 0f;
        bool In(Vector3 p) { p.y = 0f; return (p - c).sqrMagnitude < r * r; }

        // 건물 (Building 레이어 = 부술 수 있는 건물만. 단단한 초고층은 Default 레이어라 제외됨)
        int n = Physics.OverlapSphereNonAlloc(c, r, hits, LayerMask.GetMask("Building"), QueryTriggerInteraction.Ignore);
        var done = new HashSet<GameObject>();
        for (int i = 0; i < n; i++)
        {
            if (!hits[i]) continue;
            Transform t = hits[i].transform;
            while (t.parent && t.parent.gameObject.layer == hits[i].gameObject.layer) t = t.parent; // 건물 본체
            if (!t.gameObject.activeInHierarchy || !done.Add(t.gameObject)) continue;
            Vector3 push = t.position - c; push.y = 0f;
            BuildingDestruction.Break(t.gameObject, t.position, push.sqrMagnitude > 0.01f ? push.normalized : hp.transform.forward);
        }
        // 지상 적·자동차
        foreach (var tank in FindObjectsOfType<TankAI>()) if (!tank.IsDead && In(tank.transform.position)) tank.Crush();
        foreach (var pol in FindObjectsOfType<PoliceUnit>())
        {
            if (pol.IsDead) continue;
            pol.TakeGas(q => In(q), 99999f); // 내려 있던 경찰관
            if (In(pol.transform.position)) pol.Crush();
        }
        foreach (var car in TrafficCar.All.ToArray()) if (car && !car.IsDead && In(car.transform.position)) car.Crush();
        // 사람
        var mgr = CivilianManager.Instance;
        if (mgr) foreach (var civ in mgr.Civilians) if (civ && In(civ.transform.position)) civ.Collapse();
        foreach (var v in RubbleVictim.All.ToArray()) if (v && !v.Grabbed && In(v.transform.position)) Destroy(v.gameObject);

        // 흙먼지·충격 연출
        CombatFX.Explosion(c + Vector3.up * 2f, r * 0.35f);
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.8f);
    }
}
