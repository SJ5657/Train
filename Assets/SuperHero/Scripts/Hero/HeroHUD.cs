using UnityEngine;

namespace SuperHero
{
    /// <summary>IMGUI 기반 HUD: 조준점, 스킬 쿨다운, 에너지, 속도/고도, 조작 도움말(H), 도시 생성 진행률.</summary>
    public class HeroHUD : MonoBehaviour
    {
        HeroController hero;
        HeroSkills skills;
        GUIStyle label, small, big, keyStyle;
        bool help = true;
        float helpHideAt;

        void Start()
        {
            hero = GetComponent<HeroController>();
            skills = GetComponent<HeroSkills>();
            helpHideAt = Time.time + 25f;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.H)) { help = !help; helpHideAt = Time.time + 9999f; }
            else if (help && Time.time > helpHideAt) help = false;
        }

        void InitStyles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };
            small = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = new Color(1, 1, 1, 0.92f) } };
            big = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            keyStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        }

        static void Box(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void OnGUI()
        {
            InitStyles();
            float W = Screen.width, H = Screen.height;

            if (!SuperCityGenerator.Ready)
            {
                Box(new Rect(0, 0, W, H), new Color(0.05f, 0.07f, 0.12f, 0.94f));
                GUI.Label(new Rect(0, H * 0.42f, W, 40), "CITY LOADING...  도시 생성 중", big);
                float p = SuperCityGenerator.Progress;
                Box(new Rect(W * 0.3f, H * 0.52f, W * 0.4f, 10), new Color(1, 1, 1, 0.2f));
                Box(new Rect(W * 0.3f, H * 0.52f, W * 0.4f * p, 10), new Color(1f, 0.8f, 0.2f, 1f));
                return;
            }
            if (hero == null) return;

            // 조준점
            float cx = W * 0.5f, cy = H * 0.5f;
            Color cc = skills != null && skills.HeatOn ? new Color(1f, 0.3f, 0.2f, 0.95f) : new Color(1f, 1f, 1f, 0.85f);
            Box(new Rect(cx - 1, cy - 9, 2, 5), cc); Box(new Rect(cx - 1, cy + 4, 2, 5), cc);
            Box(new Rect(cx - 9, cy - 1, 5, 2), cc); Box(new Rect(cx + 4, cy - 1, 5, 2), cc);
            Box(new Rect(cx - 1, cy - 1, 2, 2), cc);

            // 상태 (좌상단)
            Box(new Rect(14, 14, 232, 78), new Color(0, 0, 0, 0.38f));
            string mode = hero.Driven ? "SLAM!" : hero.IsFlying ? (hero.Boosting ? "SUPERSONIC FLIGHT" : "FLYING") : (hero.Grounded ? "GROUND" : "AIRBORNE");
            GUI.Label(new Rect(24, 18, 220, 24), mode, label);
            float kmh = hero.Speed * 3.6f;
            float alt = hero.transform.position.y;
            GUI.Label(new Rect(24, 40, 220, 22), string.Format("속도 {0:0} km/h   고도 {1:0} m", kmh, alt), small);
            GUI.Label(new Rect(24, 62, 220, 22), string.Format("건물 {0}   파편 {1}", DestructibleBuilding.All.Count, DestructionManager.Instance.ActiveCount), small);

            // 에너지 (하단)
            if (skills != null)
            {
                float bw = 380f, bx = (W - bw) * 0.5f, by = H - 112f;
                Box(new Rect(bx - 2, by - 2, bw + 4, 14), new Color(0, 0, 0, 0.55f));
                float e = skills.energy / skills.maxEnergy;
                Box(new Rect(bx, by, bw * e, 10), Color.Lerp(new Color(1f, 0.3f, 0.2f), new Color(0.3f, 0.85f, 1f), e));

                // 스킬 슬롯
                string[] keys = { "LMB", "RMB", "E", "Q", "R", "T" };
                string[] names = { "Punch", "Heat Vision", "Blast", "Slam", "Shockwave", "Meteors" };
                float[] cds = { skills.PunchCd01, skills.HeatOn ? 0f : (skills.energy < 8f ? 1f : 0f), skills.BlastCd01, skills.SlamCd01, skills.WaveCd01, skills.MeteorCd01 };
                float sw = 74f, gap = 8f, total = keys.Length * sw + (keys.Length - 1) * gap;
                float sx = (W - total) * 0.5f, sy = H - 92f;
                for (int i = 0; i < keys.Length; i++)
                {
                    var r = new Rect(sx + i * (sw + gap), sy, sw, 64f);
                    bool active = (i == 1 && skills.HeatOn) || (i == 2 && skills.Charge01 > 0f);
                    Box(r, active ? new Color(1f, 0.6f, 0.15f, 0.85f) : new Color(0.05f, 0.08f, 0.14f, 0.7f));
                    if (cds[i] > 0f) Box(new Rect(r.x, r.y + r.height * (1f - cds[i]), r.width, r.height * cds[i]), new Color(0f, 0f, 0f, 0.55f));
                    GUI.Label(new Rect(r.x, r.y + 6, r.width, 24), keys[i], keyStyle);
                    GUI.Label(new Rect(r.x, r.y + 34, r.width, 22), names[i], new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 12 });
                }
                if (skills.Charge01 > 0f)
                    Box(new Rect(cx - 60, cy + 40, 120 * skills.Charge01, 6), new Color(1f, 0.7f, 0.2f, 0.95f));
            }

            // 도움말
            if (help)
            {
                var r = new Rect(14, 104, 330, 314);
                Box(r, new Color(0, 0, 0, 0.5f));
                string t =
                    "[ 이동 ]\n" +
                    "WASD 이동   Shift 전력질주 / 부스트\n" +
                    "Space 점프 (공중에서 한 번 더 = 비행)\n" +
                    "F 비행 켜기/끄기   Space↑ Ctrl↓ 상승/하강\n" +
                    "비행 중 W = 카메라가 보는 쪽으로 이동\n\n" +
                    "[ 공격 / 스킬 ]\n" +
                    "좌클릭 슈퍼 펀치   우클릭(꾹) 히트 비전\n" +
                    "E (꾹) 에너지 블래스트 충전 → 떼면 발사\n" +
                    "Q 히어로 슬램 (지상: 점프 후 내려찍기 / 공중: 급강하)\n" +
                    "R 충격파   T 운석 비 (조준한 곳)\n" +
                    "부스트 비행으로 건물에 돌진하면 관통!\n\n" +
                    "마우스 시점   휠 거리   Esc 커서 해제   H 도움말\n" +
                    "Backspace 시작 지점으로 복귀";
                GUI.Label(new Rect(r.x + 10, r.y + 8, r.width - 16, r.height - 12), t, small);
            }
        }
    }
}
