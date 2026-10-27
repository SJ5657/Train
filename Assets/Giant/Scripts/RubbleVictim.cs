using System.Collections.Generic;
using UnityEngine;

// 건물 잔해에 깔린 사람: 엎드린 채 다리·허리 위에 잔해 덩어리가 얹혀 있고, 팔을 버둥거림.
// 거인이 가까이 오면 HostageEvent가 인질처럼 손으로 집어 듦 (잔해는 땅에 남음).
// 만들어 내는 쪽은 RubbleVictimSpawner (건물이 부서질 때 확률로).
public class RubbleVictim : MonoBehaviour
{
    public static readonly List<RubbleVictim> All = new List<RubbleVictim>();

    public Animator Anim { get; private set; }
    public float Height { get; private set; }
    public bool Grabbed { get; private set; }
    public string Title; // 알림·확대 화면에 뜨는 이름 (고위급 인질)

    readonly List<GameObject> rubble = new List<GameObject>();
    float born, lifetime, pickRangeRatio, grabDelay;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public void Init(Animator a, float height, float life, float pickRatio, float delay)
    {
        Anim = a; Height = height; lifetime = life; pickRangeRatio = pickRatio; grabDelay = delay; born = Time.time;
    }

    public void AddRubble(GameObject g) => rubble.Add(g);

    // 거인 손에 잡힘: 일으켜 세우고(위치·방향은 HostageEvent가 정함), 잔해는 땅에 남겨 두었다가 치움
    public void OnGrabbed()
    {
        Grabbed = true;
        foreach (var g in rubble) if (g) Destroy(g, 8f);
        var glow = GetComponentInChildren<ThreatGlow>(); if (glow) Destroy(glow.gameObject); // 손에 쥔 뒤엔 테두리 표시 없앰
        rubble.Clear();
    }

    void Update()
    {
        if (Grabbed) return;
        if (Time.time - born > lifetime) { foreach (var g in rubble) if (g) Destroy(g); Destroy(gameObject); return; }
        var hp = GiantHealth.Instance;
        if (!hp || hp.IsDead || GiantShrink.Active || GameStartMenu.InMenu) return;
        if (Time.time - born < grabDelay) return; // 깔린 직후엔 바로 집지 못함
        float d = ItemDrops.PathDistance(transform.position); // 달리며 지나가도 놓치지 않게 지나간 길로 잼
        if (d < Mathf.Max(1f, hp.Height) * pickRangeRatio) HostageEvent.TryGrabVictim(this);
    }

    // 깔린 채 팔을 휘저으며 구해 달라는 몸짓 (애니메이터 적용 뒤 뼈 회전을 덧씌움)
    void LateUpdate()
    {
        if (Grabbed || !Anim || !Anim.isHuman) return;
        float tt = Time.time + GetInstanceID() * 0.37f;
        Rot(HumanBodyBones.LeftUpperArm, transform.up, -40f * Mathf.Sin(tt * 7f));
        Rot(HumanBodyBones.RightUpperArm, transform.up, 40f * Mathf.Sin(tt * 7f + 1.3f));
        Rot(HumanBodyBones.LeftLowerArm, transform.up, 25f * Mathf.Sin(tt * 11f));
        Rot(HumanBodyBones.RightLowerArm, transform.up, -25f * Mathf.Sin(tt * 11f + 0.7f));
        Rot(HumanBodyBones.Head, transform.right, 15f * Mathf.Sin(tt * 3f));
    }

    void Rot(HumanBodyBones b, Vector3 axis, float deg)
    {
        var tr = Anim.GetBoneTransform(b);
        if (tr) tr.rotation = Quaternion.AngleAxis(deg, axis) * tr.rotation;
    }
}
