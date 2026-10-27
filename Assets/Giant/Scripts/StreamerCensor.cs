using System.Collections.Generic;
using UnityEngine;

// 스트리머 모드(시작 화면 '설정'에서 켬): 거인의 중요 부위 높이(가슴·사타구니)를 검은 띠로 가림.
// 거인 모델의 뼈(가슴·엉덩이)에 붙은 "Censor_..." 상자는 위치·크기 기준점으로만 쓰고(보이지 않음),
// 카메라가 그릴 때마다 그 높이의 몸통 단면(타원)을 화면에 투영해서, 화면에 보이는 몸 너비 전체를 덮는
// 화면과 나란한 검은 네모를 맨 위에 그림 → UI처럼 회전 없이, 어느 방향에서 봐도 그 높이의 몸을 가로질러 가림.
// (확대 화면 등 다른 카메라도 각자 자기 화면 기준으로 그림)
public class StreamerCensor : MonoBehaviour
{
    class Plate
    {
        public Transform anchor; public Transform quad; public MeshRenderer renderer;
        // 모델 단위(키 약 1.7) 값: 기준 상자 가로, 몸 중심까지 뒤로, 단면 반폭·반깊이, 띠 높이
        public float anchorWidth, back, drop, moveDrop, selectDrop, moveFwd, halfW, halfD, up, down; // up·down = 중심에서 띠 위·아래 끝까지, drop = 아래로 내리는 거리 (moveDrop = 이동 중, selectDrop = 성별 선택 화면에서), moveFwd = 이동 중 앞으로 내미는 거리
        public float shownDrop = -1f, shownFwd;
    }
    readonly List<Plate> plates = new List<Plate>();
    static Material mat;
    [Tooltip("띠 너비 배율 (1 = 몸 단면 너비 그대로)")] public float margin = 1f;

    void Awake()
    {
        if (!mat)
        {
            // UI처럼 항상 맨 위에 그림 (건물 등에 가려지지 않음)
            mat = new Material(Shader.Find("UI/Default")) { color = Color.black, renderQueue = 4000 };
            mat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
        }
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("Censor_")) continue;
            var mr = t.GetComponent<MeshRenderer>();
            if (mr) mr.enabled = false; // 기준 상자는 안 보이게 (위치를 따라가도록 켜 둠)
            t.gameObject.SetActive(true);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = t.name + "_Plate";
            Destroy(q.GetComponent<Collider>());
            var qr = q.GetComponent<MeshRenderer>();
            qr.sharedMaterial = mat;
            qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            qr.receiveShadows = false;
            q.layer = t.gameObject.layer;
            q.transform.SetParent(transform, true);
            q.SetActive(false);
            bool chest = t.name.Contains("Chest");
            plates.Add(chest
                ? new Plate { anchor = t, quad = q.transform, renderer = qr, anchorWidth = 0.32f, back = 0.14f, drop = 0.045f, moveDrop = 0.09f, selectDrop = 0.01f, moveFwd = 0.07f, halfW = 0.17f, halfD = 0.185f, up = 0.08f, down = 0.13f }
                : new Plate { anchor = t, quad = q.transform, renderer = qr, anchorWidth = 0.18f, back = 0.03f, drop = 0f, moveDrop = 0f, selectDrop = 0f, moveFwd = 0f, halfW = 0.15f, halfD = 0.12f, up = 0.07f, down = 0.07f });
        }
    }

    // 거인이 움직이는 정도 (0 = 서 있음, 1 = 걷기 이상). 상체를 숙이는 건 걷기·달리기 애니메이션이라
    // 실제 이동 속도(건물에 막히면 0) 대신 애니메이터 Speed 값(0 서기 · 0.5 걷기 · 1 달리기)으로 봄
    static readonly int SpeedHash = Animator.StringToHash("Speed");
    float MoveAmount()
    {
        var c = GiantController.Instance;
        if (!c || GameStartMenu.InMenu) return 0f;
        var a = c.GetComponentInChildren<Animator>();
        return a ? Mathf.Clamp01(a.GetFloat(SpeedHash) / 0.5f) : 0f;
    }

    void OnEnable() { Camera.onPreCull += Face; }
    void OnDisable() { Camera.onPreCull -= Face; foreach (var p in plates) if (p.quad) p.quad.gameObject.SetActive(false); }

    void LateUpdate()
    {
        bool on = GameStartMenu.StreamerMode;
        float move = MoveAmount();
        foreach (var p in plates)
        {
            if (p.quad) p.quad.gameObject.SetActive(on && p.anchor && p.anchor.gameObject.activeInHierarchy);
            // 성별 선택 화면(정면 큰 화면)에서는 더 위, 이동 중엔(상체를 숙임) 더 아래. 부드럽게 오르내림
            float drop = GameStartMenu.InSelect ? p.selectDrop : Mathf.Lerp(p.drop, p.moveDrop, move);
            p.shownDrop = p.shownDrop < 0f ? drop : Mathf.MoveTowards(p.shownDrop, drop, Time.unscaledDeltaTime * 0.15f);
            // 이동 중엔 상체를 숙이며 가슴이 앞으로 나오므로 띠도 앞으로
            float fwdOff = GameStartMenu.InSelect ? 0f : p.moveFwd * move;
            p.shownFwd = Mathf.MoveTowards(p.shownFwd, fwdOff, Time.unscaledDeltaTime * 0.15f);
        }
    }

    // 카메라마다 그리기 직전: 그 높이의 몸통 단면이 화면에서 차지하는 가로 범위를 덮도록 네모를 놓음
    void Face(Camera cam)
    {
        var ct = cam.transform;
        foreach (var p in plates)
        {
            if (!p.quad || !p.quad.gameObject.activeSelf || !p.anchor) continue;
            float m = p.anchor.lossyScale.x / p.anchorWidth; // 모델 단위 → 월드 (거인 크기)
            Vector3 right = p.anchor.right, fwd = p.anchor.forward;
            float drop = Mathf.Max(0f, p.shownDrop);
            Vector3 center = p.anchor.position - fwd * (p.back - p.shownFwd) * m - p.anchor.up * drop * m; // 그 높이의 몸 중심

            p.renderer.enabled = true; // 거인이 어느 쪽을 보든 항상 보임

            // 몸통 단면(타원) 둘레 점들을 카메라 공간으로: 화면 가로 범위(x/z)와 가장 가까운 깊이
            float minU = float.MaxValue, maxU = float.MinValue, nearZ = float.MaxValue;
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                Vector3 w = center + (right * Mathf.Cos(a) * p.halfW + fwd * Mathf.Sin(a) * p.halfD) * m;
                Vector3 c = ct.InverseTransformPoint(w);
                if (c.z <= 0.01f) continue;
                float u = c.x / c.z;
                minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u); nearZ = Mathf.Min(nearZ, c.z);
            }
            if (nearZ == float.MaxValue) { p.renderer.enabled = false; continue; }
            Vector3 cc = ct.InverseTransformPoint(center);
            float v = cc.y / Mathf.Max(0.01f, cc.z) + (p.up - p.down) * 0.5f * m / Mathf.Max(0.01f, cc.z); // 위·아래 길이가 달라서 그만큼 중심을 옮김
            float z = nearZ * 0.97f; // 몸보다 조금 카메라 쪽 깊이에 (맨 위에 그리므로 위치만 맞으면 됨)
            float midU = (minU + maxU) * 0.5f;
            p.quad.position = ct.TransformPoint(new Vector3(midU * z, v * z, z));
            p.quad.rotation = ct.rotation; // 화면과 나란히: 회전하지 않는 네모 (UI처럼)
            float width = (maxU - minU) * z * margin;
            float height = (p.up + p.down) * m * z / Mathf.Max(0.01f, cc.z);
            p.quad.localScale = new Vector3(width, height, 1f);
        }
    }
}
