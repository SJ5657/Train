using UnityEngine;

// 카메라에 붙어 OnRenderImage를 ScreenOutline으로 넘김
[RequireComponent(typeof(Camera))]
public class ScreenOutlineHook : MonoBehaviour
{
    [HideInInspector] public ScreenOutline owner;
    // 불투명 물체까지만 그린 뒤에 윤곽선을 입힘 → 그 뒤에 그려지는 반투명·맨 위 오버레이(스트리머 모드 검은 네모 등)에는 선이 생기지 않음
    [ImageEffectOpaque]
    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (owner && owner.isActiveAndEnabled) owner.Render(src, dst);
        else Graphics.Blit(src, dst);
    }
}
