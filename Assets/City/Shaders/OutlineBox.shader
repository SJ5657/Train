// 박스(큐브) 메시의 모서리에 일정 두께의 윤곽선을 그리는 셰이더.
// 기본 Cube 메시 + 오브젝트 스케일만으로 동작 (모든 건물이 메시/머티리얼 공유 → GPU 인스턴싱)
// 해(Directional Light)의 그림자를 받아 그늘진 곳을 어둡게 칠함 (만화풍: 그림자 색 _ShadowColor를 곱함)
Shader "City/OutlineBox"
{
    Properties
    {
        _Color ("Fill Color", Color) = (0.95,0.95,0.95,1)
        _LineColor ("Line Color", Color) = (0.12,0.12,0.14,1)
        _LineWidth ("Line Width (m)", Float) = 0.6
        _MinLinePixels ("Min Line Width (px)", Float) = 1.2
        _Shade ("Face Shading", Range(0,1)) = 0.25
        _ShadowColor ("Shadow Color", Color) = (0.62,0.66,0.78,1)
        [Toggle] _Windows ("Windows (건물 옆면 창문)", Float) = 0
        _WindowColor ("Window Color", Color) = (0.87,0.9,0.95,1)
        _FloorHeight ("Floor Height (m)", Float) = 4.6
        _WindowSpacing ("Window Spacing (m)", Float) = 4.8
        _WindowSize ("Window Size (m) xy", Vector) = (2.6, 2.7, 0, 0)
        _WindowMargin ("Wall Margin (m)", Float) = 1.4
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"

            float4 _LineColor, _ShadowColor, _WindowColor, _WindowSize;
            float _LineWidth, _MinLinePixels, _Shade, _Windows, _FloorHeight, _WindowSpacing, _WindowMargin;

            float Hash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }

            // 옆면 창문: h = 벽 가로 위치(가운데 0), y = 바닥에서 높이, halfW·height = 벽 크기 (모두 m).
            // seed = 건물마다 다른 값(건물 크기): 건물마다 층 높이·창 간격·창 모양이 달라지고, 몇몇 창은 비어 있음.
            // 반환: xyz = 창문 색, w = 창문 덮임 정도 (멀리서 창문이 몇 픽셀 안 되면 흐리게 섞음)
            float4 Window(float h, float y, float halfW, float height, float faceId, float3 seed)
            {
                float r1 = Hash(seed), r2 = Hash(seed + 1.7), r3 = Hash(seed + 3.1), r4 = Hash(seed + 4.9), r5 = Hash(seed + 6.3), r6 = Hash(seed + 8.2);
                float floorH = _FloorHeight * lerp(0.8, 1.25, r1);
                float spacing = _WindowSpacing * lerp(0.75, 1.35, r2);
                float2 size;
                if (r3 < 0.2) size = float2(spacing * 0.9, floorH * 0.42);        // 가로로 긴 띠 창
                else if (r3 < 0.35) size = float2(spacing * 0.36, floorH * 0.7);  // 세로로 길쭉한 창
                else size = float2(min(_WindowSize.x * lerp(0.75, 1.2, r4), spacing * 0.8),
                                   min(_WindowSize.y * lerp(0.8, 1.15, r5), floorH * 0.75));
                float missChance = lerp(0.0, 0.22, r6);                            // 비어 있는(창 없는) 칸 비율

                float u = h / spacing + 0.5, v = y / floorH;
                float2 fw = float2(fwidth(u), fwidth(v));
                float2 cell = float2(frac(u) - 0.5, frac(v));
                // 창문 사각형 (가운데 정렬 가로, 각 층 아래쪽 sill 위)
                float sill = (floorH - size.y) * 0.45 / floorH;
                float2 halfSz = float2(size.x * 0.5 / spacing, size.y * 0.5 / floorH);
                float2 c = float2(cell.x, cell.y - sill - halfSz.y);
                float2 dd = halfSz - abs(c);
                float2 aa = max(fw, 1e-4);
                float m = saturate(dd.x / aa.x + 0.5) * saturate(dd.y / aa.y + 0.5);
                // 벽 가장자리·1층 아래·옥상 근처는 창문 없음
                m *= step(abs(h), halfW - _WindowMargin) * step(floorH * 0.8, y) * step(y, height - _WindowMargin);
                float3 id = float3(floor(u), floor(v), faceId) + seed;
                m *= step(missChance, Hash(id + 2.3));
                // 멀리서(창문 한 칸이 몇 픽셀 안 되면) 깜빡이지 않게 창문 대신 옅은 평균색으로
                float far = saturate((max(fw.x, fw.y) - 0.12) / 0.2);
                float3 glass = _WindowColor.rgb; // 모든 창문 같은 색
                return float4(glass, lerp(m, 0.15, far));
            }
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 p : TEXCOORD0;      // 오브젝트 공간 위치 * 스케일 (m)
                float3 halfSize : TEXCOORD1;
                float3 n : TEXCOORD2;      // 오브젝트 공간 노멀
                float3 wn : TEXCOORD3;
                UNITY_FOG_COORDS(4)
                SHADOW_COORDS(5)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 s = float3(length(unity_ObjectToWorld._m00_m10_m20),
                                  length(unity_ObjectToWorld._m01_m11_m21),
                                  length(unity_ObjectToWorld._m02_m12_m22));
                o.pos = UnityObjectToClipPos(v.vertex);
                o.p = v.vertex.xyz * s;
                o.halfSize = s * 0.5;
                o.n = v.normal;
                o.wn = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o, o.pos);
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 d = i.halfSize - abs(i.p);      // 각 축 방향으로 면 가장자리까지 거리
                float3 an = abs(i.n);
                // 면의 법선 축은 제외하고 나머지 두 축 중 가까운 가장자리
                float e = 1e5;
                if (an.x < 0.5) e = min(e, d.x);
                if (an.y < 0.5) e = min(e, d.y);
                if (an.z < 0.5) e = min(e, d.z);
                float aa = fwidth(e);
                float w = max(_LineWidth, aa * _MinLinePixels);
                float edgeMask = 1 - smoothstep(w - aa, w + aa, e);

                float3 wn = normalize(i.wn);
                float shade = 1 - _Shade * (1 - saturate(0.55 + 0.45 * dot(wn, normalize(float3(0.35, 1, 0.2)))));
                // 그림자: 그림자 맵에 가려진 곳 + 해를 등진 면은 그늘 색으로
                float atten = SHADOW_ATTENUATION(i);
                float facing = saturate(dot(wn, normalize(_WorldSpaceLightPos0.xyz)) * 4);
                float lit = min(atten, lerp(0.35, 1, facing));
                float3 shadowTint = lerp(_ShadowColor.rgb, float3(1, 1, 1), lit);
                float4 fill = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                // 옆면에만 창문 (fwidth를 쓰므로 분기 없이 계산하고 가중치로 켜고 끔)
                bool xFace = an.x > 0.5;
                float h = xFace ? i.p.z : i.p.x, halfW = xFace ? i.halfSize.z : i.halfSize.x;
                float faceId = xFace ? sign(i.n.x) * 2 : sign(i.n.z) * 3;
                float3 seed = round(i.halfSize * 10) * 0.1; // 건물 크기 = 건물마다 다른 고정값 (부서지며 기울어도 그대로)
                float4 win = Window(h, i.p.y + i.halfSize.y, halfW, i.halfSize.y * 2, faceId, seed);
                float winW = win.w * step(0.5, _Windows) * step(an.y, 0.5);
                // 건물 색이 어두워지면(부서지는 중) 창문도 같이 어두워지게 건물 색 밝기를 곱함
                float3 surface = lerp(fill.rgb, win.rgb * saturate(max(fill.r, max(fill.g, fill.b)) * 1.1), winW);
                fixed4 col = lerp(float4(surface * shade * shadowTint, 1), _LineColor, edgeMask);
                col.a = 1;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }

        // 그림자 맵에 건물을 그리는 패스 (GPU 인스턴싱 지원)
        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            struct v2f { V2F_SHADOW_CASTER; };

            v2f vert (appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 frag (v2f i) : SV_Target { SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
    Fallback Off
}
