using UnityEngine;

// 건물이 부서질 때 아이템 하나가 dropChance 확률로 나옴. 나올 때는 켜져 있는 아이템
// (각 생성기의 확률 값이 0보다 큰 것: 깔린 사람·감자가방·물웅덩이·보급상자·인질 차량) 중 하나를 무작위로 고름.
// 고른 것이 못 나오면(최대 개수 등) 다른 것을 시도.
public static class ItemDrops
{
    [Tooltip("건물 하나를 부술 때 아이템이 나올 확률")] public static float dropChance = 0.02f;

    public static int Spawned;      // 각 생성기가 실제로 하나 만들 때마다 올림
    static bool forcing;            // ItemDrops가 고른 생성기를 부르는 중 (각자 확률은 '켜짐/꺼짐'으로만 씀)

    // 생성기가 확률을 굴릴 때 사용: 고른 상태면 확률이 0(꺼짐)일 때만 건너뜀
    public static bool Skip(float chance) => forcing ? chance <= 0f : Random.value >= chance;

    // 거인이 이번 프레임에 지나간 길(이동 전 → 이동 후 선분)과 p 사이의 수평 거리.
    // 달리면 한 프레임에 많이 움직여서 위치 한 점만 보면 아이템을 건너뛸 수 있으므로 선분으로 잼
    public static float PathDistance(Vector3 p)
    {
        var hp = GiantHealth.Instance;
        if (!hp) return float.MaxValue;
        Vector3 b = hp.transform.position, a = GiantController.Instance ? GiantController.Instance.PrevPosition : b;
        a.y = b.y = p.y = 0f;
        Vector3 ab = b - a;
        float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector3.Distance(p, a + ab * t);
    }

    // 아이템을 놓을 바닥 높이: 위에서 쏜 광선이 맞은 것 중 바닥 근처(가장 낮은 곳 + 2m 안)에서 가장 높은 곳.
    // 인도 턱 위에는 올라가되, 초고층(Default 레이어) 옥상 위에 놓이지 않게
    public static float GroundY(Vector3 p)
    {
        var hits = Physics.RaycastAll(new Vector3(p.x, 500f, p.z), Vector3.down, 1000f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore);
        if (hits.Length == 0) return 0f;
        float low = float.MaxValue, y = float.MinValue;
        foreach (var h in hits) low = Mathf.Min(low, h.point.y);
        foreach (var h in hits) if (h.point.y <= low + 2f) y = Mathf.Max(y, h.point.y);
        return y;
    }

    static readonly System.Action<Vector3>[] Drops =
    {
        SupplyCrates.OnBuildingBroken,
        HostageEvent.OnBuildingBroken,
        RubbleVictimSpawner.OnBuildingBroken,
        PotatoSacks.OnBuildingBroken,
        WaterPuddles.OnBuildingBroken,
    };
    static readonly int[] order = { 0, 1, 2, 3, 4 };

    public static void OnBuildingBroken(Vector3 groundPos)
    {
        if (Random.value >= dropChance) return;
        for (int i = order.Length - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]); }
        int before = Spawned;
        forcing = true;
        try
        {
            foreach (int k in order)
            {
                Drops[k](groundPos);
                if (Spawned != before) return;
            }
        }
        finally { forcing = false; }
    }
}
