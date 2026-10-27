using UnityEngine;

// 거인을 뺀 모든 적(탱크·헬기·전투기·경찰·핵 트럭·날아가는 포탄/폭탄)을 잠깐 멈추는 효과 (보급상자). 각 적 스크립트가 Update 맨 앞에서 Active를 확인함.
public static class EnemyFreeze
{
    public static float Until;
    public static bool Active => Time.time < Until;
    public static float Left => Mathf.Max(0f, Until - Time.time);

    public static void Freeze(float seconds) { Until = Time.time + seconds; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Until = 0f; }
}
