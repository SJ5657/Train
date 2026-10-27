using UnityEngine;

namespace TrainGame
{
    /// <summary>
    /// 증기기관차 + 객차를 선로 위에서 움직이는 1차원 열차 물리.
    /// 레귤레이터(스로틀)·리버서·자동 공기 브레이크·비상 제동. 증기압/공기압 지연이 있어 레버가 즉시 반영되지 않는다.
    /// 기관사가 기관실에 있을 때(Driving)만 키 입력을 받는다.
    /// </summary>
    public class TrainController : MonoBehaviour
    {
        public static TrainController Instance { get; private set; }

        [System.Serializable]
        public class Vehicle
        {
            public Transform root;
            [Tooltip("기관차 중심에서 뒤로 떨어진 거리 (m)")] public float offset;
            [Tooltip("앞/뒤 대차 간격의 절반 (차체 방향 계산용)")] public float halfBase = 3f;
            [Tooltip("선로 바닥에서 차체 원점까지 높이")] public float height = 0.6f;
            [System.NonSerialized] public Transform[] wheels;
            [System.NonSerialized] public float[] wheelRadius, wheelAngle;
            [System.NonSerialized] public Quaternion[] wheelBase;
        }

        public TrackPath track;
        public Vehicle[] vehicles;
        [Tooltip("기관차 중심의 선로 위치 (m)")] public float s;
        [Tooltip("기관차 중심 → 앞코 거리")] public float frontOffset = 5.6f;
        [Tooltip("기관실 1인칭 시점 기준점 (기관차 자식, +Z가 진행방향)")] public Transform cabAnchor;
        public Light headlight;

        [Header("Physics")]
        public float massKg = 150000f;
        public float maxTractiveForce = 95000f;   // N
        public float maxPower = 650000f;          // W
        public float rollingCoef = 0.0022f;
        public float dragCoef = 38f;              // N/(m/s)^2
        public float maxBrakeDecel = 1.25f;       // m/s^2 (상용 최대)
        public float emergencyDecel = 1.7f;
        [Tooltip("레귤레이터를 열었을 때 실린더 증기압이 따라오는 속도 (/s)")] public float steamResponse = 0.55f;
        [Tooltip("브레이크 공기압이 따라오는 속도 (/s)")] public float brakeResponse = 0.6f;

        [Header("Controls")]
        public float throttleRate = 0.45f;
        public float brakeRate = 0.7f;

        public bool Driving { get; set; }
        public float Throttle { get; private set; }
        public float ThrottleActual { get; private set; }
        public int Reverser { get; private set; }
        public float BrakeLever { get; private set; }
        public float BrakeActual { get; private set; }
        public bool Emergency { get; private set; }
        public bool Whistle { get; private set; }
        public float Velocity { get; private set; }
        public float SpeedKmh { get { return Mathf.Abs(Velocity) * 3.6f; } }
        public bool Stopped { get { return Mathf.Abs(Velocity) < 0.03f; } }
        public float FrontS { get { return track.Wrap(s + frontOffset); } }
        public float Odometer { get; private set; }
        /// <summary>곡선 횡가속도 (m/s^2, 부호=왼쪽).</summary>
        public float LateralAccel { get; private set; }
        public Transform Loco { get { return vehicles[0].root; } }

        /// <summary>실린더 배기 한 번(동륜 1/4회전). 인자 = 세기 0..1.</summary>
        public event System.Action<float> Chuff;

        float chuffPhase;
        float roll;

        void Awake()
        {
            Instance = this;
            BrakeLever = 1f;
            BrakeActual = 1f;
            foreach (var v in vehicles) CollectWheels(v);
        }

        void Start()
        {
            if (track == null) track = TrackPath.Instance;
            PlaceVehicles(0f);
        }

        static void CollectWheels(Vehicle v)
        {
            // 에셋의 WheelRotation(고정 속도 회전)은 끄고 속도에 맞춰 직접 돌린다
            var rots = v.root.GetComponentsInChildren<WheelRotation>(true);
            v.wheels = new Transform[rots.Length];
            v.wheelRadius = new float[rots.Length];
            v.wheelAngle = new float[rots.Length];
            v.wheelBase = new Quaternion[rots.Length];
            for (int i = 0; i < rots.Length; i++)
            {
                var t = rots[i].transform;
                v.wheels[i] = t;
                var mf = t.GetComponent<MeshFilter>();
                v.wheelRadius[i] = mf != null && mf.sharedMesh != null ? Mathf.Max(0.1f, mf.sharedMesh.bounds.size.y * 0.5f) : 0.35f;
                v.wheelBase[i] = t.localRotation;
                rots[i].enabled = false;
                Destroy(rots[i]);
            }
        }

        // ───────────────────────── 업데이트 ─────────────────────────

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) return;

            if (Driving) HandleInput(dt);
            else { Whistle = false; Throttle = Mathf.MoveTowards(Throttle, 0f, dt); }

            Simulate(dt);
            PlaceVehicles(dt);
        }

        void HandleInput(float dt)
        {
            if (Input.GetKey(KeyCode.W)) Throttle = Mathf.Clamp01(Throttle + throttleRate * dt);
            if (Input.GetKey(KeyCode.S)) Throttle = Mathf.Clamp01(Throttle - throttleRate * dt);
            if (Input.GetKeyDown(KeyCode.D)) Reverser = Mathf.Min(1, Reverser + 1);
            if (Input.GetKeyDown(KeyCode.A)) Reverser = Mathf.Max(-1, Reverser - 1);
            if (Input.GetKey(KeyCode.E)) BrakeLever = Mathf.Clamp01(BrakeLever + brakeRate * dt);
            if (Input.GetKey(KeyCode.Q))
            {
                BrakeLever = Mathf.Clamp01(BrakeLever - brakeRate * dt);
                if (Emergency && Stopped) Emergency = false;
            }
            if (Input.GetKeyDown(KeyCode.Space)) SetEmergency(!Emergency);
            if (Input.GetKeyDown(KeyCode.L) && headlight != null) headlight.enabled = !headlight.enabled;
            Whistle = Input.GetKey(KeyCode.H);
        }

        public void SetEmergency(bool on)
        {
            Emergency = on;
            if (on) { Throttle = 0f; BrakeLever = 1f; }
        }

        /// <summary>기관사가 내릴 때: 레귤레이터 닫고 브레이크 체결, 리버서 중립.</summary>
        public void Secure()
        {
            Throttle = 0f;
            BrakeLever = 1f;
            Reverser = 0;
            Whistle = false;
        }

        void Simulate(float dt)
        {
            ThrottleActual = Mathf.MoveTowards(ThrottleActual, Throttle, steamResponse * dt);
            float brakeTarget = Emergency ? 1f : BrakeLever;
            BrakeActual = Mathf.MoveTowards(BrakeActual, brakeTarget, brakeResponse * (brakeTarget > BrakeActual ? 1.4f : 1f) * dt);

            float v = Velocity, av = Mathf.Abs(v);
            float tractive = ThrottleActual * Reverser * Mathf.Min(maxTractiveForce, maxPower / Mathf.Max(av, 0.5f));
            float vPre = v + tractive / massKg * dt;

            float resist = rollingCoef * 9.81f + dragCoef * v * v / massKg
                         + BrakeActual * (Emergency ? emergencyDecel : maxBrakeDecel);
            float dv = resist * dt;
            v = Mathf.Abs(vPre) <= dv ? 0f : vPre - Mathf.Sign(vPre) * dv;

            Velocity = v;
            s = track.Wrap(s + v * dt);
            Odometer += Mathf.Abs(v) * dt;
        }

        void PlaceVehicles(float dt)
        {
            if (track == null || vehicles == null) return;

            // 곡선 횡가속도 → 차체 기울기 (살짝 바깥으로)
            Vector3 ta = track.GetTangent(s - 6f), tb = track.GetTangent(s + 6f);
            float k = Vector3.SignedAngle(ta, tb, Vector3.up) * Mathf.Deg2Rad / 12f;   // 곡률 (+ = 오른쪽 회전)
            LateralAccel = -Velocity * Velocity * k;
            float targetRoll = Mathf.Clamp(Velocity * Velocity * k * 0.9f, -2.5f, 2.5f);
            roll = dt > 0f ? Mathf.Lerp(roll, targetRoll, 1f - Mathf.Exp(-3f * dt)) : targetRoll;

            float dist = Velocity * dt;
            for (int i = 0; i < vehicles.Length; i++)
            {
                var veh = vehicles[i];
                if (veh.root == null) continue;
                float c = s - veh.offset;
                Vector3 pf = track.GetPosition(c + veh.halfBase), pr = track.GetPosition(c - veh.halfBase);
                Vector3 fwd = (pf - pr).normalized;
                Vector3 left = Vector3.Cross(fwd, Vector3.up).normalized;
                Quaternion rot = Quaternion.AngleAxis(roll, fwd) * Quaternion.LookRotation(left, Vector3.up);
                veh.root.SetPositionAndRotation((pf + pr) * 0.5f + Vector3.up * veh.height, rot);

                if (veh.wheels == null) continue;
                for (int w = 0; w < veh.wheels.Length; w++)
                {
                    veh.wheelAngle[w] = (veh.wheelAngle[w] + dist / veh.wheelRadius[w] * Mathf.Rad2Deg) % 360f;
                    veh.wheels[w].localRotation = veh.wheelBase[w] * Quaternion.AngleAxis(veh.wheelAngle[w], Vector3.forward);
                }
            }

            // 동륜(반경 0.47m) 1/4 회전마다 배기 (chuff)
            chuffPhase += Mathf.Abs(dist) / (0.47f * Mathf.PI * 0.5f);
            if (chuffPhase >= 1f)
            {
                chuffPhase -= Mathf.Floor(chuffPhase);
                if (Chuff != null) Chuff(Mathf.Clamp01(0.15f + ThrottleActual * Mathf.Abs(Reverser)));
            }
        }
    }
}
