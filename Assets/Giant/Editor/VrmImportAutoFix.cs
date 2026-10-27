using UnityEditor;
using UnityEngine;

// 프로젝트를 처음 열 때 VRM이 셰이더(MToon)보다 먼저 임포트되면 'Value cannot be null. Parameter name: Shader' 오류로
// 모델 임포트가 실패함. 에디터 로드가 끝난 뒤 실패한 VRM을 자동으로 다시 임포트해서 복구.
[InitializeOnLoad]
static class VrmImportAutoFix
{
    static VrmImportAutoFix()
    {
        EditorApplication.delayCall += FixFailedVrms;
    }

    static void FixFailedVrms()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach (var guid in AssetDatabase.FindAssets("t:DefaultAsset", new[] { "Assets" }))
            TryFix(AssetDatabase.GUIDToAssetPath(guid));
        foreach (var path in System.IO.Directory.GetFiles("Assets", "*.vrm", System.IO.SearchOption.AllDirectories))
            TryFix(path.Replace('\\', '/'));
    }

    static void TryFix(string path)
    {
        if (!path.EndsWith(".vrm", System.StringComparison.OrdinalIgnoreCase)) return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return; // 정상 임포트됨
        Debug.Log($"[VrmImportAutoFix] 임포트에 실패한 VRM을 다시 임포트합니다: {path}");
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }
}
