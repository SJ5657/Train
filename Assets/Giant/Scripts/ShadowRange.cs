using UnityEngine;

// 그림자 거리를 카메라에 보이는 땅 끝까지 맞춤: 거인이 커져 카메라가 멀어져도 화면 위쪽까지 그림자가 보이게.
// (그림자 거리가 짧으면 화면 먼 곳만 그림자가 없어 어색함. 너무 길면 가까운 그림자가 흐려져서 보이는 만큼만)
public class ShadowRange : MonoBehaviour
{
    [Tooltip("보이는 땅 끝 거리 × 이 배율")] public float margin = 1.1f;
    public float minDistance = 120f;
    public float maxDistance = 1500f;
    public float checkInterval = 0.5f;
    float nextAt;

    void LateUpdate()
    {
        if (Time.unscaledTime < nextAt) return;
        nextAt = Time.unscaledTime + checkInterval;
        var cam = Camera.main;
        if (!cam) return;
        // 화면 위쪽 두 모서리에서 나간 시선이 땅(y=0)에 닿는 거리 중 먼 쪽 (하늘을 보면 카메라 먼 평면까지)
        float far = 0f;
        foreach (float x in new[] { 0f, 1f })
        {
            Ray r = cam.ViewportPointToRay(new Vector3(x, 1f, 0f));
            float d = r.direction.y < -0.01f ? -r.origin.y / r.direction.y : cam.farClipPlane;
            far = Mathf.Max(far, d);
        }
        QualitySettings.shadowDistance = Mathf.Clamp(far * margin, minDistance, maxDistance);
    }
}
