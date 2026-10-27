using System.Collections.Generic;
using UnityEngine;

// 거인 방귀 가스(DigestGauge·PotatoSacks가 생성): 반투명 살색 연기가 반경 안에 퍼졌다가 시간이 지나면 흩어짐.
// 가스 안의 적(탱크·헬기)은 지속 피해를 입고, 연막 때문에 명중률이 크게 떨어짐(GasCloud.InSmoke로 확인).
public class GasCloud : MonoBehaviour
{
    static readonly List<GasCloud> active = new List<GasCloud>();

    float radius, height, duration, dps, born, nextTick;
    ParticleSystem ps;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { active.Clear(); }

    // 거인이 연막(가스) 안에 숨어 있는지: 이때 적은 공격하지 못함
    public static bool GiantHidden
    {
        get
        {
            var hp = GiantHealth.Instance;
            return hp && InSmoke(hp.transform.position + Vector3.up * 0.5f);
        }
    }

    public static GasCloud Create(Vector3 center, float radius, float duration, float damagePerSecond, Material material)
        => Create(center, radius, duration, damagePerSecond, material, null);

    // color: 연기 색 (null이면 기본 반투명 살색)
    public static GasCloud Create(Vector3 center, float radius, float duration, float damagePerSecond, Material material, Color? color)
    {
        var go = new GameObject("FartGas");
        go.transform.position = new Vector3(center.x, 0.5f, center.z);
        var g = go.AddComponent<GasCloud>();
        g.radius = radius; g.height = radius * 1.2f; g.duration = duration; g.dps = damagePerSecond;
        g.born = Time.time; g.nextTick = Time.time + 0.5f;
        g.ps = g.CreateParticles(material, color);
        active.Add(g);
        return g;
    }

    void OnDestroy() { active.Remove(this); }

    // 이 위치가 가스(연막) 안인지. 흩어지는 마지막 1.5초는 효과 없음
    public static bool InSmoke(Vector3 p)
    {
        foreach (var g in active)
        {
            if (!g || Time.time - g.born > g.duration - 1.5f) continue;
            Vector3 d = p - g.transform.position;
            if (d.y < g.height && new Vector2(d.x, d.z).sqrMagnitude < g.radius * g.radius) return true;
        }
        return false;
    }

    bool Contains(Vector3 p)
    {
        Vector3 d = p - transform.position;
        return d.y < height && new Vector2(d.x, d.z).sqrMagnitude < radius * radius;
    }

    void Update()
    {
        float age = Time.time - born;
        // 끝나기 2.5초 전부터 새 연기를 그만 뿜어서 자연스럽게 흩어짐
        if (ps && ps.isEmitting && age > duration - 2.5f) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        if (age > duration + 3.5f) { Destroy(gameObject); return; }

        // 지속 피해 (0.5초마다)
        if (age < duration - 1.5f && Time.time >= nextTick)
        {
            nextTick = Time.time + 0.5f;
            float dmg = dps * 0.5f;
            foreach (var t in FindObjectsOfType<TankAI>()) if (!t.IsDead && Contains(t.transform.position)) t.TakeDamage(dmg);
            foreach (var h in FindObjectsOfType<HeliAI>()) if (!h.IsDead && Contains(h.transform.position)) h.TakeDamage(dmg);
            foreach (var p in FindObjectsOfType<PoliceUnit>()) p.TakeGas(Contains, dmg);
        }
    }

    ParticleSystem CreateParticles(Material material, Color? color)
    {
        var go = new GameObject("Gas");
        go.transform.SetParent(transform, false);
        var p = go.AddComponent<ParticleSystem>();
        p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = p.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 4.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 0.05f, radius * 0.15f);
        main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.45f, radius * 0.8f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = color.HasValue
            ? new ParticleSystem.MinMaxGradient(color.Value * new Color(0.95f, 0.95f, 0.95f, 0.85f), color.Value)
            : new ParticleSystem.MinMaxGradient(new Color(0.98f, 0.78f, 0.66f, 0.38f), new Color(1f, 0.86f, 0.76f, 0.55f)); // 기본: 반투명 살색 가스
        main.gravityModifier = -0.01f;
        main.maxParticles = 300;
        var emission = p.emission; emission.rateOverTime = 40f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) }); // 처음에 확 퍼짐
        var shape = p.shape; shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = radius * 0.75f;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // 반구가 위를 향하게
        var col = p.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var sol = p.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.3f)));
        var rol = p.rotationOverLifetime; rol.enabled = true; rol.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        p.Play();
        return p;
    }
}
