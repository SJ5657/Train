using UnityEngine;

// 건물을 불규칙한 육면체 잔해로 부수는 매니저.
// 물리엔진(Rigidbody) 없이 간단한 탄도 계산으로 움직이고, GPU 인스턴싱으로 한꺼번에 그림 (GameObject 생성 없음).
// 파편은 땅에 닿는 순간 땅속으로 가라앉으며 사라진다.
public class BuildingDestruction : MonoBehaviour
{
    public static BuildingDestruction Instance { get; private set; }

    [Header("파편")]
    public Material shardMaterial;           // GlassShard.mat (불투명 설정, GPU Instancing 켜짐)
    public int maxShards = 8000;
    public int shardVariants = 12;            // 서로 다른 모양 개수
    public int minShardsPerBuilding = 6;
    public int maxShardsPerBuilding = 14;
    public Vector2 shardSizeRange = new Vector2(5f, 14f);
    public float lifetime = 6f;
    public float gravity = 30f;
    public float groundY = 0.3f;
    [Tooltip("거인 이동 방향으로 밀리는 정도")] public float pushForce = 0.3f;
    [Tooltip("부서진 지점에서 옆으로 퍼지는 속도 (최소, 최대)")] public Vector2 spreadSpeed = new Vector2(1.5f, 5f);
    public float upForce = 22f;
    [Tooltip("땅에 닿은 뒤 땅속으로 흡수되어 사라지는 데 걸리는 시간(초)")] public float sinkTime = 0.45f;
    [Tooltip("파편 기본 색 (불투명 어두운 회색)")] public Color shardColor = new Color(0.30f, 0.30f, 0.32f);
    [Range(0, 1)] public float colorTintFromBuilding = 0f; // 건물 색 반영 비율

    [Header("건물 체력 (거인 공격)")]
    [Tooltip("건물이 무너지기까지 맞아야 하는 횟수 (건물 크기와 상관없이 같음)")] [Min(1)] public int hitsToBreak = 2;
    [Tooltip("건물 높이가 거인 키의 이 비율 미만이면 한 번에 무너짐 (0.2 = 거인 키의 20%)")] [Range(0f, 1f)] public float oneHitHeightRatio = 0.2f;
    [Tooltip("이만큼 떨어져 있다가 다시 닿으면 '새로 지나감'으로 보고 피해 (초)")] public float reTouchGap = 0.3f;
    [Tooltip("계속 닿아 있을 때 반복 피해 간격 (초)")] public float repeatInterval = 1f;
    [Tooltip("다 부서지기 직전 땅으로 꺼지는 정도 (건물 높이 비율)")] [Range(0, 1)] public float maxSink = 0.35f;
    [Tooltip("다 부서지기 직전 기울어지는 각도")] public float maxTilt = 10f;
    [Tooltip("맞을 때마다 떨어져 나가는 파편 양 (완전 파괴 대비 비율)")] [Range(0, 1)] public float chipShardRatio = 0.3f;

    [Header("먼지")]
    public bool dust = true;
    public Material dustMaterial;

    public int DestroyedCount { get; private set; }
    public void RestoreCount(int n) => DestroyedCount = Mathf.Max(0, n); // 세이브 불러오기 (군대 규모도 이 수를 따름)

    // 파편 데이터 (구조체 배열 = GC 없음)
    struct Shard
    {
        public Vector3 pos, vel, scale, spinAxis;
        public Quaternion rot;
        public float spin, dieAt, life;
        public Vector4 color;
        public int variant;
        public bool sinking;
    }
    Shard[] shards;
    int count;              // 활성 파편 수 (0..count-1)
    Mesh[] meshes;
    Matrix4x4[][] batchM;
    Vector4[][] batchC;
    int[] batchN;
    MaterialPropertyBlock mpb, readMpb;
    ParticleSystem dustPs;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        Instance = this;
        shards = new Shard[maxShards];
        mpb = new MaterialPropertyBlock();
        readMpb = new MaterialPropertyBlock();
        meshes = new Mesh[shardVariants];
        batchM = new Matrix4x4[shardVariants][];
        batchC = new Vector4[shardVariants][];
        batchN = new int[shardVariants];
        for (int i = 0; i < shardVariants; i++)
        {
            meshes[i] = MakeBlockMesh(i, i * 7919 + 13);
            batchM[i] = new Matrix4x4[1023];
            batchC[i] = new Vector4[1023];
        }
        if (shardMaterial) shardMaterial.enableInstancing = true;
        if (dust && dustMaterial) dustPs = CreateDust();
    }

    // 육면체 면 (꼭짓점 인덱스, 바깥에서 봤을 때 순서)
    //   꼭짓점 번호: bit0 = x(+), bit1 = y(+), bit2 = z(+)
    static readonly int[,] BoxQuads =
    {
        { 0, 2, 3, 1 }, // -z
        { 4, 5, 7, 6 }, // +z
        { 0, 4, 6, 2 }, // -x
        { 1, 3, 7, 5 }, // +x
        { 0, 1, 5, 4 }, // -y
        { 2, 6, 7, 3 }  // +y
    };

    // 불규칙한 육면체: 모양 종류(정육면체형/판/기둥/쐐기/비틀린 블록)마다 비율을 다르게 하고,
    // 8개 꼭짓점을 제각각 흔들어 같은 모양이 없게 만든다.
    // 셰이더 윤곽선은 면의 대각선에는 그리지 않도록 버텍스 컬러를 설정 (사각형 테두리만 보임).
    static Mesh MakeBlockMesh(int variant, int seed)
    {
        var r = new System.Random(seed);
        float R(float a, float b) => a + (float)r.NextDouble() * (b - a);

        int kind = variant % 5;
        Vector3 size;
        switch (kind)
        {
            case 0:  size = new Vector3(R(0.85f, 1.1f), R(0.8f, 1.05f), R(0.85f, 1.1f)); break;  // 뭉툭한 블록
            case 1:  size = new Vector3(R(1.0f, 1.3f), R(0.3f, 0.45f), R(0.8f, 1.1f)); break;   // 납작한 판 (벽·바닥)
            case 2:  size = new Vector3(R(0.4f, 0.55f), R(1.2f, 1.5f), R(0.45f, 0.6f)); break;  // 기둥·보
            case 3:  size = new Vector3(R(0.9f, 1.15f), R(0.6f, 0.85f), R(0.7f, 0.95f)); break; // 쐐기 (아래 참고)
            default: size = new Vector3(R(0.8f, 1.05f), R(0.55f, 0.8f), R(0.9f, 1.2f)); break;  // 비틀린 블록
        }

        var p = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            Vector3 s = new Vector3((i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f);
            Vector3 q = Vector3.Scale(s, size);
            // 꼭짓점마다 제각각 흔들기 → 모서리가 반듯하지 않은 깨진 덩어리
            float j = 0.14f;
            q += new Vector3(R(-j, j) * size.x, R(-j, j) * size.y, R(-j, j) * size.z);
            p[i] = q;
        }

        if (kind == 3)
        {
            // 쐐기: 윗면 한쪽을 좁혀 비스듬히 부서진 모양
            float taper = R(0.25f, 0.55f);
            for (int i = 0; i < 8; i++) if ((i & 2) != 0 && (i & 1) != 0) p[i].x -= size.x * taper;
        }
        else if (kind == 4)
        {
            // 비틀린 블록: 윗면을 옆으로 밀고 살짝 회전
            Vector3 shear = new Vector3(R(-0.25f, 0.25f), 0, R(-0.25f, 0.25f));
            Quaternion twist = Quaternion.Euler(0, R(-20f, 20f), 0);
            for (int i = 0; i < 8; i++) if ((i & 2) != 0) p[i] = twist * p[i] + shear;
        }

        Vector3 center = Vector3.zero;
        foreach (var q in p) center += q;
        center /= 8f;

        // 면마다 삼각형 2개, 정점 분리 (각진 음영)
        var v = new Vector3[36]; var col = new Color[36]; var tri = new int[36];
        // 대각선(P-R)에는 윤곽선이 생기지 않는 무게중심 컬러
        Color cP = new Color(1, 1, 0), cQ = new Color(1, 0, 0), cR = new Color(1, 0, 1);
        int n = 0;
        for (int f = 0; f < 6; f++)
        {
            int a = BoxQuads[f, 0], b = BoxQuads[f, 1], c = BoxQuads[f, 2], d = BoxQuads[f, 3];
            // 삼각형1: a(P) b(Q) c(R), 삼각형2: c(P) d(Q) a(R)  → 대각선 a-c 숨김
            v[n] = p[a] - center; col[n++] = cP;
            v[n] = p[b] - center; col[n++] = cQ;
            v[n] = p[c] - center; col[n++] = cR;
            v[n] = p[c] - center; col[n++] = cP;
            v[n] = p[d] - center; col[n++] = cQ;
            v[n] = p[a] - center; col[n++] = cR;
        }
        for (int i = 0; i < 36; i++) tri[i] = i;

        var m = new Mesh { name = "RubbleBlock" + variant };
        m.vertices = v; m.colors = col; m.triangles = tri;
        m.RecalculateNormals(); m.RecalculateBounds();
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 2.5f);
        return m;
    }

    // 거인 공격: 건물에 피해를 주고, 체력이 다 떨어지면 무너뜨림.
    // 위층(Tier)에 닿아도 아래 건물 본체 체력이 깎임.
    public static void Hit(GameObject building, Vector3 hitPoint, Vector3 push, float damage)
    {
        if (Instance == null) { Break(building, hitPoint, push); return; }
        Transform t = building.transform;
        while (t.parent && t.parent.gameObject.layer == building.layer) t = t.parent;
        if (!t.gameObject.activeInHierarchy) return;

        // 거인 키 대비 작은 건물(oneHitHeightRatio 미만)은 한 번에 무너짐
        var giant = GiantHealth.Instance;
        if (giant && giant.Height > 1f && t.lossyScale.y < giant.Height * Instance.oneHitHeightRatio) { Break(t.gameObject, hitPoint, push); return; }

        var hp = t.GetComponent<BuildingHealth>();
        if (!hp)
        {
            hp = t.gameObject.AddComponent<BuildingHealth>();
            hp.Init(damage * (Instance.hitsToBreak - 0.5f)); // 같은 세기로 hitsToBreak번 맞으면 0 이하
        }
        if (!hp.Touch(damage, hitPoint, push, Instance.reTouchGap, Instance.repeatInterval)) return;

        if (hp.HP <= 0f) { Break(t.gameObject, hitPoint, push); return; }

        // 아직 버팀: 일부 파편이 떨어져 나가고 먼지가 일어남
        Instance.shardMul = Instance.chipShardRatio;
        Instance.dustMul = Instance.chipShardRatio;
        Instance.SpawnShards(t, hitPoint, push);
        Instance.shardMul = 1f;
        Instance.dustMul = 1f;
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.08f);
    }

    // shardMultiplier: 파편/먼지 양 배율 (폭탄처럼 한꺼번에 많이 부술 때 줄여서 사용)
    public static void Break(GameObject building, Vector3 hitPoint, Vector3 push, bool countIt = true, float shardMultiplier = 1f)
    {
        if (Instance == null) { Destroy(building); return; }
        Instance.shardMul = shardMultiplier;
        Instance.dustMul = shardMultiplier;
        Instance.DoBreak(building, hitPoint, push, countIt);
        Instance.shardMul = 1f;
        Instance.dustMul = 1f;
    }

    float shardMul = 1f;

    void DoBreak(GameObject b, Vector3 hitPoint, Vector3 push, bool countIt)
    {
        if (!b.activeInHierarchy) return;
        foreach (Transform child in b.transform)
            if (child.gameObject.activeSelf && child.GetComponent<Collider>()) SpawnShards(child, hitPoint, push);
        SpawnShards(b.transform, hitPoint, push);
        ExplosiveBuildings.OnBroken(b); // 공장이면 확률로 폭발
        b.SetActive(false);
        Destroy(b, 0.1f);
        if (countIt)
        {
            DestroyedCount++;
            ScoreManager.Add(ScoreManager.Instance ? ScoreManager.Instance.buildingPoints : 100);
            Money.AddForBuilding(); // 건물을 부수면 돈 (상점에서 사용)
            ItemDrops.OnBuildingBroken(new Vector3(b.transform.position.x, 0f, b.transform.position.z)); // 확률로 보급상자·인질 차량·깔린 사람·감자가방 중 하나만
        }
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.25f);
    }

    void SpawnShards(Transform t, Vector3 hitPoint, Vector3 push)
    {
        Vector3 size = t.lossyScale;
        Vector3 c = t.position;
        Color baseCol = Color.white;
        var srcR = t.GetComponent<Renderer>();
        if (srcR)
        {
            srcR.GetPropertyBlock(readMpb);
            baseCol = readMpb.isEmpty ? srcR.sharedMaterial.GetColor(ColorId) : (Color)readMpb.GetVector(ColorId);
        }
        Color tint = Color.Lerp(shardColor, baseCol, colorTintFromBuilding);

        float volume = size.x * size.y * size.z;
        int n = Mathf.Max(3, Mathf.RoundToInt(Mathf.Clamp(Mathf.RoundToInt(volume / 250f), minShardsPerBuilding, maxShardsPerBuilding) * shardMul));
        float minDim = Mathf.Min(size.x, Mathf.Min(size.y, size.z));

        for (int k = 0; k < n; k++)
        {
            int idx = Alloc();
            ref Shard s = ref shards[idx];
            s.pos = c + Vector3.Scale(size, new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.45f, 0.5f), Random.Range(-0.5f, 0.5f)));
            float sz = Mathf.Clamp(minDim * Random.Range(0.3f, 0.7f), shardSizeRange.x, shardSizeRange.y);
            // 축마다 비율을 따로 줘서 같은 메시라도 모양이 달라 보이게
            s.scale = new Vector3(sz * Random.Range(0.75f, 1.25f), sz * Random.Range(0.75f, 1.25f), sz * Random.Range(0.75f, 1.25f));
            s.rot = Random.rotation;
            s.spinAxis = Random.onUnitSphere;
            // 큰 덩어리일수록 천천히 회전 (무게감)
            s.spin = Random.Range(90f, 360f) * Mathf.Clamp(12f / sz, 0.35f, 1.2f);
            Vector3 away = s.pos - hitPoint; away.y = 0;
            float heightFactor = Mathf.Clamp01((s.pos.y - c.y) / Mathf.Max(size.y, 0.01f) + 0.5f);
            s.vel = away.normalized * Random.Range(spreadSpeed.x, spreadSpeed.y) + push * pushForce * Random.Range(0.3f, 1f)
                    + Vector3.up * upForce * Random.Range(0.3f, 1f) * (0.4f + heightFactor);
            s.life = lifetime * Random.Range(0.8f, 1.2f);
            s.dieAt = Time.time + s.life;
            float g = Random.Range(0.8f, 1.15f); // 조각마다 명암 살짝 다르게
            s.color = new Vector4(tint.r * g, tint.g * g, tint.b * g, 1);
            s.variant = Random.Range(0, shardVariants);
            s.sinking = false;
        }

        if (dustPs) EmitDust(c, size);
    }

    [Header("먼지 양")]
    public int dustPerBuilding = 8;
    public float dustSizeScale = 1.4f;

    // 건물 바닥 주변으로 퍼지는 먼지 + 위로 피어오르는 먼지 기둥
    float dustMul = 1f;
    void EmitDust(Vector3 c, Vector3 size)
    {
        float foot = Mathf.Max(size.x, size.z);
        float groundY0 = c.y - size.y * 0.5f;
        int n = Mathf.RoundToInt((dustPerBuilding + foot / 20f) * dustMul);
        for (int i = 0; i < n; i++)
        {
            bool plume = i < n / 3;
            Vector3 pos = new Vector3(c.x + Random.Range(-0.5f, 0.5f) * size.x, groundY0 + (plume ? Random.Range(0.2f, 0.9f) * size.y : Random.Range(0f, 0.25f) * size.y), c.z + Random.Range(-0.5f, 0.5f) * size.z);
            Vector3 outDir = pos - c; outDir.y = 0; outDir = outDir.sqrMagnitude > 0.01f ? outDir.normalized : Random.insideUnitSphere;
            var ep = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = plume ? Vector3.up * Random.Range(3f, 8f) + outDir * Random.Range(1f, 4f)
                                 : outDir * Random.Range(8f, 20f) + Vector3.up * Random.Range(0.5f, 3f),
                startSize = foot * dustSizeScale * Random.Range(0.5f, 1.1f),
                startLifetime = Random.Range(3f, 5.5f),
                rotation = Random.Range(0f, 360f),
                startColor = new Color32((byte)Random.Range(215, 240), (byte)Random.Range(212, 236), (byte)Random.Range(205, 228), (byte)Random.Range(90, 135))
            };
            dustPs.Emit(ep, 1);
        }
    }

    // 빈 슬롯 확보 (가득 차면 가장 오래된 것부터 덮어씀)
    int oldestCursor;
    int Alloc()
    {
        if (count < maxShards) return count++;
        oldestCursor = (oldestCursor + 1) % maxShards;
        return oldestCursor;
    }

    void Update()
    {
        float dt = Time.deltaTime, now = Time.time;
        for (int v = 0; v < shardVariants; v++) batchN[v] = 0;
        float sinkDur = Mathf.Max(0.05f, sinkTime);

        for (int i = 0; i < count; i++)
        {
            ref Shard s = ref shards[i];
            float left = s.dieAt - now;
            if (left <= 0)
            {
                // 마지막 요소와 교체해서 제거
                count--;
                shards[i] = shards[count];
                if (oldestCursor >= count) oldestCursor = 0;
                i--;
                continue;
            }

            float shrink;
            if (!s.sinking)
            {
                s.vel.y -= gravity * dt;
                s.pos += s.vel * dt;
                s.rot = Quaternion.AngleAxis(s.spin * dt, s.spinAxis) * s.rot;
                float floor = groundY + Mathf.Min(s.scale.x, Mathf.Min(s.scale.y, s.scale.z)) * 0.35f;
                if (s.pos.y <= floor)
                {
                    // 땅에 닿는 순간 흡수 시작: 튕기지 않고 가라앉으며 사라짐
                    s.pos.y = floor;
                    s.sinking = true;
                    s.dieAt = now + sinkDur;
                    left = sinkDur;
                }
                shrink = left < 1f ? left : 1f;
            }
            else
            {
                // 크기만큼 땅속으로 내려가면서 점점 작아짐
                float maxDim = Mathf.Max(s.scale.x, Mathf.Max(s.scale.y, s.scale.z));
                s.pos.y -= maxDim * 1.1f / sinkDur * dt;
                shrink = Mathf.Clamp01(left / sinkDur);
                shrink = shrink * (2f - shrink); // 처음엔 천천히, 끝에 빠르게 줄어듦
            }

            int vi = s.variant;
            int bn = batchN[vi];
            batchM[vi][bn] = Matrix4x4.TRS(s.pos, s.rot, s.scale * shrink);
            batchC[vi][bn] = s.color;
            batchN[vi] = bn + 1;
            if (batchN[vi] == 1023) Flush(vi);
        }
        for (int v = 0; v < shardVariants; v++) if (batchN[v] > 0) Flush(v);
    }

    void Flush(int v)
    {
        if (shardMaterial)
        {
            mpb.Clear();
            mpb.SetVectorArray(ColorId, batchC[v]);
            Graphics.DrawMeshInstanced(meshes[v], 0, shardMaterial, batchM[v], batchN[v], mpb,
                UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
        batchN[v] = 0;
    }

    ParticleSystem CreateDust()
    {
        var go = new GameObject("Dust");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 10f);
        main.startColor = new Color(0.8f, 0.8f, 0.8f, 0.55f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 4000;
        main.gravityModifier = -0.02f;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 4f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                     new[] { new GradientAlphaKey(0f, 0), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1) });
        col.color = grad;
        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(0.3f, 1.2f), new Keyframe(1, 2f)));
        var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.limit = 0f; lv.dampen = 0.04f;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = dustMaterial;
        ps.Play();
        return ps;
    }
}
