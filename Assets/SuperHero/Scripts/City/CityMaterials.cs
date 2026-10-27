using UnityEngine;

namespace SuperHero
{
    /// <summary>도시에 쓰는 머티리얼/텍스처를 코드로 생성 (외부 에셋 불필요). 벽 텍스처 한 장 = 3m x 3m 한 칸.</summary>
    public static class CityMaterials
    {
        public const int StyleCount = 8;

        static Material[] styles;
        static Material concrete, roadSeg, roadCross, sidewalk, grass, park;

        struct WallStyle
        {
            public Color baseCol, glass, frame;
            public float winW, winH, smooth, metal;
            public bool ribbon;
            public WallStyle(Color b, Color g, float w, float h, float s, float m, bool ribbon = false)
            {
                baseCol = b; glass = g; winW = w; winH = h; smooth = s; metal = m; this.ribbon = ribbon;
                frame = Color.Lerp(b, Color.black, 0.45f);
            }
        }

        static readonly WallStyle[] table =
        {
            new WallStyle(new Color(0.55f,0.27f,0.21f), new Color(0.13f,0.18f,0.26f), 0.46f, 0.46f, 0.12f, 0f),            // 0 벽돌
            new WallStyle(new Color(0.62f,0.62f,0.60f), new Color(0.09f,0.13f,0.19f), 0.58f, 0.52f, 0.2f, 0f),             // 1 콘크리트
            new WallStyle(new Color(0.18f,0.28f,0.38f), new Color(0.38f,0.58f,0.74f), 0.94f, 0.84f, 0.85f, 0.35f),        // 2 유리 커튼월
            new WallStyle(new Color(0.80f,0.72f,0.55f), new Color(0.18f,0.23f,0.28f), 0.50f, 0.56f, 0.15f, 0f),           // 3 사암
            new WallStyle(new Color(0.86f,0.87f,0.89f), new Color(0.20f,0.30f,0.42f), 1.00f, 0.46f, 0.35f, 0f, true),     // 4 흰색 모던 (가로 창)
            new WallStyle(new Color(0.10f,0.12f,0.16f), new Color(0.28f,0.44f,0.60f), 0.88f, 0.78f, 0.9f, 0.5f),          // 5 검은 유리 타워
            new WallStyle(new Color(0.16f,0.40f,0.46f), new Color(0.52f,0.78f,0.88f), 0.84f, 0.74f, 0.85f, 0.35f),        // 6 청록 유리
            new WallStyle(new Color(0.76f,0.70f,0.48f), new Color(0.24f,0.29f,0.34f), 0.62f, 0.60f, 0.2f, 0f),            // 7 베이지 오피스
        };

        public static Material Style(int i)
        {
            if (styles == null || styles.Length != StyleCount || styles[0] == null)
            {
                styles = new Material[StyleCount];
                for (int k = 0; k < StyleCount; k++) styles[k] = MakeStandard("Wall" + k, MakeWallTexture(table[k], k), table[k].smooth, table[k].metal);
            }
            return styles[Mathf.Abs(i) % StyleCount];
        }

        public static Material Concrete { get { if (concrete == null) concrete = MakeStandard("Concrete", MakeNoise(64, new Color(0.55f, 0.55f, 0.53f), 0.08f, 0.2f), 0.1f, 0f); return concrete; } }
        public static Material RoadSegment { get { if (roadSeg == null) roadSeg = MakeStandard("RoadSeg", MakeRoadTexture(true), 0.15f, 0f); return roadSeg; } }
        public static Material RoadCross { get { if (roadCross == null) roadCross = MakeStandard("RoadCross", MakeRoadTexture(false), 0.15f, 0f); return roadCross; } }
        public static Material Sidewalk { get { if (sidewalk == null) sidewalk = MakeStandard("Sidewalk", MakeSidewalkTexture(), 0.1f, 0f); return sidewalk; } }
        public static Material Grass { get { if (grass == null) grass = MakeStandard("Grass", MakeNoise(64, new Color(0.30f, 0.45f, 0.22f), 0.1f, 0.25f), 0.05f, 0f); return grass; } }
        public static Material Park { get { if (park == null) park = MakeStandard("Park", MakeNoise(64, new Color(0.26f, 0.52f, 0.24f), 0.12f, 0.3f), 0.05f, 0f); return park; } }

        public static Material MakeStandard(string matName, Texture2D tex, float smooth, float metal)
        {
            var sh = Shader.Find("Standard");
            var m = new Material(sh) { name = matName, mainTexture = tex };
            m.SetFloat("_Glossiness", smooth);
            m.SetFloat("_Metallic", metal);
            return m;
        }

        // ───────────────────────── 텍스처 ─────────────────────────

        static Texture2D NewTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            return t;
        }

        static Texture2D MakeNoise(int n, Color c, float contrast, float scale)
        {
            var t = NewTex(n, n);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float f = (Mathf.PerlinNoise(x * scale, y * scale) - 0.5f) * contrast * 2f + (Random.value - 0.5f) * contrast * 0.6f;
                    px[y * n + x] = new Color(Mathf.Clamp01(c.r + f), Mathf.Clamp01(c.g + f), Mathf.Clamp01(c.b + f), 1f);
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        static Texture2D MakeWallTexture(WallStyle s, int seed)
        {
            const int N = 128;
            var t = NewTex(N, N);
            var px = new Color[N * N];
            float x0 = (1f - s.winW) * 0.5f, x1 = 1f - x0;
            float y0 = (1f - s.winH) * 0.5f + 0.04f, y1 = y0 + s.winH;
            if (y1 > 0.96f) { y1 = 0.96f; }
            int mull = s.winW > 0.85f && !s.ribbon ? 2 : 1;      // 커튼월은 세로 멀리언 추가
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                    float nz = (Mathf.PerlinNoise(x * 0.11f + seed * 7.3f, y * 0.11f) - 0.5f) * 0.10f + (Random.value - 0.5f) * 0.03f;
                    Color c = s.baseCol + new Color(nz, nz, nz, 0);
                    // 층 경계(아래쪽 슬래브 띠)
                    if (v < 0.05f || v > 0.985f) c = Color.Lerp(c, Color.black, 0.18f);
                    bool inWin = u > x0 && u < x1 && v > y0 && v < y1;
                    if (inWin)
                    {
                        float k = (v - y0) / (y1 - y0);
                        Color g = Color.Lerp(s.glass * 0.55f, s.glass * 1.15f, k);
                        // 대각선 반사 줄무늬
                        float streak = Mathf.Clamp01(1f - Mathf.Abs(((u + v * 0.7f) % 0.5f) - 0.12f) * 18f) * 0.18f;
                        g += new Color(streak, streak, streak, 0);
                        c = g;
                        float fw = 0.022f;
                        if (u < x0 + fw || u > x1 - fw || v < y0 + fw || v > y1 - fw) c = s.frame;
                        else if (mull == 2 && Mathf.Abs(u - 0.5f) < 0.012f) c = s.frame;
                        else if (s.winW <= 0.85f && Mathf.Abs(u - 0.5f) < 0.011f && !s.ribbon) c = s.frame;   // 십자 창틀
                    }
                    else if (v >= y0 - 0.035f && v < y0 && u > x0 - 0.02f && u < x1 + 0.02f)
                    {
                        c = Color.Lerp(c, Color.black, 0.25f);   // 창턱 그림자
                    }
                    c.a = 1f;
                    px[y * N + x] = c;
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        static Texture2D MakeRoadTexture(bool segment)
        {
            const int N = 256;
            var t = NewTex(N, N);
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                    float nz = (Mathf.PerlinNoise(x * 0.07f, y * 0.07f) - 0.5f) * 0.05f + (Random.value - 0.5f) * 0.03f;
                    Color c = new Color(0.17f + nz, 0.17f + nz, 0.18f + nz, 1f);
                    if (segment)
                    {
                        // 가장자리 실선 / 중앙 이중 노란선 / 차선 점선
                        if (Mathf.Abs(u - 0.035f) < 0.008f || Mathf.Abs(u - 0.965f) < 0.008f) c = new Color(0.85f, 0.85f, 0.85f);
                        if (Mathf.Abs(u - 0.488f) < 0.006f || Mathf.Abs(u - 0.512f) < 0.006f) c = new Color(0.9f, 0.75f, 0.15f);
                        if ((Mathf.Abs(u - 0.26f) < 0.006f || Mathf.Abs(u - 0.74f) < 0.006f) && (v % 0.25f) < 0.12f) c = new Color(0.85f, 0.85f, 0.85f);
                    }
                    else
                    {
                        // 횡단보도 줄무늬 (4면)
                        float e = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
                        bool zebra = ((u < 0.14f || u > 0.86f) && v > 0.14f && v < 0.86f && (Mathf.FloorToInt(v * 28f) % 2 == 0))
                                  || ((v < 0.14f || v > 0.86f) && u > 0.14f && u < 0.86f && (Mathf.FloorToInt(u * 28f) % 2 == 0));
                        if (zebra && e > 0.02f) c = new Color(0.82f, 0.82f, 0.82f);
                    }
                    px[y * N + x] = c;
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        static Texture2D MakeSidewalkTexture()
        {
            const int N = 128;
            var t = NewTex(N, N);
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float nz = (Mathf.PerlinNoise(x * 0.09f, y * 0.09f) - 0.5f) * 0.08f + (Random.value - 0.5f) * 0.03f;
                    Color c = new Color(0.58f + nz, 0.57f + nz, 0.55f + nz, 1f);
                    if (x % 64 < 2 || y % 64 < 2) c *= 0.82f;     // 보도블록 이음새
                    px[y * N + x] = c;
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }
    }
}
