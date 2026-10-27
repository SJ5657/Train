using UnityEngine;

// 폭발/포구 섬광 파티클과 투사체 생성을 담당.
public class CombatFX : MonoBehaviour
{
    public static CombatFX Instance { get; private set; }
    public Material fireMaterial;      // 가산(Additive) 파티클
    public Material smokeMaterial;     // 알파 블렌드 파티클
    public Material shellMaterial;     // 포탄 몸체
    public Material rocketMaterial;    // 로켓 몸체
    public Material warningMaterial;   // 버텍스 컬러 + 알파 재질 (총알 궤적·핏자국 등에 공유)

    ParticleSystem fire, smoke;

    void Awake()
    {
        Instance = this;
        WarningZone.material = warningMaterial;
        fire = MakePS("FireFX", fireMaterial, 0.35f, 0.7f, new Color(1f, 0.65f, 0.25f, 1f), 800);
        smoke = MakePS("SmokeFX", smokeMaterial, 1.8f, 3.2f, new Color(0.45f, 0.45f, 0.45f, 0.7f), 800);
    }

    ParticleSystem MakePS(string n, Material mat, float lifeMin, float lifeMax, Color col, int max)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startColor = col;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        var em = ps.emission; em.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        var c = ps.colorOverLifetime; c.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(0f, 1) });
        c.color = g;
        var s = ps.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 1.5f));
        var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.dampen = 0.15f; lv.limit = 0f;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        ps.Play();
        return ps;
    }

    public static void Explosion(Vector3 p, float size)
    {
        var fx = Instance; if (!fx) return;
        for (int i = 0; i < 10; i++)
        {
            var ep = new ParticleSystem.EmitParams { position = p + Random.insideUnitSphere * size * 0.3f, velocity = Random.insideUnitSphere * size * 1.5f, startSize = size * Random.Range(0.6f, 1.3f) };
            fx.fire.Emit(ep, 1);
        }
        for (int i = 0; i < 7; i++)
        {
            var ep = new ParticleSystem.EmitParams { position = p + Random.insideUnitSphere * size * 0.4f, velocity = (Random.insideUnitSphere + Vector3.up) * size * 0.5f, startSize = size * Random.Range(1f, 1.8f) };
            fx.smoke.Emit(ep, 1);
        }
    }

    public static void Muzzle(Vector3 p, Vector3 dir, float size)
    {
        var fx = Instance; if (!fx) return;
        for (int i = 0; i < 4; i++)
        {
            var ep = new ParticleSystem.EmitParams { position = p, velocity = (dir + Random.insideUnitSphere * 0.3f) * size * 4f, startSize = size * Random.Range(0.6f, 1.1f), startLifetime = 0.15f };
            fx.fire.Emit(ep, 1);
        }
        var sp = new ParticleSystem.EmitParams { position = p, velocity = dir * size, startSize = size * 1.5f, startLifetime = 1.2f };
        fx.smoke.Emit(sp, 2);
    }

    public enum Kind { Shell, Rocket }

    public static Projectile Spawn(Kind kind, Vector3 pos, Vector3 dir, float speed, float damage, float homing)
    {
        var fx = Instance;
        var go = GameObject.CreatePrimitive(kind == Kind.Shell ? PrimitiveType.Sphere : PrimitiveType.Capsule);
        Destroy(go.GetComponent<Collider>());
        go.name = kind.ToString();
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(dir);
        if (kind == Kind.Shell) go.transform.localScale = Vector3.one * 1.9f;
        else
        {
            // 캡슐을 진행방향(z)으로 눕힘
            var body = go.transform; body.localScale = new Vector3(1.2f, 2.4f, 1.2f);
            var holder = new GameObject("Rocket").transform;
            holder.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
            body.SetParent(holder, true);
            body.localRotation = Quaternion.Euler(90, 0, 0);
            body.localPosition = Vector3.zero;
            go = holder.gameObject;
        }
        var mr = go.GetComponentInChildren<MeshRenderer>();
        if (fx) mr.sharedMaterial = kind == Kind.Shell ? fx.shellMaterial : fx.rocketMaterial;

        // 꼬리 궤적
        var trailGo = new GameObject("Trail");
        trailGo.transform.SetParent(go.transform, false);
        var tr = trailGo.AddComponent<TrailRenderer>();
        if (kind == Kind.Shell)
        {
            tr.time = 0.25f; tr.startWidth = 1.9f; tr.endWidth = 0f;
            tr.startColor = new Color(1f, 0.85f, 0.4f, 1f); tr.endColor = new Color(1f, 0.4f, 0.1f, 0f);
            if (fx) tr.sharedMaterial = fx.fireMaterial;
        }
        else
        {
            tr.time = 1.4f; tr.startWidth = 1.5f; tr.endWidth = 6f;
            tr.startColor = new Color(0.9f, 0.9f, 0.9f, 0.8f); tr.endColor = new Color(0.6f, 0.6f, 0.6f, 0f);
            if (fx) tr.sharedMaterial = fx.smokeMaterial;
        }
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.minVertexDistance = 2f;

        var p = go.AddComponent<Projectile>();
        p.Launch(dir * speed, damage, homing, kind == Kind.Shell ? 6f : 9f, tr);
        ThreatGlow.Add(go.transform, kind == Kind.Shell ? 18f : 22f); // 날아오는 것을 알아볼 수 있게 붉은 후광
        return p;
    }
}
