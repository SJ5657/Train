using System.Collections.Generic;
using UnityEngine;

// 국기 그림: 이미지 파일 없이 코드로 픽셀을 칠해 만듦 (가로 3 : 세로 2). 한 번 만든 그림은 재사용.
public static class Flags
{
    public struct Flag { public string name; public Texture2D tex; }

    const int W = 120, Hgt = 80;
    delegate Color Painter(float x, float y); // x: 0(깃대 쪽)~1, y: 0(아래)~1

    static readonly Color Red = new Color(0.8f, 0.08f, 0.13f), White = Color.white, Blue = new Color(0f, 0.22f, 0.6f),
        Navy = new Color(0.04f, 0.13f, 0.4f), Yellow = new Color(1f, 0.82f, 0f), Green = new Color(0f, 0.55f, 0.27f),
        Black = new Color(0.05f, 0.05f, 0.05f), Orange = new Color(1f, 0.53f, 0.1f), Sky = new Color(0.36f, 0.6f, 0.85f);

    static readonly (string name, Painter paint)[] All =
    {
        ("대한민국", Korea),
        ("미국", Usa),
        ("일본", (x, y) => Circle(x, y, 0.5f, 0.5f, 0.3f) ? Red : White),
        ("중국", (x, y) => Star(x, y, 0.17f, 0.72f, 0.15f, 0f) || Star(x, y, 0.33f, 0.9f, 0.05f, 20f) || Star(x, y, 0.4f, 0.78f, 0.05f, 45f)
                          || Star(x, y, 0.4f, 0.6f, 0.05f, 0f) || Star(x, y, 0.33f, 0.48f, 0.05f, 20f) ? Yellow : Red),
        ("프랑스", (x, y) => V3(x, Blue, White, Red)),
        ("이탈리아", (x, y) => V3(x, Green, White, Red)),
        ("독일", (x, y) => H3(y, Black, Red, Yellow)),
        ("러시아", (x, y) => H3(y, White, Blue, Red)),
        ("네덜란드", (x, y) => H3(y, Red, White, Blue)),
        ("우크라이나", (x, y) => y > 0.5f ? Blue : Yellow),
        ("폴란드", (x, y) => y > 0.5f ? White : Red),
        ("인도네시아", (x, y) => y > 0.5f ? Red : White),
        ("벨기에", (x, y) => V3(x, Black, Yellow, Red)),
        ("아일랜드", (x, y) => V3(x, Green, White, Orange)),
        ("스웨덴", (x, y) => Nordic(x, y, Sky, Yellow)),
        ("덴마크", (x, y) => Nordic(x, y, Red, White)),
        ("핀란드", (x, y) => Nordic(x, y, White, Blue)),
        ("스위스", (x, y) => (Mathf.Abs(x - 0.5f) < 0.06f && Mathf.Abs(y - 0.5f) < 0.3f) || (Mathf.Abs(y - 0.5f) < 0.09f && Mathf.Abs(x - 0.5f) < 0.2f) ? White : Red),
        ("베트남", (x, y) => Star(x, y, 0.5f, 0.5f, 0.3f, 0f) ? Yellow : Red),
        ("방글라데시", (x, y) => Circle(x, y, 0.45f, 0.5f, 0.3f) ? Red : Green),
        ("브라질", Brazil),
        ("튀르키예", (x, y) => (Circle(x, y, 0.37f, 0.5f, 0.25f) && !Circle(x, y, 0.43f, 0.5f, 0.2f)) || Star(x, y, 0.58f, 0.5f, 0.1f, -90f) ? White : Red),
        ("오스트리아", (x, y) => H3(y, Red, White, Red)),
        ("헝가리", (x, y) => H3(y, Red, White, Green)),
        ("나이지리아", (x, y) => V3(x, Green, White, Green)),
        ("그리스", Greece),
    };

    static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

    public static Flag Random()
    {
        var f = All[UnityEngine.Random.Range(0, All.Length)];
        if (!cache.TryGetValue(f.name, out var tex) || !tex) cache[f.name] = tex = Paint(f.paint);
        return new Flag { name = f.name, tex = tex };
    }

    static Texture2D Paint(Painter p)
    {
        var tex = new Texture2D(W, Hgt, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[W * Hgt];
        for (int j = 0; j < Hgt; j++)
            for (int i = 0; i < W; i++)
                px[j * W + i] = p((i + 0.5f) / W, (j + 0.5f) / Hgt);
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // ───── 도형 도우미 (좌표는 깃발 비율 3:2를 감안해 원이 찌그러지지 않게 x에 1.5를 곱함) ─────
    static Color V3(float x, Color a, Color b, Color c) => x < 1f / 3f ? a : x < 2f / 3f ? b : c;
    static Color H3(float y, Color top, Color mid, Color bottom) => y > 2f / 3f ? top : y > 1f / 3f ? mid : bottom;
    static bool Circle(float x, float y, float cx, float cy, float r) { float dx = (x - cx) * 1.5f, dy = y - cy; return dx * dx + dy * dy < r * r; }

    static Color Nordic(float x, float y, Color field, Color cross)
        => Mathf.Abs(x - 0.36f) < 0.07f || Mathf.Abs(y - 0.5f) < 0.1f ? cross : field;

    // 꼭짓점이 위를 향한 별 (rot = 회전 각도)
    static bool Star(float x, float y, float cx, float cy, float r, float rot)
    {
        float dx = (x - cx) * 1.5f, dy = y - cy;
        float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg - 90f - rot, dist = Mathf.Sqrt(dx * dx + dy * dy);
        if (dist > r) return false;
        // 각도에 따라 바깥 꼭짓점(r)과 안쪽 꼭짓점(0.38r) 사이 직선으로 이어진 테두리
        float seg = Mathf.Repeat(ang, 72f);
        float a = Mathf.Abs(seg - 36f) * Mathf.Deg2Rad;         // 0 = 안쪽 꼭짓점 방향, 36° = 바깥 꼭짓점
        Vector2 outer = new Vector2(Mathf.Cos(36f * Mathf.Deg2Rad), Mathf.Sin(36f * Mathf.Deg2Rad)) * r, inner = new Vector2(0.38f * r, 0f);
        Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        // 원점에서 dir 방향 광선이 inner→outer 선분과 만나는 거리
        Vector2 e = outer - inner;
        float denom = dir.x * e.y - dir.y * e.x;
        float edge = Mathf.Abs(denom) < 1e-6f ? r : (inner.x * e.y - inner.y * e.x) / denom;
        return dist < edge;
    }

    static Color Usa(float x, float y)
    {
        if (x < 0.4f && y > 0.46f)
        {
            float gx = (x / 0.4f) * 6f, gy = ((y - 0.46f) / 0.54f) * 5f;
            float fx = gx - Mathf.Floor(gx) - 0.5f, fy = gy - Mathf.Floor(gy) - 0.5f;
            return fx * fx + fy * fy < 0.07f ? White : Navy;
        }
        return Mathf.FloorToInt(y * 13f) % 2 == 0 ? Red : White;
    }

    static Color Greece(float x, float y)
    {
        if (x < 0.37f && y > 0.44f)
            return Mathf.Abs(x - 0.185f) < 0.04f || Mathf.Abs(y - 0.72f) < 0.06f ? White : Blue;
        return Mathf.FloorToInt(y * 9f) % 2 == 0 ? Blue : White;
    }

    static Color Brazil(float x, float y)
    {
        float dx = Mathf.Abs(x - 0.5f) / 0.42f, dy = Mathf.Abs(y - 0.5f) / 0.4f;
        if (Circle(x, y, 0.5f, 0.5f, 0.2f)) return Mathf.Abs((y - 0.5f) + (x - 0.5f) * 0.3f) < 0.025f ? White : Navy;
        return dx + dy < 1f ? Yellow : Green;
    }

    // 태극기: 흰 바탕, 가운데 태극(위 빨강 · 아래 파랑, 비스듬히), 네 모서리에 검은 괘
    static Color Korea(float x, float y)
    {
        float px = (x - 0.5f) * 1.5f, py = y - 0.5f; // 가운데 원점, 가로세로 같은 비율
        const float r = 0.25f;
        // 태극: 왼쪽 위 → 오른쪽 아래로 기운 축
        float ang = 33.7f * Mathf.Deg2Rad, c = Mathf.Cos(ang), s = Mathf.Sin(ang);
        float u = px * c - py * s, v = px * s + py * c; // u: 축 방향(왼쪽 위 → 오른쪽 아래가 -/+)
        if (u * u + v * v < r * r)
        {
            float h = r * 0.5f;
            if ((u + h) * (u + h) + v * v < h * h) return Red;   // 왼쪽 작은 원: 빨강
            if ((u - h) * (u - h) + v * v < h * h) return Blue;  // 오른쪽 작은 원: 파랑
            return v > 0f ? Red : Blue;
        }
        // 괘: 대각선 위 중심에서 안쪽을 향해 3줄 (true = 끊긴 막대)
        if (Trigram(px, py, -1, 1, false, false, false)) return Black; // 건 ☰ (왼쪽 위)
        if (Trigram(px, py, 1, -1, true, true, true)) return Black;    // 곤 ☷ (오른쪽 아래)
        if (Trigram(px, py, 1, 1, true, false, true)) return Black;    // 감 ☵ (오른쪽 위)
        if (Trigram(px, py, -1, -1, false, true, false)) return Black; // 리 ☲ (왼쪽 아래)
        return White;
    }

    static bool Trigram(float px, float py, int sx, int sy, bool b0, bool b1, bool b2)
    {
        Vector2 dir = new Vector2(sx * 1.5f, sy).normalized;   // 가운데 → 모서리 방향
        Vector2 center = dir * 0.42f;
        Vector2 d = new Vector2(px, py) - center;
        float along = Vector2.Dot(d, dir), across = Vector2.Dot(d, new Vector2(-dir.y, dir.x));
        const float barT = 0.028f, gap = 0.02f, len = 0.11f;
        if (Mathf.Abs(across) > len) return false;
        for (int k = 0; k < 3; k++)
        {
            float off = (k - 1) * (barT + gap);
            if (Mathf.Abs(along - off) < barT * 0.5f)
            {
                bool broken = k == 0 ? b0 : k == 1 ? b1 : b2;
                return !broken || Mathf.Abs(across) > 0.015f;
            }
        }
        return false;
    }
}
