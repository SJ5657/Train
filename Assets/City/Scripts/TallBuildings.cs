using System.Collections.Generic;
using UnityEngine;

// 도시에 거인 키만 한(또는 더 큰) 초고층 빌딩을 섞어 세우고,
// 거인 키의 일정 비율(기본 65%) 이상인 빌딩은 부술 수 없고 통과할 수도 없게 함.
// 초고층은 거인 시작 위치(리스폰)에서 startDistance 이상 떨어진 곳에만 나오고, 멀수록 더 많이 나옴.
// 거인이 커지면(레벨업 등) 기준 높이도 같이 올라가서 단단하던 빌딩이 하나씩 부술 수 있게 됨.
// 단단한 빌딩은 Building 레이어에서 빼서(solidLayer) 거인의 충돌 캡슐에 막히게 하고, 부술 수 있게 되면 Building 레이어로 되돌림.
[DefaultExecutionOrder(100)] // GiantHome이 거인을 시작 위치(집 마당)로 옮긴 뒤에 실행
public class TallBuildings : MonoBehaviour
{
    [Header("초고층 배치 (리스폰에서 멀수록 많이)")]
    [Tooltip("리스폰에서 이 거리(m)보다 가까운 곳에는 초고층을 세우지 않음")] public float startDistance = 300f;
    [Tooltip("이 거리(m) 이상이면 비율이 최대")] public float fullDistance = 900f;
    [Tooltip("가장 먼 곳에서 건물 중 초고층이 되는 비율 (시작 거리에서는 0에서 출발해 거리에 따라 선형으로 늘어남)")] [Range(0f, 1f)] public float maxRatio = 0.35f;
    [Tooltip("초고층 높이 = 거인 키 × 이 범위 (1 = 거인 키와 같음)")] public Vector2 heightRange = new Vector2(1f, 1.5f);
    public int seed = 777;

    [Header("건물 높이")]
    [Tooltip("모든 일반 건물 높이에 곱하는 배율 (1.15 = 15% 높게)")] [Range(0.5f, 2f)] public float heightBoost = 1.15f;

    [Header("부술 수 있는 높이")]
    [Tooltip("거인 키의 이 비율 미만인 빌딩만 부술 수 있음 (0.5 = 거인 키의 50%)")] [Range(0.1f, 1f)] public float breakableHeightRatio = 0.5f;
    [Tooltip("부술 수 없는 빌딩이 있는 레이어 (거인이 통과하지 못하는 레이어)")] public string solidLayerName = "Default";
    public float recheckInterval = 0.25f;

    int buildingLayer, solidLayer;
    GiantHealth giant;
    readonly List<Transform> solid = new List<Transform>(); // 지금 부술 수 없는 빌딩
    float nextCheckAt, lastThreshold;

    public static TallBuildings Instance { get; private set; }
    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    // 콜라이더가 지금 부술 수 없는 빌딩(또는 그 날개)에 속하면 빌딩 본체를 돌려줌
    public Transform SolidRoot(Collider c)
    {
        for (Transform t = c ? c.transform : null; t; t = t.parent)
            if (solid.Contains(t)) return t;
        return null;
    }

    // 넘어지며 미끄러지는 거인에 부딪힌 경우: 단단한 빌딩이라도 목록에서 빼고 부숨
    public void ForceBreak(Transform b, Vector3 hitPoint, Vector3 push)
    {
        if (!b) return;
        solid.Remove(b);
        SetLayer(b, buildingLayer);
        BuildingDestruction.Break(b.gameObject, hitPoint, push);
    }

    void Start()
    {
        buildingLayer = LayerMask.NameToLayer("Building");
        solidLayer = Mathf.Max(0, LayerMask.NameToLayer(solidLayerName));
        giant = GiantHealth.Instance ? GiantHealth.Instance : FindObjectOfType<GiantHealth>();
        float giantHeight = GiantHeight();
        Vector3 spawn = giant ? giant.transform.position : Vector3.zero; spawn.y = 0f;

        var roots = new List<Transform>();
        foreach (var col in FindObjectsOfType<BoxCollider>())
        {
            var b = col.transform;
            if (b.gameObject.layer != buildingLayer || (b.parent && b.parent.gameObject.layer == buildingLayer)) continue; // 건물 본체만
            roots.Add(b);
        }

        // 건물 전체를 조금씩 높임 (초고층은 아래에서 거인 키 기준 높이로 다시 정해짐)
        if (!Mathf.Approximately(heightBoost, 1f))
            foreach (var b in roots) Raise(b, b.lossyScale.y * heightBoost);

        int tall = 0;
        foreach (var b in roots)
        {
            Vector3 p = b.position; p.y = 0f;
            float t = Mathf.InverseLerp(startDistance, Mathf.Max(startDistance + 1f, fullDistance), Vector3.Distance(p, spawn));
            if (t <= 0f) continue; // 리스폰 가까운 곳은 건드리지 않음
            // 위치로 정하는 무작위라 FindObjectsOfType 순서와 상관없이 매번 같은 배치
            var rng = new System.Random(seed ^ (Mathf.RoundToInt(b.position.x * 10f) * 73856093) ^ (Mathf.RoundToInt(b.position.z * 10f) * 19349663));
            if (rng.NextDouble() >= maxRatio * t) continue;
            Raise(b, giantHeight * Mathf.Lerp(heightRange.x, heightRange.y, (float)rng.NextDouble()));
            tall++;
        }

        lastThreshold = giantHeight * breakableHeightRatio;
        foreach (var b in roots)
            if (b.lossyScale.y >= lastThreshold) { SetLayer(b, solidLayer); solid.Add(b); }
        Debug.Log($"[TallBuildings] 초고층 {tall}채 · 부술 수 없는 빌딩 {solid.Count}채 (거인 키 {giantHeight:0}m, 기준 {lastThreshold:0}m, 리스폰 {spawn})");
    }

    float GiantHeight()
    {
        float h = giant ? giant.Height : 0f;
        return h > 1f ? h : 84f;
    }

    // 바닥은 그대로 두고 높이만 키움. 날개(Wing)는 상자의 자식이라 같이 늘어남
    static void Raise(Transform b, float height)
    {
        float bottom = b.position.y - b.lossyScale.y * 0.5f;
        float parentY = b.parent ? b.parent.lossyScale.y : 1f;
        var s = b.localScale; s.y = height / Mathf.Max(0.0001f, parentY);
        b.localScale = s;
        var p = b.position; p.y = bottom + height * 0.5f;
        b.position = p;
    }

    static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform c in t) if (c.gameObject.layer != layer && c.GetComponent<Collider>()) SetLayer(c, layer);
    }

    void Update()
    {
        if (solid.Count == 0 || Time.time < nextCheckAt) return;
        nextCheckAt = Time.time + recheckInterval;
        float threshold = GiantHeight() * breakableHeightRatio;
        if (Mathf.Approximately(threshold, lastThreshold)) return;
        lastThreshold = threshold;
        // 거인이 커져서 기준이 오르면 낮은 쪽부터 다시 부술 수 있는 빌딩(Building 레이어)으로 되돌림
        for (int i = solid.Count - 1; i >= 0; i--)
        {
            var b = solid[i];
            if (!b) { solid.RemoveAt(i); continue; }
            if (b.lossyScale.y < threshold) { SetLayer(b, buildingLayer); solid.RemoveAt(i); }
        }
    }
}
