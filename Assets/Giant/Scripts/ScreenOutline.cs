using UnityEngine;

// 화면 전체 윤곽선: 탈것·사람·게양대·잔해 등 모든 물체 가장자리에 건물과 같은 진한 선을 그림 (Hidden/ScreenOutline).
// 메인 카메라에 자동으로 붙어 동작 (카메라 깊이 버퍼 사용. 그림자 때문에 이미 만들어지는 버퍼라 추가 비용이 적음).
public class ScreenOutline : MonoBehaviour
{
    [Tooltip("Hidden/ScreenOutline 셰이더 (빌드에 포함되도록 연결)")] public Shader shader;
    public Color lineColor = new Color(0.12f, 0.12f, 0.14f, 0.9f);
    [Tooltip("이 값보다 깊이 변화가 크면 선 (작을수록 선이 많아짐)")] public float threshold = 0.012f;
    [Tooltip("선 두께 (픽셀)")] [Range(0.5f, 3f)] public float thickness = 1f;
    [Tooltip("이 거리(m)부터 선이 흐려지기 시작")] public float fadeStart = 350f;
    [Tooltip("이 거리(m)에서 선이 사라짐")] public float fadeEnd = 900f;

    Material mat;
    Camera cam;

    void Update()
    {
        // 메인 카메라에 렌더 훅을 붙임 (카메라가 바뀌어도 따라감)
        var main = Camera.main;
        if (!main || main == cam) return;
        cam = main;
        cam.depthTextureMode |= DepthTextureMode.Depth;
        var hook = cam.GetComponent<ScreenOutlineHook>();
        if (!hook) hook = cam.gameObject.AddComponent<ScreenOutlineHook>();
        hook.owner = this;
    }

    public void Render(RenderTexture src, RenderTexture dst)
    {
        if (!mat)
        {
            var sh = shader ? shader : Shader.Find("Hidden/ScreenOutline");
            if (!sh) { Graphics.Blit(src, dst); return; }
            mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        }
        mat.SetColor("_LineColor", lineColor);
        mat.SetFloat("_Threshold", threshold);
        mat.SetFloat("_Thickness", thickness);
        mat.SetFloat("_FadeStart", fadeStart);
        mat.SetFloat("_FadeEnd", fadeEnd);
        Graphics.Blit(src, dst, mat);
    }

    void OnDestroy() { if (mat) Destroy(mat); }
}
