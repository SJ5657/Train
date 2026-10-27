using UnityEngine;

namespace TrainGame
{
    /// <summary>한글이 잘 나오는 OS 폰트(맑은 고딕 등)를 한 번만 만들어 HUD와 표지판이 같이 쓴다.</summary>
    public static class KoreanFont
    {
        static Font font;

        public static Font Get()
        {
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Dotum", "Arial" }, 64);
            return font;
        }
    }

    /// <summary>
    /// 역 표지판 TextMesh: 실행 시 한글 OS 폰트로 바꾸고, 동적 폰트 아틀라스가 다시 만들어질 때마다
    /// 깊이 테스트용 재질(TrainGame/Text3D)의 텍스처를 맞춘다.
    /// </summary>
    [RequireComponent(typeof(TextMesh))]
    public class SignText : MonoBehaviour
    {
        TextMesh tm;
        Material mat;

        void Awake()
        {
            tm = GetComponent<TextMesh>();
            var r = GetComponent<MeshRenderer>();
            mat = r.material;
            tm.font = KoreanFont.Get();
            tm.font.RequestCharactersInTexture(tm.text, tm.fontSize);
            Font.textureRebuilt += OnRebuilt;
            Sync();
        }

        void OnDestroy()
        {
            Font.textureRebuilt -= OnRebuilt;
        }

        void OnRebuilt(Font f)
        {
            if (tm != null && f == tm.font) Sync();
        }

        void Sync()
        {
            if (tm.font != null && mat != null) mat.mainTexture = tm.font.material.mainTexture;
        }
    }
}
