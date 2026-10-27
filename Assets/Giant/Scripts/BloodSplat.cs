using UnityEngine;

// 시민이 밟혔을 때 피 효과: 작은 핏방울이 살짝 튀고(입자), 바닥에 불규칙한 핏자국이 남았다가 서서히 사라짐.
// BloodSplat.Spawn(위치, 크기)로 사용. 재질은 경고 표시와 같은 버텍스컬러 + 알파 재질(WarningZone.material) 공유.
public class BloodSplat : MonoBehaviour
{
    const float Life = 6f, FadeTime = 2f;
    static ParticleSystem drops;
    static Mesh disc;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    float born;
    MeshRenderer[] renderers;
    MaterialPropertyBlock mpb;

    public static void Spawn(Vector3 pos, float size = 1f)
    {
        if (!WarningZone.material) return;
        EmitDrops(pos, size);

        // 바닥 핏자국: 크기·위치가 조금씩 다른 원 몇 개를 겹쳐 불규칙한 모양으로
        if (!disc) disc = WarningZone.Annulus(0f, 1f, new Color(0.5f, 0.02f, 0.03f, 0.9f), new Color(0.42f, 0.01f, 0.02f, 0.75f));
        var go = new GameObject("BloodSplat");
        go.transform.position = new Vector3(pos.x, pos.y + 0.06f, pos.z);
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        int blobs = Random.Range(3, 6);
        for (int i = 0; i < blobs; i++)
        {
            var b = new GameObject("Blob");
            b.transform.SetParent(go.transform, false);
            float r = (i == 0 ? Random.Range(1.4f, 1.9f) : Random.Range(0.4f, 0.9f)) * size;
            Vector2 off = i == 0 ? Vector2.zero : Random.insideUnitCircle.normalized * Random.Range(1.2f, 2.4f) * size;
            b.transform.localPosition = new Vector3(off.x, i * 0.002f, off.y);
            b.transform.localScale = new Vector3(r * Random.Range(0.8f, 1.2f), 1f, r * Random.Range(0.8f, 1.2f));
            b.AddComponent<MeshFilter>().sharedMesh = disc;
            var mr = b.AddComponent<MeshRenderer>();
            mr.sharedMaterial = WarningZone.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
        var s = go.AddComponent<BloodSplat>();
        s.born = Time.time;
        s.renderers = go.GetComponentsInChildren<MeshRenderer>();
        s.mpb = new MaterialPropertyBlock();
    }

    static void EmitDrops(Vector3 pos, float size)
    {
        if (!drops)
        {
            var go = new GameObject("BloodDrops");
            drops = go.AddComponent<ParticleSystem>();
            drops.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = drops.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 2.5f;
            main.maxParticles = 600;
            var emission = drops.emission; emission.enabled = false;
            var col = drops.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = WarningZone.material;
            drops.Play();
        }
        int n = Random.Range(10, 16);
        for (int i = 0; i < n; i++)
        {
            Vector2 d = Random.insideUnitCircle.normalized;
            var ep = new ParticleSystem.EmitParams
            {
                position = pos + Vector3.up * 0.8f * size,
                velocity = (new Vector3(d.x, 0f, d.y) * Random.Range(1.5f, 5f) + Vector3.up * Random.Range(3f, 7f)) * size,
                startSize = Random.Range(0.25f, 0.6f) * size,
                startLifetime = Random.Range(0.5f, 0.9f),
                startColor = new Color(Random.Range(0.55f, 0.75f), 0.02f, 0.03f, 1f)
            };
            drops.Emit(ep, 1);
        }
    }

    void Update()
    {
        float age = Time.time - born;
        if (age >= Life) { Destroy(gameObject); return; }
        float a = Mathf.Clamp01((Life - age) / FadeTime);
        if (a >= 1f) return;
        mpb.SetColor(ColorId, new Color(1f, 1f, 1f, a));
        foreach (var r in renderers) r.SetPropertyBlock(mpb);
    }
}
