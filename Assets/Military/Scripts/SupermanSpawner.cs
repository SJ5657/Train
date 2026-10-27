using System.Collections;
using UnityEngine;

// 슈퍼맨 등장: 게임 시작 후 firstDelay초가 지나면 멀리서 날아옴. 한 번에 한 명. 패링당해 날아가면 respawnDelay초 뒤 다시 등장.
// 패링 성공 순간의 슬로모션도 여기서 처리 (슈퍼맨이 사라져도 시간이 되돌아오도록).
public class SupermanSpawner : MonoBehaviour
{
    static SupermanSpawner instance;

    [Tooltip("슈퍼맨 모델 (Assets/Models/SuperMan.vrm)")] public GameObject supermanPrefab;
    [Tooltip("떠 있을 때 애니메이션 (허리에 손 얹은 자세)")] public RuntimeAnimatorController animator;
    [Tooltip("게임 시작 후 처음 등장까지(초)")] public float firstDelay = 40f;
    [Tooltip("패링당해 날아간 뒤 다시 등장까지(초)")] public float respawnDelay = 180f;
    [Tooltip("등장 위치: 거인에게서 이 거리(거인 키 배수)만큼 떨어진 하늘")] public float spawnDistanceRatio = 10f;

    float nextAt = -1f;

    void Awake() { instance = this; }

    void Update()
    {
        if (GameStartMenu.InMenu || !supermanPrefab) return;
        var g = GiantHealth.Instance;
        if (!g || g.IsDead || GiantShrink.Active) return;
        if (nextAt < 0f) nextAt = Time.time + firstDelay;
        if (SupermanAI.Active || Time.time < nextAt || GiantHome.GiantSafe) return;
        Spawn(g);
    }

    void Spawn(GiantHealth g)
    {
        float H = Mathf.Max(5f, g.Height);
        float a = Random.Range(0f, Mathf.PI * 2f);
        Vector3 pos = g.transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * H * spawnDistanceRatio + Vector3.up * H * 2.5f;
        var go = Instantiate(supermanPrefab, pos, Quaternion.identity);
        go.name = "Superman";
        var anim = go.GetComponentInChildren<Animator>();
        if (anim) { anim.runtimeAnimatorController = animator; anim.applyRootMotion = false; }
        go.AddComponent<SupermanAI>().Init(H);
        nextAt = float.MaxValue; // 죽을 때 다시 정함
    }

    public static void OnSupermanDied() { if (instance) instance.nextAt = Time.time + instance.respawnDelay; }

    // 슬로모션(패링 성공 순간): realSeconds초(실제 시간) 동안 시간 배율을 scale로
    public static void SlowMo(float realSeconds, float scale)
    {
        if (instance) instance.StartCoroutine(instance.SlowMoRoutine(realSeconds, scale));
    }

    IEnumerator SlowMoRoutine(float realSeconds, float scale)
    {
        if (Time.timeScale < 0.99f) yield break; // 이미 멈춰 있거나 느린 상태(일시정지 등)면 건드리지 않음
        Time.timeScale = scale;
        float set = scale;
        yield return new WaitForSecondsRealtime(realSeconds * 0.7f);
        // 마지막 30%는 원래 속도로 부드럽게 돌아옴
        float rampEnd = Time.unscaledTime + realSeconds * 0.3f, start = Time.unscaledTime;
        while (Time.unscaledTime < rampEnd)
        {
            if (!Mathf.Approximately(Time.timeScale, set)) yield break; // 그 사이 일시정지·메뉴 등으로 바뀌었으면 그대로 둠
            set = Mathf.Lerp(scale, 1f, (Time.unscaledTime - start) / (rampEnd - start));
            Time.timeScale = set;
            yield return null;
        }
        if (Mathf.Approximately(Time.timeScale, set)) Time.timeScale = 1f;
    }
}
