using System.Collections.Generic;
using UnityEngine;

namespace SuperHero
{
    /// <summary>
    /// 외부 에셋 없이 코드로 만드는 이펙트 모음: 먼지/연기/불꽃/스파크 파티클, 충격파 링, 번쩍이는 빛.
    /// 파티클 시스템은 하나씩만 두고 Emit으로 한 알씩 쏘기 때문에 대량 파괴에도 가볍다.
    /// </summary>
    public class Fx : MonoBehaviour
    {
        static Fx inst;

        public static Fx Instance
        {
            get
            {
                if (inst == null)
                {
                    var go = new GameObject("FX");
                    inst = go.AddComponent<Fx>();
                }
                return inst;
            }
        }

        ParticleSystem dust, smoke, fire, sparks;
        Material alphaMat, addMat, ringMat;
        Mesh quad;
        readonly List<RingFx> rings = new List<RingFx>();
        readonly List<FlashLight> flashes = new List<FlashLight>();
        int emitThisFrame;
        const int MaxEmitPerFrame = 700;

        class RingFx
        {
            public Transform t; public MeshRenderer mr; public MaterialPropertyBlock mpb;
            public float t0, dur, r0, r1; public Color col;
        }

        class FlashLight { public Light light; public float t0, dur, intensity; }

        // ───────────────────────── 생성 ─────────────────────────

        void Awake()
        {
            inst = this;
            var soft = MakeSoftTexture(64);
            var ringTex = MakeRingTexture(128);
            alphaMat = MakeMat("Legacy Shaders/Particles/Alpha Blended", soft);
            addMat = MakeMat("Legacy Shaders/Particles/Additive", soft);
            ringMat = MakeMat("Legacy Shaders/Particles/Additive", ringTex);

            quad = new Mesh { name = "FxQuad" };
            quad.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(.5f, 0, -.5f), new Vector3(.5f, 0, .5f), new Vector3(-.5f, 0, .5f) };
            quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateNormals(); quad.RecalculateBounds();

            dust = MakePS("Dust", alphaMat, 5000, -0.01f);
            Fade(dust, 0.12f, 0.62f, 1.8f, 0.55f);
            smoke = MakePS("Smoke", alphaMat, 2000, -0.03f);
            Fade(smoke, 0.1f, 0.6f, 2.2f, 0.5f);
            fire = MakePS("Fire", addMat, 1500, -0.1f);
            FireColor(fire);
            sparks = MakePS("Sparks", addMat, 2500, 1.6f, true);
            Fade(sparks, 0.02f, 1f, 0.3f, 0.8f);
        }

        static Material MakeMat(string shaderName, Texture tex)
        {
            var s = Shader.Find(shaderName);
            if (s == null) s = Shader.Find("Sprites/Default");
            var m = new Material(s) { mainTexture = tex };
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            return m;
        }

        static Texture2D MakeSoftTexture(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    a *= 0.8f + 0.4f * Mathf.PerlinNoise(x * 0.17f + 3.1f, y * 0.17f + 7.7f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            t.SetPixels32(px); t.Apply(true);
            return t;
        }

        static Texture2D MakeRingTexture(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) / 0.14f);
                    float inner = d < 0.86f ? 0.12f * d * d : 0f;
                    float a = Mathf.Clamp01(ring * ring + inner);
                    if (d > 1f) a = 0f;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            t.SetPixels32(px); t.Apply(true);
            return t;
        }

        ParticleSystem MakePS(string psName, Material mat, int max, float gravity, bool stretched = false)
        {
            var go = new GameObject(psName);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            main.startLifetime = 2f;
            main.startSpeed = 0f;
            main.startSize = 1f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (stretched)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.lengthScale = 1.5f; r.velocityScale = 0.035f;
            }
            ps.Play();
            return ps;
        }

        /// <summary>알파 페이드 + 크기 변화 (grow = 끝 크기 배율).</summary>
        static void Fade(ParticleSystem ps, float fadeIn, float peakAlpha, float grow, float startSize)
        {
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peakAlpha, fadeIn), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, startSize), new Keyframe(1f, startSize * grow)));
        }

        static void FireColor(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.45f), new GradientColorKey(new Color(0.35f, 0.1f, 0.05f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.2f)));
        }

        // ───────────────────────── 업데이트 ─────────────────────────

        void LateUpdate()
        {
            emitThisFrame = 0;
            float now = Time.time;
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i];
                float k = (now - r.t0) / r.dur;
                if (k >= 1f || r.t == null)
                {
                    if (r.t != null) Destroy(r.t.gameObject);
                    rings.RemoveAt(i);
                    continue;
                }
                float e = 1f - (1f - k) * (1f - k);
                float rad = Mathf.Lerp(r.r0, r.r1, e);
                r.t.localScale = new Vector3(rad * 2f, 1f, rad * 2f);
                var c = r.col; c.a *= (1f - k);
                r.mpb.SetColor("_TintColor", c);
                r.mr.SetPropertyBlock(r.mpb);
            }
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                var f = flashes[i];
                float k = (now - f.t0) / f.dur;
                if (k >= 1f || f.light == null)
                {
                    if (f.light != null) Destroy(f.light.gameObject);
                    flashes.RemoveAt(i);
                    continue;
                }
                f.light.intensity = f.intensity * (1f - k) * (1f - k);
            }
        }

        // ───────────────────────── 방출 ─────────────────────────

        void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color col)
        {
            if (emitThisFrame++ > MaxEmitPerFrame) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = pos, velocity = vel, startSize = size, startLifetime = life, startColor = col,
                rotation = Random.value * 360f, angularVelocity = Random.Range(-25f, 25f)
            };
            ps.Emit(ep, 1);
        }

        public static void Dust(Vector3 p, float size, int count = 1, float speed = 3f)
        {
            var f = Instance;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = Random.insideUnitSphere * speed;
                v.y = Mathf.Abs(v.y) * 0.5f + speed * 0.15f;
                float g = Random.Range(0.52f, 0.7f);
                f.Emit(f.dust, p + Random.insideUnitSphere * size * 0.35f, v, size * Random.Range(0.8f, 1.5f), Random.Range(2.5f, 5.5f), new Color(g, g * 0.95f, g * 0.88f, 1f));
            }
        }

        public static void Smoke(Vector3 p, float size, int count = 1, float speed = 2f)
        {
            var f = Instance;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = Random.insideUnitSphere * speed; v.y = Mathf.Abs(v.y) + speed * 0.6f;
                float g = Random.Range(0.12f, 0.3f);
                f.Emit(f.smoke, p + Random.insideUnitSphere * size * 0.3f, v, size * Random.Range(0.8f, 1.4f), Random.Range(3f, 6f), new Color(g, g, g, 1f));
            }
        }

        public static void Fire(Vector3 p, float size, int count = 1, float speed = 6f)
        {
            var f = Instance;
            for (int i = 0; i < count; i++)
                f.Emit(f.fire, p + Random.insideUnitSphere * size * 0.3f, Random.insideUnitSphere * speed, size * Random.Range(0.7f, 1.3f), Random.Range(0.35f, 0.9f), Color.white);
        }

        public static void Sparks(Vector3 p, Vector3 normal, int count = 6, float speed = 14f)
        {
            var f = Instance;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = (normal + Random.insideUnitSphere * 0.9f).normalized * speed * Random.Range(0.4f, 1.1f);
                f.Emit(f.sparks, p, v, Random.Range(0.12f, 0.3f), Random.Range(0.35f, 0.9f), new Color(1f, Random.Range(0.6f, 0.9f), 0.3f, 1f));
            }
        }

        public static void Ring(Vector3 center, Vector3 normal, float r0, float r1, float dur, Color col)
        {
            var f = Instance;
            var go = new GameObject("Ring");
            go.transform.SetParent(f.transform, false);
            go.transform.position = center + normal * 0.15f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
            go.AddComponent<MeshFilter>().sharedMesh = f.quad;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = f.ringMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            f.rings.Add(new RingFx { t = go.transform, mr = mr, mpb = new MaterialPropertyBlock(), t0 = Time.time, dur = dur, r0 = r0, r1 = r1, col = col });
        }

        public static void Flash(Vector3 p, float range, Color col, float dur = 0.3f, float intensity = 5f)
        {
            var f = Instance;
            if (f.flashes.Count > 12) return;
            var go = new GameObject("Flash");
            go.transform.SetParent(f.transform, false);
            go.transform.position = p;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = range; l.color = col; l.intensity = intensity; l.shadows = LightShadows.None;
            f.flashes.Add(new FlashLight { light = l, t0 = Time.time, dur = dur, intensity = intensity });
        }

        /// <summary>폭발 연출 한 방: 불꽃 + 연기 + 먼지 + 충격파 링 + 빛 + 카메라 흔들림.</summary>
        public static void Explosion(Vector3 p, float radius)
        {
            Flash(p + Vector3.up * 2f, radius * 3f, new Color(1f, 0.6f, 0.25f), 0.35f, 6f);
            Fire(p, radius * 0.28f, Mathf.Clamp((int)(radius * 1.6f), 8, 36), radius * 1.1f);
            Smoke(p, radius * 0.6f, Mathf.Clamp((int)radius, 6, 28), radius * 0.5f);
            Dust(p, radius * 0.6f, Mathf.Clamp((int)radius, 6, 30), radius * 0.7f);
            Sparks(p, Vector3.up, Mathf.Clamp((int)(radius * 2f), 10, 50), radius * 1.6f);
            Ring(p, Vector3.up, radius * 0.15f, radius * 1.3f, 0.55f, new Color(1f, 0.75f, 0.45f, 0.9f));
            Shake(p, Mathf.Clamp(radius * 0.06f, 0.2f, 1.6f), radius * 9f + 40f);
            Sfx.Boom(p, radius);
        }

        /// <summary>카메라와의 거리에 따라 약해지는 화면 흔들림.</summary>
        public static void Shake(Vector3 worldPos, float amount, float range)
        {
            var cam = Camera.main;
            if (cam == null) return;
            float d = Vector3.Distance(cam.transform.position, worldPos);
            float k = Mathf.Clamp01(1f - d / range);
            if (k > 0f) HeroCamera.AddShake(amount * k);
        }
    }
}
