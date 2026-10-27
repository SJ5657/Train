using UnityEngine;

// 도시 도로를 달리는 자동차: 교차로(그리드)를 따라 직진하다가 교차로마다 무작위로 방향을 골라 계속 달림 (오른쪽 차선).
// 거인 발에 밟히면 찌그러지며 폭발 (Military 레이어 + 충돌 박스, GiantStomp가 Crush 호출). 적이 아니라서 공격은 하지 않음.
public class TrafficCar : MonoBehaviour
{
    public CityGenerator city;
    public float speed = 25f;
    public float turnSpeed = 220f;
    [Tooltip("도로 가운데에서 오른쪽으로 떨어진 거리 (m)")] public float laneOffset = 3.5f;

    [Tooltip("켜면 교차로마다 거인에게서 가장 멀어지는 길을 골라 도망감 (인질 이벤트 차량)")] public bool flee;

    float emergeT = 1f, emergeTime; Vector3 emergeFrom;
    // Init 다음에 부름: 이 자리(부서진 건물)에서 출발해 emergeTime초 동안 도로로 빠져나옴
    public void EmergeFrom(Vector3 from, float time) { emergeFrom = from; emergeTime = time; emergeT = 0f; transform.position = from; }

    public bool IsDead { get; private set; }

    // ───── 서로 겹치지 않기 ─────
    public static readonly System.Collections.Generic.List<TrafficCar> All = new System.Collections.Generic.List<TrafficCar>();
    [Tooltip("차 길이(m). 생성 때 모델 크기로 채워짐")] public float length = 10f;
    [Tooltip("앞차를 살피는 거리(m)")] public float lookAhead = 45f;
    [Tooltip("앞차와 이 거리(m, 차체 사이) 이내면 완전히 멈춤")] public float stopGap = 3f;
    [Tooltip("앞차와 이 거리(m) 이내면 점점 감속")] public float followGap = 22f;
    [Tooltip("옆으로 이 거리(m) 이내에 있으면 같은 길을 가는 것으로 봄")] public float laneWidth = 6f;
    float curSpeed, limit = 1f, nextCheck;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // 앞에 있는 차에 맞춰 속도 배율(0~1)을 정함. 같은 방향 앞차는 항상 피하고,
    // 교차로에서 엇갈리는 차는 인스턴스 ID가 작은 쪽이 먼저 지나감 (서로 양보하다 멈추는 교착 방지)
    float CheckAhead()
    {
        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 right = Right(fwd), pos = transform.position;
        int myId = GetInstanceID();
        float best = 1f;
        foreach (var o in All)
        {
            if (!o || o == this || o.IsDead) continue;
            Vector3 d = o.transform.position - pos; d.y = 0f;
            float f = Vector3.Dot(d, fwd);
            if (f <= 0f || f > lookAhead) continue;
            if (Mathf.Abs(Vector3.Dot(d, right)) > laneWidth) continue;
            Vector3 of = o.transform.forward; of.y = 0f;
            bool same = Vector3.Dot(of, fwd) > 0.5f;
            if (!same && o.GetInstanceID() > myId) continue; // 엇갈리는 차는 ID가 작은 쪽에게만 양보
            float gap = f - (length + o.length) * 0.5f;
            best = Mathf.Min(best, Mathf.Clamp01((gap - stopGap) / Mathf.Max(0.1f, followGap - stopGap)));
        }
        return best;
    }

    Vector3 gridOrigin; float pitch; int maxI, maxJ;
    int ci, cj, ti, tj, prevI = -1, prevJ = -1;
    float t;                 // 현재 구간(ci,cj → ti,tj)에서 진행 비율
    Vector3 offset;          // 현재 차선 오프셋 (차선이 바뀔 때 부드럽게 이동)

    // 교차로 (i, j)에 세워서 시작
    public void Init(CityGenerator c, int i, int j)
    {
        city = c;
        pitch = city.blockSize + city.roadWidth;
        float sizeX = city.blocksX * pitch + city.roadWidth, sizeZ = city.blocksZ * pitch + city.roadWidth;
        gridOrigin = city.transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f + new Vector3(city.roadWidth, 0, city.roadWidth) * 0.5f;
        maxI = city.blocksX; maxJ = city.blocksZ;
        ci = i; cj = j;
        curSpeed = speed;
        PickNext();
        Vector3 dir = Dir();
        offset = Right(dir) * laneOffset;
        transform.position = Node(ci, cj) + offset;
        transform.rotation = Quaternion.LookRotation(dir);
    }

    Vector3 Node(int i, int j) => new Vector3(gridOrigin.x + i * pitch, 0f, gridOrigin.z + j * pitch);
    Vector3 Dir() { Vector3 d = Node(ti, tj) - Node(ci, cj); d.y = 0; return d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward; }
    static Vector3 Right(Vector3 dir) => Vector3.Cross(Vector3.up, dir);

    // 다음 교차로 고르기: 온 길로 되돌아가지 않고 (막다른 길이면 되돌아감), 막힌 도로(합친 블록)는 지나지 않음
    void PickNext()
    {
        var opts = new System.Collections.Generic.List<Vector2Int>(4);
        if (ci > 0 && city.RoadOpen(ci, cj, ci - 1, cj)) opts.Add(new Vector2Int(ci - 1, cj));
        if (ci < maxI && city.RoadOpen(ci, cj, ci + 1, cj)) opts.Add(new Vector2Int(ci + 1, cj));
        if (cj > 0 && city.RoadOpen(ci, cj, ci, cj - 1)) opts.Add(new Vector2Int(ci, cj - 1));
        if (cj < maxJ && city.RoadOpen(ci, cj, ci, cj + 1)) opts.Add(new Vector2Int(ci, cj + 1));
        if (opts.Count > 1) opts.RemoveAll(o => o.x == prevI && o.y == prevJ);
        if (opts.Count == 0) { ti = ci; tj = cj; return; }
        var n = opts[Random.Range(0, opts.Count)];
        var giant = flee ? GiantHealth.Instance : null;
        if (giant)
        {
            float best = -1f; Vector3 gp = giant.transform.position; gp.y = 0f;
            foreach (var o in opts) { float d = (Node(o.x, o.y) - gp).sqrMagnitude; if (d > best) { best = d; n = o; } }
        }
        ti = n.x; tj = n.y; t = 0f;
    }

    void Update()
    {
        if (IsDead || !city) return;
        if (ti == ci && tj == cj) { PickNext(); return; }
        Vector3 a = Node(ci, cj), b = Node(ti, tj);
        float len = Vector3.Distance(a, b);
        // 앞차·교차로 차량과 겹치지 않게 속도 조절 (0.1초마다 확인, 감속은 빠르게 · 가속은 천천히)
        if (Time.time >= nextCheck) { nextCheck = Time.time + 0.1f + Random.value * 0.03f; limit = flee ? 1f : CheckAhead(); }
        float want = speed * limit;
        curSpeed = Mathf.MoveTowards(curSpeed, want, (want < curSpeed ? 80f : 25f) * Time.deltaTime);
        t += curSpeed * Time.deltaTime / Mathf.Max(1f, len);
        if (t >= 1f)
        {
            prevI = ci; prevJ = cj; ci = ti; cj = tj;
            PickNext();
            a = Node(ci, cj); b = Node(ti, tj);
            t = 0f;
        }
        Vector3 dir = (b - a).normalized;
        offset = Vector3.MoveTowards(offset, Right(dir) * laneOffset, 14f * Time.deltaTime);
        Vector3 pos = Vector3.Lerp(a, b, t) + offset;
        pos.y = transform.position.y;
        if (emergeT < 1f) // 건물 자리에서 도로로 빠져나오는 중
        {
            emergeT += Time.deltaTime / Mathf.Max(0.1f, emergeTime);
            float k = Mathf.Clamp01(emergeT); k = k * k * (3f - 2f * k);
            Vector3 from = emergeFrom; from.y = pos.y;
            pos = Vector3.Lerp(from, pos, k);
        }
        transform.position = pos;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
    }

    // 거인에게 밟힘: 납작하게 찌그러지며 폭발한 뒤 잠시 뒤 사라짐
    public void Crush()
    {
        if (IsDead) return;
        IsDead = true;
        enabled = false;
        ScoreManager.Add(50, "CAR");
        CombatFX.Explosion(transform.position + Vector3.up * 2f, 8f);
        Vector3 sc = transform.localScale;
        transform.localScale = new Vector3(sc.x * 1.15f, sc.y * 0.2f, sc.z * 1.1f);
        foreach (var r in GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials) if (m.HasProperty("_Color")) m.color *= 0.35f;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        Destroy(gameObject, 6f);
    }
}
