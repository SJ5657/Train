using UnityEngine;

// 방귀 가스에 휩싸인 탈것의 오작동: AI를 멈추고 몇 초 동안 덜덜 떨며 빙글빙글 돌고(나는 것은 비틀거리며 떨어짐),
// 연기를 뿜다가 끝나면 onDone(폭발·파괴)을 부름.
public class Malfunction : MonoBehaviour
{
    System.Action onDone;
    float until, spin, shake, fall;
    bool flying;
    Vector3 basePos;
    float nextSmoke;

    public static void Begin(GameObject target, MonoBehaviour ai, bool flying, float seconds, System.Action done)
    {
        if (!target || target.GetComponent<Malfunction>()) return;
        if (ai) ai.enabled = false; // 원래 움직임·공격 멈춤
        var m = target.AddComponent<Malfunction>();
        m.onDone = done; m.until = Time.time + seconds; m.flying = flying;
        m.spin = Random.Range(120f, 320f) * (Random.value < 0.5f ? -1f : 1f);
        m.shake = Mathf.Max(0.2f, target.transform.lossyScale.x * 0.15f);
        m.basePos = target.transform.position;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        // 덜덜 떨면서 제자리에서 돌거나(땅) 비틀거리며 떨어짐(하늘)
        transform.Rotate(0f, spin * dt, 0f, Space.World);
        if (flying)
        {
            fall += 9.81f * 0.6f * dt;
            basePos += Vector3.down * fall * dt;
            transform.Rotate(Random.Range(-40f, 40f) * dt, 0f, Random.Range(-60f, 60f) * dt, Space.Self);
            if (basePos.y < 1f) until = 0f; // 땅에 닿으면 바로 터짐
        }
        transform.position = basePos + Random.insideUnitSphere * shake;
        if (Time.time >= nextSmoke)
        {
            nextSmoke = Time.time + 0.25f;
            CombatFX.Muzzle(transform.position + Vector3.up * shake * 3f, Vector3.up, shake * 6f);
        }
        if (Time.time >= until)
        {
            var d = onDone; onDone = null;
            Destroy(this);
            d?.Invoke();
        }
    }
}
