using UnityEngine;

// 축소된 거인 주변을 지나가는 군중 한 명: 정해진 방향으로 곧게 걷거나 뛰어감 (GiantShrink가 만들고 치움).
public class CrowdWalker : MonoBehaviour
{
    public Vector3 dir = Vector3.forward;
    public float speed = 3f;
    float animValue;
    Animator anim;
    static readonly int SpeedHash = Animator.StringToHash("Speed");

    public void Init(Vector3 direction, float moveSpeed, Animator a, bool run)
    {
        dir = direction; dir.y = 0f; dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
        speed = moveSpeed; anim = a; animValue = run ? 1f : 0.5f;
        transform.rotation = Quaternion.LookRotation(dir);
    }

    void Update()
    {
        transform.position += dir * speed * Time.deltaTime;
        if (anim) anim.SetFloat(SpeedHash, animValue);
    }
}
