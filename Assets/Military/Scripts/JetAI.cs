using UnityEngine;

// 전투기: 멀리서 거인을 향해 날아와 머리 위를 지나가고, 지나친 뒤 직진하다 선회해서 다시 접근. 공격은 하지 않음. 거인 몸에 닿으면 폭발.
public class JetAI : MonoBehaviour
{
    [Header("비행")]
    public float speed = 230f;
    public float turnRate = 55f;
    [Tooltip("거인 머리 위로 얼마나 높이 지나갈지 (m)")] public float passHeightAboveHead = 25f;
    public float egressTime = 6f;
    [Tooltip("거인에게 이 거리(m) 안으로 들어와 지나치면 직진하다 다시 접근")] public float passDistance = 350f;

    [Header("충돌")]
    public float contactRadius = 9f;

    public bool IsDead { get; private set; }
    [Tooltip("체력 (새장 포로의 총격용. 거인 몸에 닿으면 체력과 관계없이 폭발)")] public float maxHP = 30f;
    float hp = -1f;

    public void TakeDamage(float dmg)
    {
        if (IsDead) return;
        if (hp < 0f) hp = maxHP;
        hp -= dmg;
        if (hp <= 0f) Explode();
    }

    enum State { Attack, Egress }
    State state = State.Attack;
    float stateUntil, bank;
    Vector3 vel, fallSpin;

    void Start() { vel = transform.forward * speed; }

    // 해산(거인이 집에 들어감): 그대로 지나가 화면 밖에서 사라짐
    bool dispersing; float disperseAt;
    public void Disperse()
    {
        if (IsDead || dispersing) return;
        dispersing = true; disperseAt = Time.time;
        state = State.Egress; stateUntil = float.MaxValue;
    }

    float PassAltitude(GiantHealth g) => g.ShoulderY + g.Scale * 0.35f + passHeightAboveHead;

    void Update()
    {
        float dt = Time.deltaTime;
        if (IsDead) { Fall(dt); return; }
        var g = GiantHealth.Instance;
        if (!g) { if (!EnemyFreeze.Active) transform.position += vel * dt; return; }
        if (g.IsTouching(transform.position, contactRadius)) { Explode(); return; } // 정지 중이어도 거인이 닿으면 부서짐
        if (EnemyFreeze.Active) return; // 보급상자 효과: 적 정지
        if (dispersing && (MilitarySpawner.OutOfSight(transform.position) || Time.time - disperseAt > 30f)) { Destroy(gameObject); return; }

        Vector3 gp = g.transform.position;
        Vector3 flatFwd = new Vector3(vel.x, 0, vel.z).normalized;
        Vector3 steerTarget;

        if (state == State.Attack)
        {
            Vector3 to = gp - transform.position; to.y = 0;
            steerTarget = gp; steerTarget.y = PassAltitude(g);
            // 가까이 왔다가 지나쳤으면 직진하다 다시 접근
            if (to.magnitude < passDistance && Vector3.Dot(to, flatFwd) < 0) { state = State.Egress; stateUntil = Time.time + egressTime; }
        }
        else
        {
            steerTarget = transform.position + flatFwd * 500f; steerTarget.y = PassAltitude(g) + 20f;
            if (Time.time >= stateUntil) state = State.Attack;
        }

        Vector3 desiredDir = (steerTarget - transform.position).normalized;
        Vector3 newDir = Vector3.RotateTowards(vel.normalized, desiredDir, turnRate * Mathf.Deg2Rad * dt, 0f);
        float yawDelta = Vector3.SignedAngle(new Vector3(vel.x, 0, vel.z), new Vector3(newDir.x, 0, newDir.z), Vector3.up);
        vel = newDir * speed;
        transform.position += vel * dt;
        if (transform.position.y < 30f) transform.position = new Vector3(transform.position.x, 30f, transform.position.z);

        bank = Mathf.Lerp(bank, Mathf.Clamp(-yawDelta / Mathf.Max(dt, 0.0001f) * 1.2f, -70f, 70f), dt * 3f);
        transform.rotation = Quaternion.LookRotation(vel) * Quaternion.Euler(0, 0, bank);
    }

    public void Explode()
    {
        if (IsDead) return;
        IsDead = true;
        ScoreManager.Add(ScoreManager.Instance ? ScoreManager.Instance.jetPoints : 1500, "FIGHTER JET");
        CageSkill.Imprison(CageSkill.Kind.Soldier, CageSkill.JetCrew); // 조종사는 새장으로
        CombatFX.Explosion(transform.position, 26f);
        foreach (var r in GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials) if (m.HasProperty("_Color")) m.color *= 0.25f;
        fallSpin = new Vector3(Random.Range(-90f, 90f), Random.Range(-60f, 60f), Random.Range(200f, 400f));
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.5f);
    }

    void Fall(float dt)
    {
        vel += Vector3.down * 30f * dt;
        transform.position += vel * dt;
        transform.Rotate(fallSpin * dt, Space.Self);
        if (Random.value < 0.3f) CombatFX.Explosion(transform.position, 6f);
        if (transform.position.y <= 1f)
        {
            CombatFX.Explosion(new Vector3(transform.position.x, 2f, transform.position.z), 26f);
            Destroy(gameObject);
        }
    }
}
