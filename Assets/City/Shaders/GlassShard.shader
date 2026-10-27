// 깨진 유리 파편: 반투명 + 프레넬 반짝임 + 모서리 윤곽선(버텍스 컬러 무게중심 좌표 사용)
Shader "City/GlassShard"
{
    Properties
    {
        _Color ("Tint", Color) = (0.85,0.93,1,1)
        _LineColor ("Edge Color", Color) = (0.12,0.14,0.18,1)
        _LineWidth ("Edge Width (px)", Float) = 1.3
        _Alpha ("Alpha", Range(0,1)) = 0.72
        _Fresnel ("Fresnel", Range(0,2)) = 0.9
        _Glint ("Glint", Range(0,4)) = 1.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            float4 _LineColor;
            float _LineWidth, _Alpha, _Fresnel, _Glint;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 bary : TEXCOORD0;
                float3 wn : TEXCOORD1;
                float3 wp : TEXCOORD2;
                UNITY_FOG_COORDS(3)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.bary = v.color.rgb;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.wn) * (facing > 0 ? 1 : -1);
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                float3 l = normalize(_WorldSpaceLightPos0.xyz);
                float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);

                float diff = 0.55 + 0.45 * saturate(dot(n, l));
                float fres = pow(1 - saturate(dot(n, v)), 3) * _Fresnel;
                float spec = pow(saturate(dot(reflect(-l, n), v)), 40) * _Glint;
                float3 col = tint.rgb * diff + fres + spec;

                // 모서리 윤곽선
                float3 d = fwidth(i.bary);
                float3 a = smoothstep(float3(0,0,0), d * _LineWidth, i.bary);
                float edge = 1 - min(a.x, min(a.y, a.z));

                float alpha = saturate(_Alpha + fres * 0.5 + spec);
                fixed4 c = fixed4(lerp(col, _LineColor.rgb, edge), lerp(alpha, 1, edge));
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
