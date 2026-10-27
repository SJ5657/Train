using UnityEngine;

// 거인 손에서 빠져나와 땅으로 떨어지는 사람: 포물선으로 빙글빙글 떨어져 바닥에 닿으면 죽음 (엎어진 채 피가 튀고 잠시 뒤 사라짐).
public class FallingVictim : MonoBehaviour
{
    Animator anim;
    Vector3 vel, awayFrom;
    float height, landedAt = -1f, spin;
    static readonly int SpeedHash = Animator.StringToHash("Speed");
    int groundMask;

    public void Init(Animator a, float h, Vector3 push, Vector3 giantPos)
    {
        anim = a; height = Mathf.Max(0.5f, h); awayFrom = giantPos;
        vel = push + Vector3.up * 1.5f;
        spin = Random.Range(-200f, 200f);
        groundMask = LayerMask.GetMask("Default");
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (landedAt < 0f)
        {
            // 떨어지는 중: 중력 + 빙글빙글 돌며 버둥
            vel += Vector3.down * 9.81f * 2f * dt;
            Vector3 p = transform.position + vel * dt;
            float ground = Physics.Raycast(new Vector3(p.x, p.y + 50f, p.z), Vector3.down, out RaycastHit hit, 200f, groundMask, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
            transform.Rotate(Vector3.right, spin * dt, Space.Self);
            if (anim) anim.SetFloat(SpeedHash, 1f);
            if (p.y <= ground && vel.y < 0f)
            {
                p.y = ground; landedAt = Time.time;
                Vector3 f = transform.forward; f.y = 0f;
                // 바닥에 떨어져 죽음: 엎어진 채 움직이지 않고, 피가 튐
                transform.rotation = Quaternion.LookRotation(f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward) * Quaternion.Euler(90f, 0f, 0f);
                p.y = ground + height * 0.06f;
                BloodSplat.Spawn(new Vector3(p.x, ground, p.z), height / 4f);
                if (anim) anim.enabled = false;
                Destroy(gameObject, 8f);
            }
            transform.position = p;
        }
    }
}
