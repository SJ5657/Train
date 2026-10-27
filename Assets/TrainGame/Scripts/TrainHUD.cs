using UnityEngine;

namespace TrainGame
{
    /// <summary>
    /// IMGUI HUD: 운행 정보(다음 역, 거리, 목표시간, 점수), 제한속도 표지, 기관실 계기판(속도·레귤레이터·브레이크·리버서),
    /// 정차 위치 정밀 게이지, 탑승/하차 안내, 메시지, 조작 도움말(F1).
    /// </summary>
    public class TrainHUD : MonoBehaviour
    {
        public TrainController train;
        public RouteManager route;
        public DriverController driver;

        GUIStyle label, small, big, huge, center, keyStyle, title;
        bool help = true;
        float helpHideAt;

        static readonly Color Panel = new Color(0.05f, 0.06f, 0.08f, 0.62f);
        static readonly Color Amber = new Color(1f, 0.78f, 0.3f);
        static readonly Color Good = new Color(0.45f, 0.95f, 0.55f);
        static readonly Color Bad = new Color(1f, 0.38f, 0.3f);

        void Start()
        {
            if (train == null) train = TrainController.Instance;
            if (route == null) route = RouteManager.Instance;
            if (driver == null) driver = DriverController.Instance;
            helpHideAt = Time.time + 30f;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) { help = !help; helpHideAt = float.MaxValue; }
            else if (help && Time.time > helpHideAt) help = false;
        }

        void InitStyles()
        {
            if (label != null) return;
            var baseStyle = new GUIStyle(GUI.skin.label) { font = KoreanFont.Get(), wordWrap = false };
            label = new GUIStyle(baseStyle) { fontSize = 16, normal = { textColor = Color.white } };
            small = new GUIStyle(baseStyle) { fontSize = 13, normal = { textColor = new Color(1f, 1f, 1f, 0.9f) } };
            title = new GUIStyle(baseStyle) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            big = new GUIStyle(baseStyle) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            huge = new GUIStyle(baseStyle) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = Color.white } };
            center = new GUIStyle(baseStyle) { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            keyStyle = new GUIStyle(baseStyle) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        }

        static void Box(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Text(Rect r, string s, GUIStyle st, Color c)
        {
            var old = st.normal.textColor;
            st.normal.textColor = c;
            GUI.Label(r, s, st);
            st.normal.textColor = old;
        }

        static string Clock(float sec)
        {
            bool neg = sec < 0f;
            int t = Mathf.CeilToInt(Mathf.Abs(sec));
            return (neg ? "-" : "") + (t / 60) + ":" + (t % 60).ToString("00");
        }

        void OnGUI()
        {
            if (train == null || route == null) return;
            InitStyles();
            // 900px 높이 기준으로 HUD 전체를 비례 축소/확대
            float scale = Mathf.Clamp(Screen.height / 900f, 0.55f, 2f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float W = Screen.width / scale, H = Screen.height / scale;

            DrawRouteInfo();
            DrawLimitSign(W);
            DrawMessage(W, H);

            bool inCab = driver != null && driver.InCab;
            if (inCab) DrawDashboard(W, H);
            if (route.CurrentPhase == RouteManager.Phase.Running && Mathf.Abs(route.DistanceToStop) < 160f && route.LegStarted)
                DrawStopGauge(W, H, inCab);

            if (driver != null)
            {
                if (driver.CanBoard) Prompt(W, H * 0.62f, "F", "기관차 탑승");
                else if (inCab && driver.CanExit) Prompt(W, H - 235f, "F", "하차");
            }

            if (help) DrawHelp(W, H, inCab);
            else Text(new Rect(W - 170, H - 26, 160, 22), "F1  조작법 보기", small, new Color(1, 1, 1, 0.6f));

            if (inCab) Crosshair(W, H);
        }

        void DrawRouteInfo()
        {
            var r = new Rect(16, 16, 330, 132);
            Box(r, Panel);
            float y = r.y + 8;
            Text(new Rect(r.x + 12, y, 310, 28), "다음 역  " + route.Next.name, title, Color.white); y += 32;

            if (route.CurrentPhase == RouteManager.Phase.Dwelling)
            {
                Text(new Rect(r.x + 12, y, 310, 24), "승객 승하차 중...  " + Mathf.CeilToInt(route.DwellLeft) + "초", label, Amber); y += 24;
            }
            else
            {
                float d = route.DistanceToStop;
                string ds = d >= 1000f ? (d / 1000f).ToString("F2") + " km" : Mathf.RoundToInt(d) + " m";
                Text(new Rect(r.x + 12, y, 310, 24), "정지 위치까지  " + ds, label, Color.white); y += 24;
            }
            Color tc = route.LegTimeLeft >= 0f ? Color.white : Bad;
            Text(new Rect(r.x + 12, y, 310, 24), "목표 시간  " + Clock(route.LegTimeLeft) + (route.LegStarted ? "" : "  (출발 시 시작)"), label, tc); y += 24;
            Text(new Rect(r.x + 12, y, 310, 24), "점수  " + route.Score + "     정차 " + route.StopsMade + "회", label, Amber);
        }

        void DrawLimitSign(float W)
        {
            int lim = route.CurrentLimit;
            var r = new Rect(W - 120, 16, 104, 104);
            Box(r, Panel);
            Box(new Rect(r.x + 12, r.y + 8, 80, 80), route.Overspeed ? Bad : new Color(0.85f, 0.15f, 0.12f));
            Box(new Rect(r.x + 19, r.y + 15, 66, 66), Color.white);
            Text(new Rect(r.x + 12, r.y + 8, 80, 80), lim.ToString(), big, Color.black);
            Text(new Rect(r.x, r.y + 84, 104, 18), "제한 km/h", center, Color.white);

            int next; float dist;
            if (route.track.FindNextLowerLimit(train.FrontS, 400f, out next, out dist))
            {
                var r2 = new Rect(W - 250, 20, 122, 52);
                Box(r2, Panel);
                Text(new Rect(r2.x, r2.y + 4, r2.width, 22), Mathf.RoundToInt(dist) + " m 앞", center, Amber);
                Text(new Rect(r2.x, r2.y + 24, r2.width, 24), "제한 " + next, center, Color.white);
            }
        }

        void DrawMessage(float W, float H)
        {
            if (string.IsNullOrEmpty(route.Message)) return;
            float age = route.MessageAge, dur = route.MessageDuration;
            if (age > dur) return;
            float a = Mathf.Clamp01((dur - age) / 0.6f) * Mathf.Clamp01(age / 0.15f);
            var c = route.MessageColor; c.a = a;
            var r = new Rect(W * 0.5f - 380, H * 0.16f, 760, 40);
            Box(r, new Color(0f, 0f, 0f, 0.5f * a));
            Text(r, route.Message, big, c);
        }

        void Prompt(float W, float y, string key, string what)
        {
            var r = new Rect(W * 0.5f - 110, y, 220, 40);
            Box(r, Panel);
            Box(new Rect(r.x + 10, r.y + 6, 28, 28), new Color(1f, 1f, 1f, 0.9f));
            Text(new Rect(r.x + 10, r.y + 6, 28, 28), key, keyStyle, Color.black);
            Text(new Rect(r.x + 46, r.y + 8, 170, 26), what, label, Color.white);
        }

        void Crosshair(float W, float H)
        {
            Box(new Rect(W * 0.5f - 2, H * 0.5f - 2, 4, 4), new Color(1, 1, 1, 0.5f));
        }

        // ───────────────────────── 기관실 계기판 ─────────────────────────

        void DrawDashboard(float W, float H)
        {
            float pw = 720, ph = 170;
            var r = new Rect(W * 0.5f - pw * 0.5f, H - ph - 14, pw, ph);
            Box(r, Panel);

            // 속도
            float kmh = train.SpeedKmh;
            int lim = route.CurrentLimit;
            Color sc = kmh > lim + 3f ? Bad : Color.white;
            Text(new Rect(r.x + 10, r.y + 10, 150, 56), Mathf.FloorToInt(kmh).ToString(), huge, sc);
            Text(new Rect(r.x + 162, r.y + 34, 60, 24), "km/h", label, new Color(1, 1, 1, 0.8f));
            if (train.Velocity < -0.05f) Text(new Rect(r.x + 162, r.y + 12, 60, 22), "후진", label, Amber);

            var bar = new Rect(r.x + 20, r.y + 74, 200, 12);
            Box(bar, new Color(1, 1, 1, 0.15f));
            Box(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(kmh / 100f), bar.height), sc);
            Box(new Rect(bar.x + bar.width * Mathf.Clamp01(lim / 100f) - 1, bar.y - 4, 3, bar.height + 8), new Color(0.9f, 0.2f, 0.15f));
            Text(new Rect(bar.x, bar.y + 14, 200, 18), "0          50         100", small, new Color(1, 1, 1, 0.5f));

            // 레버들
            float x0 = r.x + 250;
            Lever(new Rect(x0, r.y + 14, 120, 130), "레귤레이터", "W / S", train.Throttle, train.ThrottleActual, new Color(1f, 0.7f, 0.25f));
            Lever(new Rect(x0 + 130, r.y + 14, 120, 130), "브레이크", "E / Q", train.Emergency ? 1f : train.BrakeLever, train.BrakeActual, new Color(0.45f, 0.75f, 1f));

            // 리버서
            var rv = new Rect(x0 + 262, r.y + 14, 196, 130);
            Text(new Rect(rv.x, rv.y, rv.width, 22), "리버서  (A / D)", label, Color.white);
            string[] names = { "후진", "중립", "전진" };
            for (int i = 0; i < 3; i++)
            {
                bool on = train.Reverser == i - 1;
                var cell = new Rect(rv.x + i * 64, rv.y + 28, 60, 34);
                Box(cell, on ? (i == 1 ? new Color(0.7f, 0.7f, 0.7f) : Amber) : new Color(1, 1, 1, 0.12f));
                Text(cell, names[i], center, on ? Color.black : Color.white);
            }
            string status = train.Emergency ? "비상 제동!  (정차 후 Q로 해제)" : (train.headlight != null && train.headlight.enabled ? "전조등 켜짐 (L)" : "전조등 꺼짐 (L)");
            Text(new Rect(rv.x, rv.y + 72, rv.width, 22), status, small, train.Emergency ? Bad : new Color(1, 1, 1, 0.75f));
            Text(new Rect(rv.x, rv.y + 92, rv.width, 22), "H 기적   Space 비상제동", small, new Color(1, 1, 1, 0.75f));
            Text(new Rect(rv.x, rv.y + 110, rv.width, 22), "우클릭/C 창밖 보기", small, new Color(1, 1, 1, 0.75f));
        }

        void Lever(Rect r, string name, string keys, float lever, float actual, Color c)
        {
            Text(new Rect(r.x, r.y, r.width, 22), name, label, Color.white);
            var track = new Rect(r.x + 8, r.y + 28, 34, 92);
            Box(track, new Color(1, 1, 1, 0.12f));
            Box(new Rect(track.x, track.yMax - track.height * actual, track.width, track.height * actual), new Color(c.r, c.g, c.b, 0.85f));
            float ly = track.yMax - track.height * lever;
            Box(new Rect(track.x - 6, ly - 2, track.width + 12, 4), Color.white);
            Text(new Rect(r.x + 50, r.y + 50, 70, 28), Mathf.RoundToInt(lever * 100f) + "%", big, Color.white);
            Text(new Rect(r.x + 50, r.y + 80, 70, 22), keys, center, new Color(1, 1, 1, 0.6f));
        }

        // ───────────────────────── 정차 위치 게이지 ─────────────────────────

        void DrawStopGauge(float W, float H, bool inCab)
        {
            float d = route.DistanceToStop;      // + = 아직 앞
            float gw = 520, gy = inCab ? H - 245f : H - 110f;
            var r = new Rect(W * 0.5f - gw * 0.5f, gy, gw, 46);
            Box(r, Panel);
            float range = Mathf.Abs(d) > 30f ? 160f : 30f;
            float cx = r.x + gw * 0.5f;
            // 정지 목표선
            Box(new Rect(cx - 2, r.y + 4, 4, 38), Good);
            // 기관차 앞코 위치 (목표선 기준 왼쪽 = 아직 못 미침)
            float px = cx - Mathf.Clamp(d / range, -1f, 1f) * (gw * 0.5f - 12f);
            Box(new Rect(px - 6, r.y + 12, 12, 22), Mathf.Abs(d) <= route.stopWindow ? Good : Amber);
            for (int i = -4; i <= 4; i++) Box(new Rect(cx + i * (gw * 0.5f - 12f) / 4f, r.y + 38, 1, 6), new Color(1, 1, 1, 0.4f));
            string t = d >= 0f ? "정지 위치까지 " + d.ToString(Mathf.Abs(d) < 10f ? "F1" : "F0") + " m" : (-d).ToString("F1") + " m 지나침";
            Text(new Rect(r.x, r.y - 22, gw, 20), t + "   (눈금 " + Mathf.RoundToInt(range / 4f) + " m)", center, Color.white);
        }

        // ───────────────────────── 도움말 ─────────────────────────

        void DrawHelp(float W, float H, bool inCab)
        {
            var r = new Rect(16, 160, 330, inCab ? 310 : 210);
            Box(r, Panel);
            float y = r.y + 8;
            Text(new Rect(r.x + 12, y, 300, 24), inCab ? "기관실 조작 (1인칭)" : "기관사 조작 (3인칭)", label, Amber); y += 28;
            string[] lines = inCab
                ? new[] {
                    "W / S   레귤레이터 열기 / 닫기 (가속)",
                    "A / D   리버서 후진 ◀ 중립 ▶ 전진",
                    "E / Q   브레이크 걸기 / 풀기",
                    "Space   비상 제동",
                    "H       기적 (누르는 동안)",
                    "L       전조등",
                    "마우스  둘러보기",
                    "우클릭 / C  창밖으로 내다보기",
                    "F       하차 (정차 중)",
                    "",
                    "증기압과 공기압이 늦게 따라옵니다.",
                    "미리 감속해서 정지선에 맞춰 세우세요!" }
                : new[] {
                    "W A S D  이동      Shift  달리기",
                    "Space    점프      마우스  시점 / 휠 줌",
                    "F        기관차 탑승 (기관실 출입구 근처)",
                    "Esc      마우스 커서 풀기",
                    "",
                    "역에서 정지선에 정확히 멈추면 고득점!",
                    "제한속도를 지키고 목표 시간 안에 도착하세요." };
            foreach (var l in lines) { Text(new Rect(r.x + 12, y, 310, 20), l, small, Color.white); y += 20; }
            Text(new Rect(r.x + 12, r.yMax - 22, 300, 20), "F1  도움말 닫기", small, new Color(1, 1, 1, 0.55f));
        }
    }
}
