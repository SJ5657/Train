using UnityEngine;

// 헬리콥터: 거인 주위를 일정 반경으로 선회하며 로켓 공격.
// 목표 지점이 항상 거인 기준 반경 위에 있으므로 거인이 다가오면 자연스럽게 물러나 거리를 유지.
public class HeliAI : MonoBehaviour
{
    public Transform[] mainRotors;
    public Transform[] tailRotors;
    public float rotorSpeed = 1400f;

    [Header("선회")]
    public float orbitRadius = 140f;
    public float altitude = 55f;
    public float bobAmount = 5f;
    [Tooltip("거인 어깨 높이보다 높이 날지 않음")] public bool capAtShoulder = true;
    public float minAltitude = 25f;
    public float angularSpeed = 12f;     // deg/s
    public float direction = 1f;         // 1 = 반시계, -1 = 시계
    public float startAngle = 0f;
    public float smoothTime = 2f;
    public float maxSpeed = 70f;         // 최대 이동 속도 (m/s)
    [Tooltip("이동 속도 배율 (선회 각속도·최대 이동 속도에 곱함)")] public float moveSpeedMultiplier = 1f;
    [Tooltip("거인에게 이보다 가까워지면 바깥으로 급히 벗어남")] public float panicRadius = 90f;

    [Header("사격")]
    public float fireInterval = 3.2f;
    public float rocketSpeed = 80f;
    public float damage = 40f;
    public float homing = 0.55f;

    [Header("충돌")]
    [Tooltip("거인 몸에 이 반경 안으로 닿으면: 처음엔 비틀거리다 균형을 되찾고, 두 번째에는 추락")] public float contactRadius = 10f;
    [Tooltip("비행 높이가 거인 키의 이 비율 이하인 낮은 헬기는 처음 닿아도 비틀거리지 않고 바로 추락 (0.6 = 거인 키의 60%)")] [Range(0f, 1f)] public float lowFlightRatio = 0.6f;
    [Tooltip("처음 닿았을 때 비틀거리는 시간(초)")] public float staggerTime = 2.5f;
    [Tooltip("처음 닿았을 때 거인 반대쪽으로 튕겨나가는 속도(m/s)")] public float staggerPush = 45f;

    public bool IsDead { get; private set; }
    [Tooltip("체력 (방귀 가스 등 지속 피해용)")] public float maxHP = 40f;
    float hp = -1f;

    // 지속 피해 (방귀 가스). 체력이 다 떨어지면 추락
    public void TakeDamage(float dmg)
    {
        if (IsDead) return;
        if (hp < 0f) hp = maxHP;
        hp -= dmg;
        if (hp <= 0f) Explode();
    }
    float angle, nextFire;
    Vector3 vel, fallSpin;

    // 해산(거인이 집에 들어감): 거인 반대쪽으로 날아가다가 화면 밖에서 사라짐
    bool dispersing; float disperseAt;
    public void Disperse() { if (!IsDead && !dispersing) { dispersing = true; disperseAt = Time.time; } }

    void Start()
    {
        angle = startAngle;
        nextFire = Time.time + Random.Range(1.5f, fireInterval);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        foreach (var r in mainRotors) if (r) r.Rotate(transform.up, rotorSpeed * dt, Space.World);
        foreach (var r in tailRotors) if (r) r.Rotate(transform.right, rotorSpeed * 1.5f * dt, Space.World);

        if (IsDead) { Fall(dt); return; }

        var g = GiantHealth.Instance;
        if (!g) return;
        Vector3 c = g.transform.position;

        // 거인 몸에 처음 닿으면 비틀거리다 균형을 되찾고, 한 번 더 닿으면 추락 (정지 중이어도)
        if (staggerLeft > 0f) { Stagger(dt); return; }
        if (g.IsTouching(transform.position, contactRadius))
        {
            // 낮게 나는 헬기(비행 높이가 거인 키의 lowFlightRatio 이하)는 처음 닿아도 비틀거리지 않고 한 번에 추락
            bool low = transform.position.y - g.transform.position.y <= g.Height * lowFlightRatio;
            if (!hasStaggered && !low) { StartStagger(c); return; }
            Explode(); return;
        }
        if (EnemyFreeze.Active) return; // 보급상자 효과: 적 정지 (프로펠러만 계속 돎)

        if (dispersing)
        {
            if (MilitarySpawner.OutOfSight(transform.position) || Time.time - disperseAt > 30f) { Destroy(gameObject); return; }
            Vector3 away = transform.position - c; away.y = 0;
            away = away.sqrMagnitude > 1f ? away.normalized : transform.forward;
            vel = Vector3.MoveTowards(vel, (away + Vector3.up * 0.15f) * maxSpeed * moveSpeedMultiplier, maxSpeed * moveSpeedMultiplier * dt);
            transform.position += vel * dt;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(away) * Quaternion.Euler(12f, 0, 0), dt * 2f);
            return;
        }

        angle += angularSpeed * moveSpeedMultiplier * direction * dt;
        float rad = angle * Mathf.Deg2Rad;
        Vector3 desired = c + new Vector3(Mathf.Cos(rad), 0, Mathf.Sin(rad)) * orbitRadius
                          + Vector3.up * (altitude + Mathf.Sin(Time.time * 0.6f + startAngle) * bobAmount);

        // 고도: 거인 어깨 높이를 넘지 않게
        if (capAtShoulder) desired.y = Mathf.Min(desired.y, g.ShoulderY);
        desired.y = Mathf.Max(desired.y, minAltitude);

        // 너무 가까우면 거인 반대 방향으로 밀어냄
        Vector3 flat = transform.position - c; flat.y = 0;
        if (flat.magnitude < panicRadius)
            desired += flat.normalized * (panicRadius - flat.magnitude) * 2f;

        Vector3 prev = transform.position;
        transform.position = Vector3.SmoothDamp(transform.position, desired, ref vel, smoothTime, maxSpeed * moveSpeedMultiplier * ChaseBoost.Factor(transform.position));

        // 기수는 거인을 향하고, 이동 방향에 따라 기울기
        Vector3 look = c + Vector3.up * altitude * 0.7f - transform.position; look.y = 0;
        if (look.sqrMagnitude > 1f)
        {
            Quaternion yaw = Quaternion.LookRotation(look);
            Vector3 localVel = Quaternion.Inverse(yaw) * vel;
            float roll = Mathf.Clamp(-localVel.x * 0.25f, -28f, 28f);
            float pitch = Mathf.Clamp(localVel.z * 0.15f, -15f, 15f);
            transform.rotation = Quaternion.Slerp(transform.rotation, yaw * Quaternion.Euler(pitch, 0, roll), dt * 3f);
        }

        if (!g.IsDead && !GiantHome.GiantSafe && !HostageEvent.HoldingHuman && !GasCloud.GiantHidden && Time.time >= nextFire) Fire(g); // 거인의 집 안이거나 사람을 쥐고 있으면 공격 안 함
    }

    // ───────────── 비틀거림 ─────────────
    bool hasStaggered;          // 이미 한 번 비틀거렸는지 (이후 닿으면 추락)
    float staggerLeft;
    Vector3 staggerVel;

    // 거인에게 처음 닿음: 거인 반대쪽으로 튕겨나가며 기체가 크게 흔들림. 비틀거리는 동안 닿는 건 무시, 끝나면 균형을 되찾고 원래 비행으로 돌아감
    void StartStagger(Vector3 giantPos)
    {
        hasStaggered = true;
        staggerLeft = staggerTime;
        Vector3 away = transform.position - giantPos; away.y = 0f;
        away = away.sqrMagnitude > 1f ? away.normalized : transform.forward;
        staggerVel = (away + Vector3.down * 0.25f).normalized * staggerPush;
        CombatFX.Explosion(transform.position, 7f); // 부딪힌 불꽃
    }

    void Stagger(float dt)
    {
        staggerLeft -= dt;
        float k = Mathf.Clamp01(staggerLeft / Mathf.Max(0.1f, staggerTime)); // 1 → 0
        staggerVel = Vector3.Lerp(staggerVel, Vector3.zero, dt * 2.2f);
        transform.position += staggerVel * dt;
        if (transform.position.y < minAltitude * 0.5f) transform.position = new Vector3(transform.position.x, minAltitude * 0.5f, transform.position.z); // 땅에는 닿지 않음
        // 좌우·앞뒤로 크게 흔들리며 살짝 돌다가 점점 잦아듦
        float roll = Mathf.Sin(Time.time * 9f) * 38f * k, pitch = Mathf.Sin(Time.time * 7f + 1f) * 22f * k;
        transform.rotation = Quaternion.Euler(pitch, transform.eulerAngles.y + 160f * k * dt, roll);
        if (staggerLeft <= 0f) { vel = staggerVel; nextFire = Time.time + 1f; } // 정상 비행으로 부드럽게 이어서 복귀
    }

    public void Explode()
    {
        if (IsDead) return;
        IsDead = true;
        ScoreManager.Add(ScoreManager.Instance ? ScoreManager.Instance.heliPoints : 800, "HELICOPTER");
        CageSkill.Imprison(CageSkill.Kind.Soldier, CageSkill.HeliCrew); // 승무원은 새장으로
        CombatFX.Explosion(transform.position, 24f);
        CombatFX.Explosion(transform.position + Random.insideUnitSphere * 6f, 14f);
        foreach (var r in GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials) if (m.HasProperty("_Color")) m.color *= 0.25f;
        vel = vel * 0.3f + Random.insideUnitSphere * 15f;
        fallSpin = new Vector3(Random.Range(-60f, 60f), Random.Range(200f, 400f), Random.Range(-60f, 60f));
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.5f);
    }

    // 폭발 후 추락 → 땅에 닿으면 한 번 더 폭발하고 사라짐
    void Fall(float dt)
    {
        vel += Physics.gravity * dt;
        transform.position += vel * dt;
        transform.Rotate(fallSpin * dt, Space.World);
        if (Random.value < 0.3f) CombatFX.Explosion(transform.position, 5f);
        if (transform.position.y <= 1f)
        {
            CombatFX.Explosion(new Vector3(transform.position.x, 2f, transform.position.z), 20f);
            Destroy(gameObject);
        }
    }

    void Fire(GiantHealth g)
    {
        nextFire = Time.time + fireInterval * Random.Range(0.8f, 1.2f);
        float side = Random.value < 0.5f ? -1f : 1f;
        Vector3 origin = transform.position + transform.right * side * 4f - transform.up * 2f + transform.forward * 3f;
        Vector3 aim = g.GetAimPoint();
        // 방귀 가스(연막) 안에서는 유도도 못 하고 아무 데나 쏨
        bool smoke = GasCloud.InSmoke(transform.position);
        if (smoke) aim += Random.insideUnitSphere * g.Scale * 1.5f;
        Vector3 dir = (aim - origin).normalized;
        CombatFX.Muzzle(origin, dir, 2f);
        CombatFX.Spawn(CombatFX.Kind.Rocket, origin, dir, rocketSpeed, damage, smoke ? 0f : homing);
    }
}
