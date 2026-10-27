using UnityEngine;

namespace SuperHero
{
    /// <summary>외부 사운드 파일 없이 코드로 합성한 효과음: 폭발, 붕괴 소리, 타격, 충격파, 비행 바람, 히트 비전 빔.</summary>
    public class Sfx : MonoBehaviour
    {
        static Sfx inst;

        public static Sfx Instance
        {
            get
            {
                if (inst == null) inst = new GameObject("SFX").AddComponent<Sfx>();
                return inst;
            }
        }

        const int Rate = 22050;
        AudioClip boom, crumble, thump, zap, whoosh, hum;
        AudioSource[] pool;
        int next;
        AudioSource wind, beam;
        float lastCrumble, lastBoom;

        void Awake()
        {
            inst = this;
            boom = Make("boom", 1.8f, false, (t, n, s) =>
            {
                float env = Mathf.Exp(-t * 2.8f);
                s.lp += (n - s.lp) * 0.10f;
                float body = Mathf.Sin(2f * Mathf.PI * (62f - 22f * Mathf.Min(t, 1f)) * t) * 0.9f;
                return (s.lp * 1.6f + body) * env + n * Mathf.Exp(-t * 45f) * 0.6f;
            });
            crumble = Make("crumble", 2.4f, false, (t, n, s) =>
            {
                s.lp += (n - s.lp) * 0.22f;
                float mod = (Mathf.Sin(t * 9f) + Mathf.Sin(t * 17.3f + 1f) + 2.2f) * 0.25f;
                return s.lp * 1.7f * mod * Mathf.Exp(-t * 1.5f) * Mathf.Min(1f, t * 25f);
            });
            thump = Make("thump", 0.4f, false, (t, n, s) =>
            {
                s.lp += (n - s.lp) * 0.3f;
                return (Mathf.Sin(2f * Mathf.PI * (95f - 120f * t) * t) * 1.1f + s.lp * 0.8f) * Mathf.Exp(-t * 11f);
            });
            zap = Make("zap", 1.3f, false, (t, n, s) =>
            {
                float f = 1100f * Mathf.Exp(-t * 4.2f) + 50f;
                s.ph += 2f * Mathf.PI * f / Rate;
                return (Mathf.Sin(s.ph) * 0.8f + n * 0.12f) * Mathf.Exp(-t * 2.4f);
            });
            whoosh = Make("whoosh", 1.5f, true, (t, n, s) =>
            {
                s.lp += (n - s.lp) * 0.07f;
                s.lp2 += (n - s.lp2) * 0.012f;
                float fade = Mathf.Min(1f, Mathf.Min(t, 1.5f - t) * 20f);
                return (s.lp - s.lp2) * 2.2f * fade;
            });
            hum = Make("hum", 1f, true, (t, n, s) =>
                (Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 331f * t) * 0.3f + Mathf.Sin(2f * Mathf.PI * 441f * t) * 0.2f) * 0.5f);

            pool = new AudioSource[10];
            for (int i = 0; i < pool.Length; i++) pool[i] = NewSource(false);
            wind = NewSource(true); wind.clip = whoosh; wind.loop = true; wind.volume = 0f; wind.spatialBlend = 0f; wind.Play();
            beam = NewSource(true); beam.clip = hum; beam.loop = true; beam.volume = 0f; beam.Play();
        }

        class State { public float lp, lp2, ph; }

        delegate float Gen(float t, float noise, State s);

        static AudioClip Make(string clipName, float seconds, bool loop, Gen gen)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            var s = new State();
            var rnd = new System.Random(clipName.GetHashCode());
            for (int i = 0; i < n; i++)
            {
                float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
                data[i] = Mathf.Clamp(gen(i / (float)Rate, noise, s), -1f, 1f);
            }
            var c = AudioClip.Create(clipName, n, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        AudioSource NewSource(bool is3D)
        {
            var go = new GameObject("src");
            go.transform.SetParent(transform, false);
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Logarithmic;
            a.minDistance = 12f; a.maxDistance = 1400f;
            a.dopplerLevel = 0f;
            return a;
        }

        void PlayAt(AudioClip clip, Vector3 pos, float vol, float pitch)
        {
            var a = pool[next]; next = (next + 1) % pool.Length;
            a.transform.position = pos;
            a.clip = clip; a.volume = Mathf.Clamp01(vol); a.pitch = pitch;
            a.Play();
        }

        public static void Boom(Vector3 p, float size)
        {
            var s = Instance;
            if (Time.time - s.lastBoom < 0.04f) return;
            s.lastBoom = Time.time;
            s.PlayAt(s.boom, p, Mathf.Clamp(0.35f + size * 0.03f, 0.35f, 1f), Mathf.Clamp(1.25f - size * 0.02f, 0.65f, 1.2f));
        }

        public static void Crumble(Vector3 p, float vol)
        {
            var s = Instance;
            if (Time.time - s.lastCrumble < 0.06f) return;
            s.lastCrumble = Time.time;
            s.PlayAt(s.crumble, p, vol, Random.Range(0.8f, 1.15f));
        }

        public static void Thump(Vector3 p) { var s = Instance; s.PlayAt(s.thump, p, 0.9f, Random.Range(0.9f, 1.1f)); }
        public static void Zap(Vector3 p, float pitch = 1f) { var s = Instance; s.PlayAt(s.zap, p, 0.9f, pitch); }

        public static void SetWind(float k)
        {
            var s = Instance;
            k = Mathf.Clamp01(k);
            s.wind.volume = Mathf.Lerp(s.wind.volume, k * 0.7f, 0.15f);
            s.wind.pitch = Mathf.Lerp(0.8f, 1.7f, k);
        }

        public static void SetBeam(bool on, Vector3 p)
        {
            var s = Instance;
            s.beam.transform.position = p;
            s.beam.volume = on ? Mathf.Lerp(s.beam.volume, 0.35f, 0.3f) : 0f;
        }
    }
}
