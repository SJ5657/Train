using UnityEngine;

// 적이 거인에게서 너무 멀어지면 이동 속도를 올려 따라붙게 하는 배율 (탱크·경찰·헬기가 사용)
public static class ChaseBoost
{
    public static float startDistance = 600f;  // 이 거리(m)까지는 보통 속도 (스폰 거리보다 멀게)
    public static float fullDistance = 1400f;  // 이 거리 이상이면 최대 배율
    public static float maxMultiplier = 3f;

    public static float Factor(Vector3 pos)
    {
        var g = GiantHealth.Instance;
        if (!g) return 1f;
        Vector3 d = pos - g.transform.position; d.y = 0f;
        float t = Mathf.InverseLerp(startDistance, fullDistance, d.magnitude);
        return Mathf.Lerp(1f, maxMultiplier, t);
    }
}
