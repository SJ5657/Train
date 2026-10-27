// 화면 전체 윤곽선 (ScreenOutline.cs가 메인 카메라에 적용): 카메라 깊이 버퍼에서 물체 가장자리를 찾아 진한 선을 그림.
// 1/깊이는 평평한 면 위에서 화면을 따라 일정하게 변하므로, 그 2차 차분(라플라시안)이 크면 물체 경계(윤곽)로 봄
// → 도로·벽 같은 평면에는 선이 안 생기고 자동차·탱크·사람·건물 테두리에만 생김.
Shader "Hidden/ScreenOutline"
{
    Properties
    {
        _MainTex ("Screen", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _MainTex_TexelSize;
            float4 _LineColor;
            float _Threshold, _Thickness, _FadeStart, _FadeEnd;

            float InvDepth(float2 uv)
            {
                float z = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
                return 1.0 / max(z, 1e-3);
            }

            fixed4 frag (v2f_img i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                float2 d = _MainTex_TexelSize.xy * _Thickness;
                float c = InvDepth(i.uv);
                float l = InvDepth(i.uv - float2(d.x, 0)), r = InvDepth(i.uv + float2(d.x, 0));
                float b = InvDepth(i.uv - float2(0, d.y)), t = InvDepth(i.uv + float2(0, d.y));
                // 평면 위에서는 0에 가까움. 물체 경계에서 큼 (가까운 쪽 깊이 기준으로 정규화)
                float near = max(max(c, l), max(max(r, b), t));
                float lap = (abs(l + r - 2 * c) + abs(b + t - 2 * c)) / near;
                float edge = smoothstep(_Threshold, _Threshold * 2.5, lap);
                // 아주 먼 곳은 선이 자글거리지 않게 흐리게
                float z = 1.0 / near;
                edge *= 1 - saturate((z - _FadeStart) / max(1, _FadeEnd - _FadeStart));
                col.rgb = lerp(col.rgb, _LineColor.rgb, edge * _LineColor.a);
                return col;
            }
            ENDCG
        }
    }
}
