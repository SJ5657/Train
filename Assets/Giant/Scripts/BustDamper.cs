using System.Collections.Generic;
using UnityEngine;

// 여성 거인 가슴(J_Sec_*_Bust*) 흔들림 줄이기.
// VRM 스프링 본 설정(stiffness·drag)을 바꿔도 달릴 때의 흔들림이 거의 줄지 않아서,
// 스프링 계산이 끝난 뒤(LateUpdate) 각 가슴 뼈의 회전을 처음 자세 쪽으로 되돌려 흔들림 폭을 줄임.
[DefaultExecutionOrder(31000)] // VRM 스프링 본 계산 뒤에 실행
public class BustDamper : MonoBehaviour
{
    [Tooltip("가슴 흔들림 크기 (1 = 원래대로, 0 = 전혀 안 흔들림)")] [Range(0f, 1f)] public float jiggle = 0.3f;
    [Tooltip("이 이름이 들어간 뼈를 대상으로 함")] public string boneNameContains = "Bust";

    readonly List<Transform> bones = new List<Transform>();
    readonly List<Quaternion> rest = new List<Quaternion>();

    void Awake()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("J_Sec_") && t.name.Contains(boneNameContains)) { bones.Add(t); rest.Add(t.localRotation); }
    }

    void LateUpdate()
    {
        if (jiggle >= 1f) return;
        for (int i = 0; i < bones.Count; i++)
            if (bones[i]) bones[i].localRotation = Quaternion.Slerp(rest[i], bones[i].localRotation, jiggle);
    }
}
