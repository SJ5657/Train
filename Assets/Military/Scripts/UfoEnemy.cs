using UnityEngine;

// UFO: 게임 시작부터 거인 머리 위 높은 곳을 맴돌며 따라다니다가, 주기적으로 축소빔을 쏨.
//  1) 경고: 거인 발밑에 보라색 원이 생기고 UFO에서 옅은 조준선이 내려옴 (앞 절반은 거인을 따라가고 뒤 절반은 그 자리에 고정)
//  2) 발사: 빔이 내려꽂히고, 그때 거인이 원 안에 있으면 GiantShrink로 작아짐
// UFO는 공격받지 않음 (충돌 박스 없음).
public class UfoEnemy : MonoBehaviour
{
    [Tooltip("UFO 모델 프리팹 (FlexUnit UFO)")] public GameObject ufoPrefab;

    [Header("비행")]
    [Tooltip("UFO 폭 = 거인 키 × 이 비율")] public float sizeRatio = 1.1f;
    [Tooltip("나는 높이 = 거인 키 × 이 비율")] public float altitudeRatio = 1.4f;
    [Tooltip("거인 주변을 도는 반경 = 거인 키 × 이 비율")] public float orbitRadiusRatio = 2.2f;
    [Tooltip("화면 위쪽에서 좌우로 오가는 속도")] public float orbitSpeed = 14f;
    [Tooltip("UFO 자체 회전 속도 (도/초)")] public float spinSpeed = 25f;

    [Header("축소빔")]
    [Tooltip("게임 시작 후 첫 발사까지(초)")] public float firstShotDelay = 0f;
    [Tooltip("발사 간격(초)")] public float fireInterval = 25f;
    [Tooltip("경고 시간(초): 이 동안 피하면 안 맞음")] public float warnTime = 2f;
    [Tooltip("빔이 보이는 시간(초)")] public float beamTime = 0.6f;
    [Tooltip("빔 반경 = 거인 키 × 이 비율")] public float beamRadiusRatio = 0.35f;
    public Color beamColor = new Color(0.75f, 0.35f, 1f, 1f);

    enum State { Idle, Warn, Fire }
    State state;
    float stateT, nextShotAt, angle, swing, baseWidth = 12f, normalHeight = 25f;
    Transform ufo;
    Vector3 target; float beamRadius;
    GameObject ring, beam; Material mat;
    Mesh ringMesh, beamMesh;

    void Start()
    {
        nextShotAt = Time.time + firstShotDelay;
        angle = Random.Range(0f, 360f);
        if (!ufoPrefab) return;
        ufo = Instantiate(ufoPrefab, transform).transform;
        ufo.name = "UFO";
        foreach (var c in ufo.GetComponentsInChildren<Collider>()) Destroy(c);
        var rs = ufo.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0) { Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); baseWidth = Mathf.Max(1f, Mathf.Max(b.size.x, b.size.z)); }
        var g = GiantHealth.Instance;
        if (g) { normalHeight = Mathf.Max(1f, g.Height); ufo.position = Desired(g, 0f); }

        mat = new Material(Shader.Find("Sprites/Default"));
        ring = MakeObj("UfoWarnRing"); beam = MakeObj("UfoBeam");
        beamMesh = WarningZone.Wall(Color.white, Color.white);
        beam.GetComponent<MeshFilter>().sharedMesh = beamMesh;
        ring.SetActive(false); beam.SetActive(false);
    }

    GameObject MakeObj(string n)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        return go;
    }

    // 거인 주변 궤도 위 목표 위치
    Vector3 Desired(GiantHealth g, float a)
    {
        float H = normalHeight, rad = a * Mathf.Deg2Rad;
        Vector3 gp = g.transform.position; gp.y = 0f;
        return gp + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * H * orbitRadiusRatio + Vector3.up * H * altitudeRatio;
    }

    Vector3 BeamOrigin => ufo ? ufo.position - Vector3.up * ufo.lossyScale.y * 2.5f : transform.position;

    void Update()
    {
        var g = GiantHealth.Instance;
        if (!ufo || !g || GameStartMenu.InMenu) return;
        float dt = Time.deltaTime;
        if (!GiantShrink.Active) normalHeight = Mathf.Max(1f, g.Height); // 작아진 동안엔 원래 키 기준 유지

        // 크기·비행 (거인을 따라 궤도를 돎. 경고 중엔 천천히)
        float wantScale = normalHeight * sizeRatio / baseWidth;
        ufo.localScale = Vector3.one * Mathf.MoveTowards(ufo.localScale.x, wantScale, wantScale * dt);
        // 카메라 반대편(화면 위쪽)에서만 좌우로 오가며 맴돎 → 카메라와 거인 사이를 가리지 않고 항상 화면에 보임
        swing += orbitSpeed * (state == State.Idle ? 1f : 0.3f) * dt;
        var cam = Camera.main;
        Vector3 cf = cam ? cam.transform.forward : Vector3.forward; cf.y = 0f;
        float baseAngle = cf.sqrMagnitude > 0.001f ? Mathf.Atan2(cf.z, cf.x) * Mathf.Rad2Deg : 90f;
        angle = Mathf.MoveTowardsAngle(angle, baseAngle + Mathf.Sin(swing * Mathf.Deg2Rad) * 55f, 90f * dt);
        float follow = Mathf.Max(40f, normalHeight * 2f); // 거인이 뛰어도 따라붙을 만큼 (거인 달리기 ≈ 키 × 1.5 m/s)
        Vector3 want = Desired(g, angle) + Vector3.up * Mathf.Sin(Time.time * 1.3f) * normalHeight * 0.05f;
        ufo.position = Vector3.MoveTowards(ufo.position, want, follow * dt);
        ufo.Rotate(0f, spinSpeed * dt, 0f, Space.World);

        bool canFire = !GiantShrink.Active && !g.IsDead && !EnemyFreeze.Active && !(HostageEvent.Instance && HostageEvent.Instance.Busy);
        switch (state)
        {
            case State.Idle:
                if (Time.time < nextShotAt) break;
                if (!canFire) { nextShotAt = Time.time + 2f; break; }
                state = State.Warn; stateT = 0f;
                beamRadius = normalHeight * beamRadiusRatio;
                target = g.transform.position; target.y = 0f;
                ring.SetActive(true); beam.SetActive(true);
                break;
            case State.Warn:
                stateT += dt;
                if (!canFire) { Cancel(); break; }
                if (stateT < warnTime * 0.5f) { target = g.transform.position; target.y = 0f; } // 앞 절반은 따라오고 뒤 절반은 고정 → 피할 수 있음
                float pulse = 0.5f + 0.5f * Mathf.Sin(stateT * 18f);
                DrawRing(new Color(beamColor.r, beamColor.g, beamColor.b, 0.45f + 0.45f * pulse));
                DrawBeam(beamRadius * 0.12f, new Color(beamColor.r, beamColor.g, beamColor.b, 0.25f));
                if (stateT >= warnTime)
                {
                    state = State.Fire; stateT = 0f;
                    Vector3 gp = g.transform.position; gp.y = 0f;
                    bool hit = (gp - target).magnitude <= beamRadius && GiantShrink.TryBegin();
                    nextShotAt = Time.time + fireInterval + (hit && GiantShrink.Instance ? GiantShrink.Instance.duration : 0f);
                }
                break;
            case State.Fire:
                stateT += dt;
                float fade = 1f - Mathf.Clamp01(stateT / beamTime);
                DrawRing(new Color(beamColor.r, beamColor.g, beamColor.b, 0.8f * fade));
                DrawBeam(beamRadius * Mathf.Lerp(0.6f, 1f, fade), new Color(beamColor.r, beamColor.g, beamColor.b, 0.75f * fade));
                if (stateT >= beamTime) Cancel();
                break;
        }
    }

    void Cancel()
    {
        state = State.Idle;
        if (ring) ring.SetActive(false);
        if (beam) beam.SetActive(false);
        if (Time.time >= nextShotAt) nextShotAt = Time.time + 3f;
    }

    void DrawRing(Color c)
    {
        if (ringMesh) Destroy(ringMesh);
        ringMesh = WarningZone.Annulus(0.88f, 1f, c, c);
        ring.GetComponent<MeshFilter>().sharedMesh = ringMesh;
        ring.transform.position = target + Vector3.up * 0.3f;
        ring.transform.rotation = Quaternion.identity;
        ring.transform.localScale = Vector3.one * beamRadius;
    }

    // UFO 밑에서 목표 지점까지 원기둥 빔 (Wall 메시: 반지름 1, 높이 0~1)
    void DrawBeam(float radius, Color c)
    {
        var cols = beamMesh.colors;
        for (int i = 0; i < cols.Length; i++) cols[i] = c;
        beamMesh.colors = cols;
        Vector3 top = BeamOrigin, bottom = target;
        Vector3 d = top - bottom;
        beam.transform.position = bottom;
        beam.transform.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
        beam.transform.localScale = new Vector3(radius, d.magnitude, radius);
    }
}
