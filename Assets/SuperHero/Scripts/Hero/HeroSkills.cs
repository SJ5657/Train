using System.Collections;
using UnityEngine;

namespace SuperHero
{
    /// <summary>
    /// 슈퍼히어로 스킬 모음.
    ///  좌클릭 슈퍼 펀치 / 우클릭(꾹) 히트 비전 / E(꾹) 에너지 블래스트 /
    ///  Q 히어로 슬램(점프 후 내려찍기, 공중에서는 급강하) / R 충격파 / T 운석 비
    /// </summary>
    public class HeroSkills : MonoBehaviour
    {
        public static HeroSkills Instance { get; private set; }

        public float energy = 100f, maxEnergy = 100f;
        public float punchCd = 0.38f, blastCd = 1.2f, slamCd = 7f, waveCd = 5f, meteorCd = 14f;
        [Tooltip("테스트용: 입력 없이 히트 비전 유지 / 로그 출력")] public bool debugHeat, debugLog;

        HeroController hero;
        HeroCamera cam;
        Animator anim;

        float punchNext, blastNext, slamNext, waveNext, meteorNext;
        bool rightHand;
        float aimUntil;
        float upperW, upperUntil;

        // 히트 비전
        LineRenderer[] beams;
        bool heatOn;
        float heatTick, heatFx;
        Light heatLight;

        // 블래스트 충전
        bool charging;
        float charge;

        // 슬램
        enum SlamPhase { None, Leap, Dive }
        SlamPhase slam;
        float slamT;
        Vector3 diveDir;

        public float PunchCd01 { get { return Mathf.Clamp01((punchNext - Time.time) / punchCd); } }
        public float BlastCd01 { get { return Mathf.Clamp01((blastNext - Time.time) / blastCd); } }
        public float SlamCd01 { get { return Mathf.Clamp01((slamNext - Time.time) / slamCd); } }
        public float WaveCd01 { get { return Mathf.Clamp01((waveNext - Time.time) / waveCd); } }
        public float MeteorCd01 { get { return Mathf.Clamp01((meteorNext - Time.time) / meteorCd); } }
        public bool HeatOn { get { return heatOn; } }
        public float Charge01 { get { return charging ? Mathf.Clamp01(charge / 1.4f) : 0f; } }

        void Awake() { Instance = this; }

        void Start()
        {
            hero = GetComponent<HeroController>();
            cam = hero.cam != null ? hero.cam : HeroCamera.Instance;
            anim = hero.animator != null ? hero.animator : GetComponentInChildren<Animator>();
            BuildBeams();
        }

        void BuildBeams()
        {
            var sh = Shader.Find("Legacy Shaders/Particles/Additive");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", new Color(1f, 0.25f, 0.1f, 1f));
            mat.color = new Color(1f, 0.25f, 0.1f, 1f);
            beams = new LineRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("HeatBeam" + i);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = mat;
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.startWidth = 0.08f; lr.endWidth = 0.14f;
                lr.numCapVertices = 4;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.startColor = new Color(1f, 0.4f, 0.2f, 1f); lr.endColor = new Color(1f, 0.15f, 0.05f, 1f);
                lr.enabled = false;
                beams[i] = lr;
            }
            var lg = new GameObject("HeatLight");
            lg.transform.SetParent(transform, false);
            heatLight = lg.AddComponent<Light>();
            heatLight.type = LightType.Point; heatLight.range = 14f; heatLight.color = new Color(1f, 0.4f, 0.2f); heatLight.intensity = 0f; heatLight.shadows = LightShadows.None;
        }

        // ───────────────────────── 입력 ─────────────────────────

        void Update()
        {
            if (!SuperCityGenerator.Ready || hero == null) return;
            float dt = Time.deltaTime;
            bool input = Cursor.lockState == CursorLockMode.Locked && Time.frameCount != HeroCamera.LockFrame;

            if (input)
            {
                if (Input.GetMouseButton(0) && Time.time >= punchNext && slam == SlamPhase.None) Punch();

                bool wantHeat = (Input.GetMouseButton(1) || debugHeat) && slam == SlamPhase.None;
                if (wantHeat && !heatOn && energy > 8f) BeginHeat();
                else if ((!wantHeat || energy <= 0f) && heatOn) EndHeat();

                if (Input.GetKeyDown(KeyCode.E) && Time.time >= blastNext && slam == SlamPhase.None) { charging = true; charge = 0f; TriggerUpper("Cast", 5f); }
                if (charging)
                {
                    charge += dt;
                    ChargeFx();
                    if (Input.GetKeyUp(KeyCode.E) || charge >= 1.8f) FireBlast();
                }

                if (Input.GetKeyDown(KeyCode.Q)) Slam();
                if (Input.GetKeyDown(KeyCode.R)) ShockWave();
                if (Input.GetKeyDown(KeyCode.T)) MeteorRain();
            }
            else if (heatOn) EndHeat();

            UpdateSlam(dt);
            UpdateHeat(dt);

            energy = Mathf.Min(maxEnergy, energy + (heatOn ? 0f : 14f) * dt);
            hero.AimLock = heatOn || charging || Time.time < aimUntil;

            // 상체 애니메이션 레이어 가중치
            bool upperAllowed = anim != null && !hero.IsFlying && slam == SlamPhase.None;
            float tw = upperAllowed && Time.time < upperUntil ? 1f : 0f;
            upperW = Mathf.MoveTowards(upperW, tw, dt * 9f);
            if (anim != null && anim.layerCount > 1) anim.SetLayerWeight(1, upperW);
        }

        void TriggerUpper(string state, float hold)
        {
            upperUntil = Time.time + hold;
            if (anim != null && anim.layerCount > 1) anim.CrossFadeInFixedTime(state, 0.06f, 1);
        }

        Vector3 AimDir
        {
            get
            {
                if (hero.IsFlying && cam != null) return cam.transform.forward;
                return hero.transform.forward;
            }
        }

        // ───────────────────────── 슈퍼 펀치 ─────────────────────────

        public void Punch()
        {
            punchNext = Time.time + punchCd;
            rightHand = !rightHand;
            aimUntil = Time.time + 0.35f;
            if (!hero.IsFlying)
            {
                TriggerUpper(rightHand ? "PunchR" : "PunchL", 0.45f);
                Vector3 f = hero.transform.forward; f.y = 0f;
                hero.Impulse(f.normalized * 5f);
            }
            else hero.Impulse(AimDir * 14f);
            StartCoroutine(PunchHit(0.13f));
        }

        IEnumerator PunchHit(float delay)
        {
            yield return new WaitForSeconds(delay);
            Vector3 dir = AimDir;
            Vector3 origin = hero.Center + Vector3.up * 0.35f;
            float spd = hero.Speed;
            float range = 4.4f + spd * 0.04f;
            Vector3 hp = origin + dir * range * 0.75f;
            Vector3 n = -dir;
            RaycastHit hit;
            if (Physics.SphereCast(origin, 0.8f, dir, out hit, range, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
            { hp = hit.point; n = hit.normal; }
            float power = 1f + Mathf.Clamp01(spd / 80f);
            int broke = DestructionManager.Explode(hp, 4.2f * power, 260f * power, 16f * power);
            if (debugLog) Debug.Log("[Punch] hit=" + hp + " dir=" + dir + " broke=" + broke);
            Sfx.Thump(hp);
            Fx.Sparks(hp, n, 10, 16f);
            Fx.Dust(hp, 2.2f, broke > 0 ? 3 : 1, 3f);
            Fx.Ring(hp, n, 0.3f, 3.5f, 0.28f, new Color(1f, 0.9f, 0.6f, 0.8f));
            Fx.Shake(hp, broke > 0 ? 0.28f : 0.1f, 80f);
        }

        // ───────────────────────── 히트 비전 ─────────────────────────

        public void BeginHeat()
        {
            heatOn = true;
            TriggerUpper("Cast", 0.3f);
            foreach (var b in beams) b.enabled = true;
        }

        public void EndHeat()
        {
            heatOn = false;
            foreach (var b in beams) b.enabled = false;
            if (heatLight != null) heatLight.intensity = 0f;
            Sfx.SetBeam(false, hero != null ? hero.Center : Vector3.zero);
        }

        Vector3 EyePos(bool left)
        {
            var eye = left ? hero.LeftEye : hero.RightEye;
            if (eye != null) return eye.position;
            var t = hero.modelPivot != null ? hero.modelPivot : hero.transform;
            Vector3 headPos = hero.Head != null ? hero.Head.position : hero.Center + Vector3.up * 0.6f;
            return headPos + t.up * 0.05f + t.forward * 0.09f + t.right * (left ? -0.033f : 0.033f);
        }

        void UpdateHeat(float dt)
        {
            if (!heatOn) return;
            energy -= 20f * dt;
            if (energy <= 0f) { EndHeat(); return; }
            TriggerUpper("Cast", 0.2f);

            Vector3 aim = cam != null ? cam.AimPoint(800f) : hero.transform.position + hero.transform.forward * 50f;
            Vector3 eL = EyePos(true), eR = EyePos(false);
            Vector3 mid = (eL + eR) * 0.5f;
            Vector3 dir = (aim - mid).normalized;
            Vector3 end = aim;
            Vector3 normal = -dir;
            RaycastHit hit;
            if (Physics.Raycast(mid, dir, out hit, 900f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore)) { end = hit.point; normal = hit.normal; }

            float w = 0.06f + 0.03f * Mathf.Sin(Time.time * 70f);
            for (int i = 0; i < 2; i++)
            {
                var lr = beams[i];
                lr.SetPosition(0, i == 0 ? eL : eR);
                lr.SetPosition(1, end);
                lr.startWidth = w; lr.endWidth = w * 2.2f;
            }

            heatTick += dt;
            if (heatTick >= 0.05f)
            {
                heatTick = 0f;
                DestructionManager.DamageBuildings(end + normal * 0.2f, 2.4f, 40f, 0f);
                Fx.Sparks(end, normal, 3, 12f);
                Fx.Fire(end + normal * 0.2f, 0.7f, 1, 2.5f);
            }
            heatFx += dt;
            if (heatFx > 0.12f) { heatFx = 0f; Fx.Smoke(end, 1.4f, 1, 2f); Fx.Dust(end, 1.2f, 1, 2f); }
            if (heatLight != null) { heatLight.transform.position = end + normal * 0.8f; heatLight.intensity = 2.5f + Mathf.Sin(Time.time * 50f) * 0.7f; }
            Sfx.SetBeam(true, mid);
        }

        // ───────────────────────── 에너지 블래스트 ─────────────────────────

        void ChargeFx()
        {
            float k = Mathf.Clamp01(charge / 1.4f);
            Vector3 p = hero.Center + AimDir * 1.4f + Vector3.up * 0.3f;
            Fx.Fire(p, 0.5f + k * 1.2f, 1, 1.5f);
            if (Random.value < 0.4f) Fx.Sparks(p, Random.onUnitSphere, 1, 5f);
            aimUntil = Time.time + 0.2f;
            TriggerUpperKeep();
        }

        void TriggerUpperKeep() { upperUntil = Time.time + 0.2f; }

        /// <summary>테스트용: 충전 시간을 지정해 즉시 발사.</summary>
        public void DebugBlast(float chargeSeconds) { charge = chargeSeconds; FireBlast(); }

        public void FireBlast()
        {
            float k = Mathf.Clamp01(charge / 1.4f);
            charging = false; charge = 0f;
            blastNext = Time.time + blastCd;
            aimUntil = Time.time + 0.4f;
            Vector3 aim = cam != null ? cam.AimPoint(900f) : hero.transform.position + hero.transform.forward * 80f;
            Vector3 spawn = hero.Center + AimDir * 1.6f + Vector3.up * 0.25f;
            Vector3 dir = (aim - spawn).normalized;
            HeroProjectile.Spawn(spawn, dir * 135f, Mathf.Lerp(1.1f, 2.6f, k), Mathf.Lerp(10f, 24f, k), Mathf.Lerp(650f, 1500f, k), Mathf.Lerp(16f, 30f, k), 0f, new Color(1f, 0.55f, 0.15f));
            Fx.Flash(spawn, 14f, new Color(1f, 0.6f, 0.2f), 0.2f, 4f);
            Sfx.Zap(spawn, 1.5f);
            Fx.Sparks(spawn, dir, 12, 18f);
            Fx.Shake(spawn, 0.2f + 0.2f * k, 60f);
            if (hero.IsFlying) hero.Impulse(-dir * 8f);
        }

        // ───────────────────────── 슬램 ─────────────────────────

        public void Slam()
        {
            if (Time.time < slamNext || slam != SlamPhase.None) return;
            slamNext = Time.time + slamCd;
            if (charging) { charging = false; charge = 0f; }
            if (heatOn) EndHeat();
            if (!hero.IsFlying && hero.Grounded)
            {
                slam = SlamPhase.Leap; slamT = 0f;
                Vector3 f = hero.transform.forward; f.y = 0f;
                hero.BeginDriven(Vector3.up * 36f + f.normalized * 7f);
                Fx.Dust(hero.transform.position, 3f, 8, 6f);
                Fx.Ring(hero.transform.position, Vector3.up, 0.5f, 9f, 0.4f, new Color(1f, 1f, 1f, 0.6f));
            }
            else StartDive();
        }

        void StartDive()
        {
            slam = SlamPhase.Dive; slamT = 0f;
            Vector3 f = hero.transform.forward; f.y = 0f;
            diveDir = (Vector3.down + f.normalized * 0.22f).normalized;
            hero.BeginDriven(diveDir * 50f);
        }

        void UpdateSlam(float dt)
        {
            if (slam == SlamPhase.None) return;
            slamT += dt;
            if (slam == SlamPhase.Leap)
            {
                Vector3 f = hero.transform.forward; f.y = 0f;
                hero.SetDrivenVelocity(Vector3.up * Mathf.Lerp(36f, 4f, slamT / 0.6f) + f.normalized * 7f);
                if (slamT > 0.6f) StartDive();
                return;
            }
            float speed = Mathf.Min(50f + slamT * 260f, 150f);
            hero.SetDrivenVelocity(diveDir * speed);
            if (Random.value < 0.8f) Fx.Fire(hero.Center - diveDir * 0.6f, 0.9f, 1, 2f);
            bool near = Physics.Raycast(hero.Center, diveDir, 2.4f + speed * dt, GameLayers.WorldMask, QueryTriggerInteraction.Ignore);
            if ((hero.Grounded && slamT > 0.08f) || near || slamT > 5f) SlamImpact();
        }

        void SlamImpact()
        {
            Vector3 p = hero.transform.position;
            slam = SlamPhase.None;
            hero.EndDriven(false);
            Vector3 c = p + Vector3.up * 0.8f;
            DestructionManager.Explode(c, 36f, 1600f, 30f);
            Fx.Explosion(p + Vector3.up * 0.5f, 24f);
            Fx.Ring(p, Vector3.up, 2f, 42f, 0.8f, new Color(1f, 0.8f, 0.5f, 0.9f));
            Fx.Ring(p, Vector3.up, 1f, 28f, 0.55f, new Color(1f, 1f, 1f, 0.9f));
            for (int i = 0; i < 20; i++)
            {
                float a = i / 20f * Mathf.PI * 2f;
                Fx.Dust(p + new Vector3(Mathf.Cos(a), 0.3f, Mathf.Sin(a)) * 7f, 6f, 1, 14f);
            }
            Fx.Shake(p, 1.6f, 800f);
            if (anim != null) anim.CrossFadeInFixedTime("SlamLand", 0.05f, 0);
            hero.Impulse(Vector3.up * 6f);
        }

        // ───────────────────────── 충격파 ─────────────────────────

        public void ShockWave()
        {
            if (Time.time < waveNext) return;
            waveNext = Time.time + waveCd;
            StartCoroutine(WaveRoutine());
        }

        IEnumerator WaveRoutine()
        {
            Vector3 c = hero.Center;
            Vector3 ground = hero.transform.position;
            const float maxR = 60f;
            const int steps = 12;
            Fx.Ring(ground, Vector3.up, 1f, maxR, 0.55f, new Color(0.7f, 0.9f, 1f, 0.9f));
            Fx.Flash(c, 40f, new Color(0.7f, 0.9f, 1f), 0.4f, 5f);
            Sfx.Zap(c, 0.7f);
            Fx.Shake(c, 0.9f, 400f);
            TriggerUpper("Cast", 0.5f);
            aimUntil = Time.time + 0.5f;
            for (int i = 1; i <= steps; i++)
            {
                float r0 = maxR * (i - 1) / steps, r1 = maxR * i / steps;
                DestructionManager.Explode(c, r1, 380f * (1f - 0.55f * i / steps), 22f, r0);
                for (int k = 0; k < 10; k++)
                {
                    float a = Random.value * Mathf.PI * 2f;
                    Fx.Dust(ground + new Vector3(Mathf.Cos(a), 0.2f, Mathf.Sin(a)) * r1, 4f, 1, 6f);
                }
                yield return new WaitForSeconds(0.04f);
            }
        }

        // ───────────────────────── 운석 비 ─────────────────────────

        public void MeteorRain()
        {
            if (Time.time < meteorNext) return;
            meteorNext = Time.time + meteorCd;
            StartCoroutine(MeteorRoutine());
        }

        IEnumerator MeteorRoutine()
        {
            Vector3 target = cam != null ? cam.AimPoint(700f) : hero.transform.position + hero.transform.forward * 100f;
            TriggerUpper("Cast", 0.8f);
            aimUntil = Time.time + 0.8f;
            Fx.Ring(target, Vector3.up, 2f, 45f, 1.4f, new Color(1f, 0.3f, 0.1f, 0.7f));
            yield return new WaitForSeconds(0.5f);
            for (int i = 0; i < 16; i++)
            {
                Vector2 o = Random.insideUnitCircle * 42f;
                Vector3 tp = target + new Vector3(o.x, 0f, o.y);
                RaycastHit hit;
                if (Physics.Raycast(tp + Vector3.up * 400f, Vector3.down, out hit, 800f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore)) tp = hit.point;
                Vector3 start = tp + new Vector3(Random.Range(-70f, 70f), 230f, Random.Range(-70f, 70f));
                Vector3 vel = (tp - start).normalized * 115f;
                HeroProjectile.Spawn(start, vel, Random.Range(2.2f, 3.4f), 13f, 950f, 22f, 0f, new Color(1f, 0.4f, 0.1f));
                yield return new WaitForSeconds(0.13f);
            }
        }
    }
}
