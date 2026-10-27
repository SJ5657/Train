using UnityEngine;

// 건물 체력. 거인이 처음 건드릴 때 자동으로 붙음 (BuildingDestruction.Hit).
// 거인이 건물을 지나갈 때마다 공격력만큼 피해를 입고, 피해가 쌓일수록 땅으로 꺼지며 기울고 어두워짐.
// 체력이 0이 되면 BuildingDestruction.Break로 완전히 무너짐.
public class BuildingHealth : MonoBehaviour
{
    public float MaxHP { get; private set; }
    public float HP { get; private set; }
    public float Damage01 => MaxHP > 0f ? 1f - HP / MaxHP : 0f;

    float lastTouch = -999f, lastHit = -999f;
    Vector3 basePos, pivot, tiltAxis = Vector3.right;
    Quaternion baseRot;
    float height;
    float shownDamage, targetDamage;
    float shake;
    Renderer[] renderers;
    Color[] baseColors;
    static MaterialPropertyBlock mpb;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    public void Init(float maxHp)
    {
        MaxHP = HP = maxHp;
        basePos = transform.position;
        baseRot = transform.rotation;
        height = transform.lossyScale.y;
        pivot = basePos - Vector3.up * height * 0.5f;

        if (mpb == null) mpb = new MaterialPropertyBlock();
        renderers = GetComponentsInChildren<Renderer>();
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].GetPropertyBlock(mpb);
            baseColors[i] = mpb.isEmpty ? renderers[i].sharedMaterial.GetColor(ColorId) : (Color)mpb.GetVector(ColorId);
        }
    }

    // 거인과 닿음. 새로 닿았거나(지나갈 때마다) 계속 닿은 채 repeatInterval이 지나면 피해. 피해를 줬으면 true.
    public bool Touch(float damage, Vector3 hitPoint, Vector3 push, float reTouchGap, float repeatInterval)
    {
        float now = Time.time;
        bool newContact = now - lastTouch > reTouchGap;
        lastTouch = now;
        if (!newContact && now - lastHit < repeatInterval) return false;
        lastHit = now;

        HP = Mathf.Max(0f, HP - damage);
        if (HP <= 0f) return true;

        // 거인에게서 멀어지는 쪽으로 기울어짐
        Vector3 away = transform.position - hitPoint; away.y = 0f;
        if (away.sqrMagnitude < 0.01f) { away = push; away.y = 0f; }
        if (away.sqrMagnitude > 0.01f) tiltAxis = Vector3.Cross(Vector3.up, away.normalized);
        targetDamage = Damage01;
        shake = 1f;
        return true;
    }

    void Update()
    {
        if (shownDamage == targetDamage && shake <= 0f) return;
        float dt = Time.deltaTime;
        shownDamage = Mathf.MoveTowards(shownDamage, targetDamage, dt * 1.6f);
        shake = Mathf.Max(0f, shake - dt * 2.5f);

        var bd = BuildingDestruction.Instance;
        float maxSink = bd ? bd.maxSink : 0.35f, maxTilt = bd ? bd.maxTilt : 10f;

        // 바닥 중심을 축으로 기울이고, 피해만큼 땅속으로 꺼짐 + 맞은 직후 덜덜 떨림
        Quaternion tilt = Quaternion.AngleAxis(shownDamage * maxTilt, tiltAxis);
        Vector3 jitter = shake > 0f ? new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * shake * Mathf.Min(height * 0.04f, 2f) : Vector3.zero;
        transform.SetPositionAndRotation(
            pivot + tilt * (basePos - pivot) - Vector3.up * shownDamage * maxSink * height + jitter,
            tilt * baseRot);

        float dark = Mathf.Lerp(1f, 0.6f, shownDamage);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!renderers[i]) continue;
            renderers[i].GetPropertyBlock(mpb);
            Color c = baseColors[i];
            mpb.SetColor(ColorId, new Color(c.r * dark, c.g * dark, c.b * dark, c.a));
            renderers[i].SetPropertyBlock(mpb);
        }
    }
}
