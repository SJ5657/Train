using UnityEngine;

namespace TrainGame
{
    /// <summary>
    /// 절차적 열차 사운드: 증기 배기(칙칙), 레일 이음매(덜컹), 주행 럼블, 기적, 브레이크 끽-, 증기 쉭.
    /// 외부 오디오 에셋 없이 시작할 때 클립을 합성한다.
    /// </summary>
    public class TrainAudio : MonoBehaviour
    {
        public TrainController train;
        [Tooltip("레일 이음매 간격 (m)")] public float jointSpacing = 12.5f;
        public float masterVolume = 0.9f;

        const int Rate = 44100;
        AudioClip chuffClip, clackClip, rumbleClip, whistleClip, squealClip, hissClip;
        AudioSource oneShot, rumble, whistle, squeal, hiss;
        int[] lastJoint;
        float whistleVol;
        System.Random rng = new System.Random(7);

        void Start()
        {
            if (train == null) train = GetComponentInParent<TrainController>();
            if (train == null) train = TrainController.Instance;

            chuffClip = MakeChuff();
            clackClip = MakeClack();
            rumbleClip = MakeLoop("Rumble", 2f, Brown);
            whistleClip = MakeWhistle();
            squealClip = MakeLoop("Squeal", 1f, t => 0.5f * Mathf.Sin(2f * Mathf.PI * 2600f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * 3900f * t));
            hissClip = MakeLoop("Hiss", 1.5f, HighNoise);

            var host = train != null ? train.Loco : transform;
            oneShot = NewSource(host, null, false, 0.55f);
            rumble = NewSource(host, rumbleClip, true, 0.4f);
            whistle = NewSource(host, whistleClip, true, 0.35f);
            squeal = NewSource(host, squealClip, true, 0.5f);
            hiss = NewSource(host, hissClip, true, 0.5f);

            if (train != null)
            {
                train.Chuff += OnChuff;
                lastJoint = new int[train.vehicles.Length * 2];
                for (int i = 0; i < lastJoint.Length; i++) lastJoint[i] = JointIndex(i);
            }
        }

        void OnDestroy()
        {
            if (train != null) train.Chuff -= OnChuff;
        }

        AudioSource NewSource(Transform host, AudioClip clip, bool loop, float spatial)
        {
            var src = host.gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = loop;
            src.playOnAwake = false;
            src.spatialBlend = spatial;
            src.minDistance = 8f;
            src.maxDistance = 400f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.dopplerLevel = 0.3f;
            src.volume = 0f;
            if (loop && clip != null) { src.time = (float)rng.NextDouble() * clip.length * 0.9f; src.Play(); }
            return src;
        }

        void OnChuff(float strength)
        {
            float v = Mathf.Lerp(0.12f, 1f, strength) * masterVolume;
            float spd = train.SpeedKmh;
            oneShot.pitch = Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble()) * (1f + spd * 0.002f);
            oneShot.PlayOneShot(chuffClip, v * 0.9f);
        }

        int JointIndex(int bogie)
        {
            var veh = train.vehicles[bogie / 2];
            float sb = train.s - veh.offset + (bogie % 2 == 0 ? veh.halfBase : -veh.halfBase);
            return Mathf.FloorToInt(train.track.Wrap(sb) / jointSpacing);
        }

        void Update()
        {
            if (train == null || train.track == null) return;
            float dt = Time.deltaTime;
            float spd = Mathf.Abs(train.Velocity);
            bool moving = spd > 0.2f;

            // 레일 이음매: 대차가 이음매를 지날 때마다 덜컹
            for (int i = 0; i < lastJoint.Length; i++)
            {
                int j = JointIndex(i);
                if (j != lastJoint[i])
                {
                    lastJoint[i] = j;
                    if (moving)
                    {
                        float vol = Mathf.Clamp01(0.25f + spd / 25f) * (i < 2 ? 0.7f : 0.45f) * masterVolume;
                        oneShot.PlayOneShot(clackClip, vol);
                    }
                }
            }

            rumble.volume = Mathf.Clamp01(spd / 18f) * 0.55f * masterVolume;
            rumble.pitch = 0.7f + Mathf.Clamp01(spd / 25f) * 0.6f;

            whistleVol = Mathf.MoveTowards(whistleVol, train.Whistle ? 1f : 0f, dt * (train.Whistle ? 6f : 3f));
            whistle.volume = whistleVol * 0.55f * masterVolume;
            whistle.pitch = 0.97f + whistleVol * 0.03f;

            float sq = train.BrakeActual > 0.25f && spd > 0.3f && spd < 7f ? (train.BrakeActual - 0.25f) * Mathf.Clamp01((7f - spd) / 4f) : 0f;
            squeal.volume = Mathf.MoveTowards(squeal.volume, sq * 0.22f * masterVolume, dt * 0.8f);

            float hs = 0.04f + train.ThrottleActual * (spd < 3f ? 0.35f : 0.12f) + (train.Emergency ? 0.25f : 0f);
            hiss.volume = Mathf.MoveTowards(hiss.volume, hs * masterVolume, dt * 0.6f);
        }

        // ───────────────────────── 합성 ─────────────────────────

        float Noise() { return (float)(rng.NextDouble() * 2.0 - 1.0); }

        float brownState;
        float Brown(float t)
        {
            brownState = Mathf.Clamp(brownState + Noise() * 0.06f, -1f, 1f) * 0.995f;
            return brownState * 0.9f + Mathf.Sin(2f * Mathf.PI * 46f * t) * 0.15f;
        }

        float hpPrev, hpOut;
        float HighNoise(float t)
        {
            float x = Noise();
            hpOut = 0.85f * (hpOut + x - hpPrev);
            hpPrev = x;
            return hpOut * 0.5f;
        }

        AudioClip MakeLoop(string name, float seconds, System.Func<float, float> f)
        {
            int n = Mathf.RoundToInt(Rate * seconds);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = f(i / (float)Rate);
            // 이음새 크로스페이드
            int fade = Rate / 20;
            for (int i = 0; i < fade; i++)
            {
                float a = i / (float)fade;
                data[i] = data[i] * a + data[n - fade + i] * (1f - a);
            }
            var clip = AudioClip.Create(name, n - fade, 1, Rate, false);
            var trimmed = new float[n - fade];
            System.Array.Copy(data, trimmed, n - fade);
            clip.SetData(trimmed, 0);
            return clip;
        }

        AudioClip MakeChuff()
        {
            int n = (int)(Rate * 0.34f);
            var d = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, t / 0.012f) * Mathf.Exp(-t * 11f);
                lp += (Noise() - lp) * 0.32f;
                float thump = Mathf.Sin(2f * Mathf.PI * 62f * t) * Mathf.Exp(-t * 24f);
                d[i] = (lp * 1.6f + thump * 0.7f) * env;
            }
            var c = AudioClip.Create("Chuff", n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        AudioClip MakeClack()
        {
            int n = (int)(Rate * 0.12f);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t * 45f);
                d[i] = (Noise() * 0.5f + Mathf.Sin(2f * Mathf.PI * 1850f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 140f * t) * 0.6f) * env;
            }
            var c = AudioClip.Create("Clack", n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        AudioClip MakeWhistle()
        {
            // 1초 루프에 정수 주기가 들어가는 화음 (Bb 장3화음 근처)
            float[] f = { 466f, 587f, 698f, 932f };
            float[] a = { 0.42f, 0.32f, 0.26f, 0.08f };
            float lp = 0f;
            return MakeLoop("Whistle", 1.05f, t =>
            {
                float v = 0f;
                for (int k = 0; k < f.Length; k++) v += a[k] * Mathf.Sin(2f * Mathf.PI * f[k] * t * (1f + 0.003f * Mathf.Sin(2f * Mathf.PI * 5f * t)));
                lp += (Noise() - lp) * 0.5f;
                return v * 0.8f + lp * 0.12f;
            });
        }
    }
}
