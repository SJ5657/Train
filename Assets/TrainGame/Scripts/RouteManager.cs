using UnityEngine;

namespace TrainGame
{
    [System.Serializable]
    public class Station
    {
        public string name;
        [Tooltip("기관차 앞코가 멈춰야 하는 선로 위치 (m)")] public float stopS;
    }

    /// <summary>
    /// 운행 규칙: 다음 역 정차 위치 판정(오차별 점수), 승하차 대기, 구간 목표 시간, 제한속도 위반 감점.
    /// </summary>
    public class RouteManager : MonoBehaviour
    {
        public static RouteManager Instance { get; private set; }

        public TrainController train;
        public TrackPath track;
        public Station[] stations;
        public int nextIndex = 1;
        public float dwellTime = 8f;
        [Tooltip("구간 목표 시간 계산용 평균 속도 (m/s)")] public float scheduleSpeed = 10.5f;
        [Tooltip("정차로 인정하는 정지 위치 오차 (m)")] public float stopWindow = 12f;

        public enum Phase { Running, Dwelling }
        public Phase CurrentPhase { get; private set; }
        public int Score { get { return Mathf.RoundToInt(score); } }
        public float LegTimeLeft { get; private set; }
        public bool LegStarted { get; private set; }
        public float DwellLeft { get; private set; }
        public int StopsMade { get; private set; }
        public bool Overspeed { get; private set; }
        public Station Next { get { return stations[nextIndex]; } }
        public Station Previous { get { return stations[(nextIndex - 1 + stations.Length) % stations.Length]; } }
        /// <summary>앞코 → 정지 위치 (양수 = 아직 앞에 있음).</summary>
        public float DistanceToStop { get { return track.Delta(train.FrontS, Next.stopS); } }
        public int CurrentLimit { get { return track.GetLimit(train.FrontS); } }

        public string Message { get; private set; }
        public Color MessageColor { get; private set; }
        public float MessageAge { get { return Time.time - messageTime; } }
        public float MessageDuration { get; private set; }

        float score;
        float departOdo;
        float messageTime = -99f;
        float hintCooldown;

        void Awake() { Instance = this; }

        void Start()
        {
            if (train == null) train = TrainController.Instance;
            if (track == null) track = TrackPath.Instance;
            CurrentPhase = Phase.Running;
            LegTimeLeft = LegTarget(Previous, Next);
            departOdo = train.Odometer;
            Show("기관차 옆으로 가서 F 를 눌러 탑승하세요", new Color(1f, 0.92f, 0.6f), 8f);
        }

        float LegTarget(Station from, Station to)
        {
            float d = track.Wrap(to.stopS - from.stopS);
            return Mathf.Round(d / scheduleSpeed + 25f);
        }

        public void Show(string msg, Color color, float duration = 4f)
        {
            Message = msg;
            MessageColor = color;
            MessageDuration = duration;
            messageTime = Time.time;
        }

        void Update()
        {
            if (train == null || track == null || stations == null || stations.Length < 2) return;
            float dt = Time.deltaTime;
            hintCooldown -= dt;

            // 제한속도
            Overspeed = train.SpeedKmh > CurrentLimit + 3f;
            if (Overspeed && train.Driving)
            {
                score -= 3f * dt;
                if (hintCooldown <= 0f) { Show("제한속도 초과! 감속하세요 (제한 " + CurrentLimit + " km/h)", new Color(1f, 0.4f, 0.3f), 2.5f); hintCooldown = 3f; }
            }

            if (CurrentPhase == Phase.Dwelling) UpdateDwell(dt);
            else UpdateRunning(dt);
        }

        void UpdateRunning(float dt)
        {
            if (!LegStarted && train.SpeedKmh > 0.5f) LegStarted = true;
            if (LegStarted) LegTimeLeft -= dt;

            float d = DistanceToStop;
            bool travelled = train.Odometer - departOdo > 100f;
            if (!travelled) return;

            if (train.Stopped && Mathf.Abs(d) <= stopWindow)
            {
                Arrive(d);
            }
            else if (train.Stopped && d > stopWindow && d < 90f && hintCooldown <= 0f)
            {
                Show("정지 위치까지 " + Mathf.CeilToInt(d) + " m 더 전진하세요", new Color(1f, 0.9f, 0.5f), 2.5f);
                hintCooldown = 4f;
            }
            else if (train.Stopped && d < -stopWindow && d > -60f && hintCooldown <= 0f)
            {
                Show("정지 위치를 " + Mathf.CeilToInt(-d) + " m 지나쳤습니다. 리버서를 후진으로 놓고 조정하세요", new Color(1f, 0.7f, 0.4f), 3f);
                hintCooldown = 4f;
            }
            else if (d < -80f && !train.Stopped)
            {
                score -= 60f;
                Show(Next.name + " 을(를) 통과했습니다! -60", new Color(1f, 0.35f, 0.3f), 4f);
                Advance();
            }
        }

        void Arrive(float d)
        {
            float err = Mathf.Abs(d);
            string grade; int pts; Color c;
            if (err <= 0.5f) { grade = "완벽!"; pts = 150; c = new Color(0.5f, 1f, 0.6f); }
            else if (err <= 1.5f) { grade = "훌륭해요"; pts = 100; c = new Color(0.6f, 1f, 0.6f); }
            else if (err <= 4f) { grade = "좋아요"; pts = 70; c = new Color(0.8f, 1f, 0.6f); }
            else if (err <= 8f) { grade = "보통"; pts = 40; c = new Color(1f, 0.95f, 0.6f); }
            else { grade = "아쉬워요"; pts = 15; c = new Color(1f, 0.8f, 0.5f); }

            int timeBonus;
            string timeText;
            if (LegTimeLeft >= 0f) { timeBonus = 30; timeText = "정시 도착 +30"; }
            else { timeBonus = -Mathf.Min(60, Mathf.CeilToInt(-LegTimeLeft * 0.5f)); timeText = Mathf.CeilToInt(-LegTimeLeft) + "초 지연 " + timeBonus; }

            score += pts + timeBonus;
            StopsMade++;
            Show(Next.name + " 도착 — " + grade + "  오차 " + err.ToString("F1") + " m  (+" + pts + ")   " + timeText, c, 6f);
            CurrentPhase = Phase.Dwelling;
            DwellLeft = dwellTime;
        }

        void UpdateDwell(float dt)
        {
            if (!train.Stopped)
            {
                score -= 40f;
                Show("승하차가 끝나기 전에 출발했습니다! -40", new Color(1f, 0.4f, 0.3f), 4f);
                Advance();
                return;
            }
            DwellLeft -= dt;
            if (DwellLeft <= 0f)
            {
                Advance();
                Show("승하차 완료 — 출발 진행! 다음 역: " + Next.name, new Color(0.6f, 0.9f, 1f), 5f);
            }
        }

        void Advance()
        {
            nextIndex = (nextIndex + 1) % stations.Length;
            CurrentPhase = Phase.Running;
            LegStarted = false;
            LegTimeLeft = LegTarget(Previous, Next);
            departOdo = train.Odometer;
        }
    }
}
