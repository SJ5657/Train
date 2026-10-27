using UnityEngine;

// 시민 한 명: 평소엔 블록 둘레의 인도를 따라 걷고(가끔 멈추거나 돌아섬), 거인이 가까이 오면 반대쪽으로 뛰어 도망.
// 충분히 멀어지면 가장 가까운 인도로 돌아가 다시 걸음. 도망칠 땐 건물을 비켜 감. 거인 발밑에 깔리면 납작해졌다가 사라지고,
// 거인 손에 잡히면(GiantEat) 버둥거림.
public class Civilian : MonoBehaviour
{
    enum State { Idle, Walk, Flee, Return, Squashed, Grabbed }

    CivilianManager mgr;
    Animator anim;
    State state;
    Vector2Int block;
    int corner, dir;           // 다음에 향할 인도 모서리, 도는 방향(+1 반시계 / -1 시계)
    Vector3 returnTarget;
    float stateUntil, animSpeed, squashedAt, steerAngle;
    static readonly int SpeedHash = Animator.StringToHash("Speed");
    // 막혔을 때 시도해 볼 방향 (정면 → 좌우로 점점 크게)
    static readonly float[] SteerAngles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 135f, -135f };

    public void Init(CivilianManager m, Animator a, Vector2Int b, int edge)
    {
        mgr = m; anim = a;
        if (anim) anim.Update(Random.value); // 모두 같은 발걸음이 되지 않게
        JoinWalkway(b, edge);
        if (Random.value < 0.3f) StartIdle(); else state = State.Walk;
    }

    // 블록 b의 인도, edge 변 위에서 걷기 시작
    void JoinWalkway(Vector2Int b, int edge)
    {
        block = b;
        dir = Random.value < 0.5f ? 1 : -1;
        corner = dir > 0 ? edge + 1 : edge;
    }

    void Update()
    {
        if (!mgr) return;
        float dt = Time.deltaTime;
        if (state == State.Squashed)
        {
            if (Time.time - squashedAt > 3f) Destroy(gameObject);
            return;
        }
        if (state == State.Grabbed) { if (anim) anim.SetFloat(SpeedHash, 1f); return; } // 거인 손에 잡혀 버둥거림 (위치는 GiantEat이 정함)

        var g = mgr.Giant;
        float gs = g ? g.GameScale : 27f;
        Vector3 toGiant = g ? g.transform.position - transform.position : Vector3.one * 9999f;
        toGiant.y = 0f;
        float gd = toGiant.magnitude;

        // 거인 발밑에 깔림
        if (gd < mgr.squashRadius * gs) { Squash(); return; }

        // 거인이 가까이 오면 도망, 충분히 멀어지면 가장 가까운 인도로 돌아감
        if (gd < mgr.sightRadius * gs) state = State.Flee;
        else if (state == State.Flee && gd > mgr.calmRadius * gs)
        {
            Vector2Int b = mgr.BlockAt(transform.position);
            returnTarget = mgr.NearestOnWalkway(b, transform.position, out int edge);
            JoinWalkway(b, edge);
            state = State.Return;
        }

        float speed = 0f, anim01 = 0f;
        Vector3 desired = Vector3.zero;
        bool avoid = false;
        switch (state)
        {
            case State.Idle:
                if (Time.time >= stateUntil) state = State.Walk;
                break;
            case State.Walk:
            {
                Vector3 t = mgr.Corner(block, corner);
                desired = t - transform.position; desired.y = 0f;
                if (desired.magnitude < 0.6f)
                {
                    // 모서리 도착: 다음 모서리로. 가끔 멈추거나 돌아섬
                    if (Random.value < 0.15f) dir = -dir;
                    corner += dir;
                    if (Random.value < 0.25f) { StartIdle(); desired = Vector3.zero; break; }
                    desired = mgr.Corner(block, corner) - transform.position; desired.y = 0f;
                }
                speed = mgr.walkSpeed * mgr.scale; anim01 = 0.5f;
                break;
            }
            case State.Return:
                desired = returnTarget - transform.position; desired.y = 0f;
                if (desired.magnitude < 0.6f) { StartIdle(); desired = Vector3.zero; break; }
                speed = mgr.walkSpeed * mgr.scale; anim01 = 0.5f; avoid = true;
                break;
            case State.Flee:
                desired = gd > 0.01f ? -toGiant : transform.forward;
                speed = mgr.runSpeed * mgr.scale; anim01 = 1f; avoid = true;
                break;
        }

        if (desired.sqrMagnitude > 0.0001f)
        {
            // 인도 위에서는 길이 뚫려 있으니 그대로, 도망·복귀 중엔 건물을 비켜 감
            Vector3 dirv = avoid ? Steer(desired.normalized, Mathf.Max(2f, speed * 0.4f)) : desired.normalized;
            if (dirv == Vector3.zero) { speed = 0f; anim01 = 0f; }
            else
            {
                float step = Mathf.Min(speed * dt, desired.magnitude);
                Vector3 p = transform.position + dirv * step;
                p.y = Mathf.Lerp(transform.position.y, mgr.GroundY(p), 0.5f);
                transform.position = p;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dirv), 540f * dt);
            }
        }

        animSpeed = Mathf.MoveTowards(animSpeed, anim01, dt * 3f);
        if (anim) anim.SetFloat(SpeedHash, animSpeed);
    }

    // 앞이 건물로 막혀 있으면 좌우로 비켜 갈 방향을 찾음. 어디로도 못 가면 zero.
    // 한 번 정한 우회 방향은 정면이 뚫릴 때까지 유지 (매 프레임 좌우를 오가며 제자리에서 떨지 않게)
    Vector3 Steer(Vector3 want, float lookAhead)
    {
        if (CanGo(want, 0f, lookAhead)) { steerAngle = 0f; return want; }
        if (steerAngle != 0f && CanGo(want, steerAngle, lookAhead)) return Quaternion.Euler(0f, steerAngle, 0f) * want;
        foreach (float a in SteerAngles)
            if (a != 0f && CanGo(want, a, lookAhead)) { steerAngle = a; return Quaternion.Euler(0f, a, 0f) * want; }
        return Vector3.zero;
    }

    bool CanGo(Vector3 want, float angle, float lookAhead)
        => mgr.Free(transform.position + Quaternion.Euler(0f, angle, 0f) * want * lookAhead, 0.9f);

    public bool CanBeGrabbed => state != State.Squashed && state != State.Grabbed;

    // 거인에게 잡힘: 스스로 움직이지 않음
    public void Grab() { state = State.Grabbed; }

    // 방귀 가스에 쓰러져 죽음: 그 자리에 엎어져 움직이지 않다가 사라짐
    public void Collapse()
    {
        if (state == State.Squashed || state == State.Grabbed) return;
        state = State.Squashed; squashedAt = Time.time;
        if (anim) anim.enabled = false;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * Quaternion.Euler(90f, 0f, 0f);
        transform.position += Vector3.up * 0.3f * mgr.scale;
    }

    void StartIdle()
    {
        state = State.Idle;
        stateUntil = Time.time + Random.Range(1f, 3f);
    }

    void Squash()
    {
        state = State.Squashed;
        squashedAt = Time.time;
        BloodSplat.Spawn(transform.position, mgr.scale);
        if (mgr.stompScore > 0) ScoreManager.Add(mgr.stompScore, "밟기!");
        if (anim) anim.enabled = false;
        var s = transform.localScale;
        transform.localScale = new Vector3(s.x * 1.4f, s.y * 0.12f, s.z * 1.4f);
    }
}
