using UnityEngine;

// OnAnimatorIK는 Animator가 붙은 오브젝트(거인 모델)에서만 호출되므로, 그 호출을 GiantEat으로 넘겨줌.
// GiantEat이 현재 모델에 자동으로 붙임 (성별 선택으로 모델이 바뀌어도 대응).
public class GiantIKRelay : MonoBehaviour
{
    [HideInInspector] public GiantEat owner;
    Animator anim;

    void Awake() { anim = GetComponent<Animator>(); }

    void OnAnimatorIK(int layerIndex)
    {
        if (owner && anim) owner.OnIK(anim);
        if (HostageEvent.Instance && anim) HostageEvent.Instance.OnIK(anim); // 인질 이벤트의 손 동작 (GiantEat 다음에 덮어씀, 왼손)
        if (Flagpoles.Instance && anim) Flagpoles.Instance.OnIK(anim);       // 국기 게양대 휘두르기 (오른손)
        if (anim) GiantPalmStrike.OnIK(anim);                                 // 슈퍼맨 패링 손바닥 치기 (왼손, 맨 마지막에 덮어씀)
    }
}
