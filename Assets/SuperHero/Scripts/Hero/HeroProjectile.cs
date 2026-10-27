using UnityEngine;

namespace SuperHero
{
    /// <summary>에너지탄 / 운석. 날아가다 지면·건물에 닿으면 폭발하며 닿은 곳에서부터 구조물을 파괴한다.</summary>
    public class HeroProjectile : MonoBehaviour
    {
        public Vector3 velocity;
        public float gravity;
        public float radius = 12f, damage = 700f, force = 20f;
        public float size = 1f;
        public float life = 8f;
        public Color glow = new Color(1f, 0.55f, 0.15f);

        Light lightComp;
        float age;
        static Material coreMat;

        public static HeroProjectile Spawn(Vector3 pos, Vector3 vel, float size, float radius, float damage, float force, float gravity, Color glow)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Projectile";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * size;
            if (coreMat == null) coreMat = new Material(Shader.Find("Unlit/Color"));
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = coreMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", Color.Lerp(glow, Color.white, 0.7f));
            mr.SetPropertyBlock(mpb);

            var p = go.AddComponent<HeroProjectile>();
            p.velocity = vel; p.size = size; p.radius = radius; p.damage = damage; p.force = force; p.gravity = gravity; p.glow = glow;
            var l = new GameObject("L").AddComponent<Light>();
            l.transform.SetParent(go.transform, false);
            l.type = LightType.Point; l.range = 18f * size; l.intensity = 3f; l.color = glow; l.shadows = LightShadows.None;
            p.lightComp = l;
            return p;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (age > life) { Detonate(transform.position, Vector3.up); return; }

            velocity += Vector3.down * gravity * dt;
            Vector3 step = velocity * dt;
            float len = step.magnitude;
            RaycastHit hit;
            if (len > 0.0001f && Physics.SphereCast(transform.position, Mathf.Max(0.3f, size * 0.35f), step / len, out hit, len + 0.05f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
            {
                Detonate(hit.point + hit.normal * 0.4f, hit.normal);
                return;
            }
            transform.position += step;

            // 꼬리 불꽃
            Fx.Fire(transform.position, size * 0.9f, 2, 2f);
            if (Random.value < 0.5f) Fx.Smoke(transform.position, size * 1.1f, 1, 1.5f);
            if (lightComp != null) lightComp.intensity = 2.5f + Mathf.Sin(age * 40f) * 0.6f;
        }

        void Detonate(Vector3 p, Vector3 normal)
        {
            Fx.Explosion(p, radius);
            Fx.Ring(p, normal, radius * 0.1f, radius * 1.6f, 0.6f, new Color(glow.r, glow.g, glow.b, 0.8f));
            DestructionManager.Explode(p, radius, damage, force);
            Destroy(gameObject);
        }
    }
}
