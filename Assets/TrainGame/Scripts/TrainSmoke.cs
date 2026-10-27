using UnityEngine;

namespace TrainGame
{
    /// <summary>
    /// 굴뚝 연기: 배기(chuff)마다 연기 덩어리를 뿜고, 레귤레이터를 많이 열수록 짙어진다. 월드 공간이라 달리면 뒤로 길게 남는다.
    /// </summary>
    public class TrainSmoke : MonoBehaviour
    {
        public TrainController train;
        public Material smokeMaterial;
        [Tooltip("기관차 로컬 좌표: 굴뚝 꼭대기")] public Vector3 chimneyLocal = new Vector3(3.7f, 2.95f, 0f);

        ParticleSystem ps;
        ParticleSystem.EmitParams emit;

        void Start()
        {
            if (train == null) train = TrainController.Instance;
            var go = new GameObject("ChimneySmoke");
            go.transform.SetParent(train.Loco, false);
            go.transform.localPosition = chimneyLocal;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 4.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.42f, 0.41f, 0.4f, 0.85f), new Color(0.6f, 0.59f, 0.57f, 0.8f));
            main.gravityModifier = -0.04f;
            main.maxParticles = 900;
            main.playOnAwake = false;

            var em = ps.emission;
            em.rateOverTime = 3f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.12f;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 2.2f), new Keyframe(1f, 5f)));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(0.55f, 0.55f, 0.55f), 0f), new GradientColorKey(new Color(0.85f, 0.85f, 0.86f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.06f), new GradientAlphaKey(0.45f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var rol = ps.rotationOverLifetime;
            rol.enabled = true;
            rol.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.4f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);   // 약한 바람
            vel.y = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            vel.z = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.sortMode = ParticleSystemSortMode.OldestInFront;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (smokeMaterial != null) rend.sharedMaterial = smokeMaterial;

            ps.Play();
            train.Chuff += Puff;
        }

        void OnDestroy()
        {
            if (train != null) train.Chuff -= Puff;
        }

        void Puff(float strength)
        {
            if (ps == null) return;
            Vector3 up = train.Loco.up;
            int n = 1 + Mathf.RoundToInt(strength * 3f);
            for (int i = 0; i < n; i++)
            {
                emit.velocity = up * Random.Range(2.5f, 4.5f) * (0.5f + strength * 0.7f) + Random.insideUnitSphere * 0.5f;
                emit.startSize = Random.Range(0.6f, 1.1f) * (0.6f + strength * 0.8f);
                ps.Emit(emit, 1);
            }
        }

        void Update()
        {
            if (ps == null) return;
            var em = ps.emission;
            em.rateOverTime = 2f + train.ThrottleActual * 10f;
        }
    }
}
