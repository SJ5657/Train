using System.Collections.Generic;
using UnityEngine;

namespace TrainGame
{
    /// <summary>
    /// 닫힌 순환 선로의 중심선. 약 1m 간격으로 균일하게 샘플한 점과 샘플별 제한속도를 갖고
    /// 선로 거리(s) → 위치/진행방향 조회를 제공한다. 점은 에디터(TrainGameSetup)에서
    /// 다각형 모서리를 원호로 깎아(필렛) 만든다: 직선 + 일정 반경 곡선.
    /// </summary>
    public class TrackPath : MonoBehaviour
    {
        public static TrackPath Instance { get; private set; }

        [HideInInspector] public Vector3[] points;
        [HideInInspector] public byte[] limits;   // 샘플별 제한속도 (km/h)
        public float length;

        float step;

        void Awake()
        {
            Instance = this;
            Init();
        }

        public void Init()
        {
            if (points != null && points.Length > 1) step = length / points.Length;
        }

        public float Wrap(float s)
        {
            s %= length;
            if (s < 0f) s += length;
            return s;
        }

        /// <summary>from → to 진행방향 부호 있는 거리 (-L/2 .. L/2). 양수면 to가 앞쪽.</summary>
        public float Delta(float from, float to)
        {
            float d = Wrap(to - from);
            return d > length * 0.5f ? d - length : d;
        }

        public Vector3 GetPosition(float s)
        {
            if (step <= 0f) Init();
            float f = Wrap(s) / step;
            int n = points.Length;
            int i = Mathf.Min((int)f, n - 1);
            return Vector3.Lerp(points[i], points[(i + 1) % n], f - i);
        }

        public Vector3 GetTangent(float s)
        {
            Vector3 d = GetPosition(s + 1f) - GetPosition(s - 1f);
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        /// <summary>진행방향 왼쪽 수평 벡터.</summary>
        public Vector3 GetLeft(float s)
        {
            return Vector3.Cross(GetTangent(s), Vector3.up).normalized;
        }

        public int GetLimit(float s)
        {
            if (limits == null || limits.Length == 0) return 80;
            if (step <= 0f) Init();
            int i = Mathf.Clamp((int)(Wrap(s) / step), 0, limits.Length - 1);
            return limits[i];
        }

        /// <summary>앞쪽 lookAhead 안에서 현재보다 낮은 제한속도가 시작되는 지점. 없으면 false.</summary>
        public bool FindNextLowerLimit(float s, float lookAhead, out int limit, out float distance)
        {
            int cur = GetLimit(s);
            for (float d = 5f; d <= lookAhead; d += 5f)
            {
                int l = GetLimit(s + d);
                if (l < cur) { limit = l; distance = d; return true; }
            }
            limit = cur; distance = 0f;
            return false;
        }

        /// <summary>월드 좌표에서 가장 가까운 선로 거리 s.</summary>
        public float ClosestS(Vector3 p)
        {
            return ClosestS(p, out _);
        }

        public float ClosestS(Vector3 p, out float distance)
        {
            if (step <= 0f) Init();
            int n = points.Length, best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float dx = points[i].x - p.x, dz = points[i].z - p.z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = i; }
            }
            // 이웃 구간으로 투영해 다듬는다
            float s = best * step;
            Vector3 a = points[best], b = points[(best + 1) % n], c = points[(best - 1 + n) % n];
            float tF = Mathf.Clamp01(Vector3.Dot(p - a, b - a) / Mathf.Max(1e-6f, (b - a).sqrMagnitude));
            float tB = Mathf.Clamp01(Vector3.Dot(p - a, c - a) / Mathf.Max(1e-6f, (c - a).sqrMagnitude));
            if (tF > 0f) s += tF * step; else s -= tB * step;
            s = Wrap(s);
            Vector3 q = GetPosition(s);
            distance = Mathf.Sqrt((q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z));
            return s;
        }

        // ───────────────────────── 생성 ─────────────────────────

        struct Seg
        {
            public bool arc;
            public Vector2 a, b;          // 직선
            public Vector2 center;        // 원호
            public float radius, start, sweep;
            public float length;

            public Vector2 Eval(float u)
            {
                if (!arc) return Vector2.Lerp(a, b, u);
                float ang = start + sweep * u;
                return center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;
            }
        }

        /// <summary>
        /// 다각형(XZ, 진행 순서) 모서리를 반경 radius 원호로 깎은 닫힌 경로를 spacing 간격으로 균일 샘플.
        /// 곡선 구간(앞 curveLead m, 뒤 curveTail m 포함)은 curveLimit, 나머지는 straightLimit.
        /// </summary>
        public static void BuildFilleted(Vector2[] corners, float radius, float spacing,
            int curveLimit, int straightLimit, float curveLead, float curveTail,
            out Vector3[] pts, out byte[] lims, out float total)
        {
            int n = corners.Length;
            var tin = new Vector2[n];
            var tout = new Vector2[n];
            var arcs = new Seg[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = corners[(i - 1 + n) % n], cur = corners[i], next = corners[(i + 1) % n];
                Vector2 d1 = (cur - prev).normalized, d2 = (next - cur).normalized;
                float theta = Mathf.Atan2(d1.x * d2.y - d1.y * d2.x, Vector2.Dot(d1, d2));
                float t = radius * Mathf.Tan(Mathf.Abs(theta) * 0.5f);
                tin[i] = cur - d1 * t;
                tout[i] = cur + d2 * t;
                Vector2 nL = new Vector2(-d1.y, d1.x);
                Vector2 center = tin[i] + nL * radius * Mathf.Sign(theta);
                Vector2 r0 = tin[i] - center;
                arcs[i] = new Seg
                {
                    arc = true, center = center, radius = radius,
                    start = Mathf.Atan2(r0.y, r0.x), sweep = theta,
                    length = radius * Mathf.Abs(theta)
                };
            }

            var segs = new List<Seg>();
            for (int i = 0; i < n; i++)
            {
                segs.Add(arcs[i]);
                Vector2 a = tout[i], b = tin[(i + 1) % n];
                segs.Add(new Seg { arc = false, a = a, b = b, length = Vector2.Distance(a, b) });
            }

            total = 0f;
            foreach (var sg in segs) total += sg.length;
            int count = Mathf.Max(8, Mathf.RoundToInt(total / spacing));
            float ds = total / count;
            pts = new Vector3[count];
            var onArc = new bool[count];

            int si = 0;
            float segStart = 0f;
            for (int k = 0; k < count; k++)
            {
                float s = k * ds;
                while (si < segs.Count - 1 && s > segStart + segs[si].length) { segStart += segs[si].length; si++; }
                var sg = segs[si];
                float u = sg.length > 1e-5f ? Mathf.Clamp01((s - segStart) / sg.length) : 0f;
                Vector2 p = sg.Eval(u);
                pts[k] = new Vector3(p.x, 0f, p.y);
                onArc[k] = sg.arc;
            }

            lims = new byte[count];
            for (int k = 0; k < count; k++) lims[k] = (byte)straightLimit;
            int lead = Mathf.RoundToInt(curveLead / ds), tail = Mathf.RoundToInt(curveTail / ds);
            for (int k = 0; k < count; k++)
            {
                if (!onArc[k]) continue;
                for (int j = -lead; j <= tail; j++) lims[(k + j + count) % count] = (byte)curveLimit;
            }
        }
    }
}
