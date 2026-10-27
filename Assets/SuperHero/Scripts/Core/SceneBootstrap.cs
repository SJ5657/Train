using UnityEngine;

namespace SuperHero
{
    /// <summary>씬 시작 시 품질/물리 설정과 필수 매니저(FX, 파괴)를 준비한다.</summary>
    public class SceneBootstrap : MonoBehaviour
    {
        void Awake()
        {
            QualitySettings.shadowDistance = 450f;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowCascade4Split = new Vector3(0.04f, 0.14f, 0.4f);
            QualitySettings.lodBias = 2f;
            QualitySettings.maxQueuedFrames = 2;
            Time.fixedDeltaTime = 1f / 60f;
            Physics.defaultSolverIterations = 8;
            Physics.autoSyncTransforms = false;

            // 매니저 보장 (Awake에서 자동 생성)
            var dm = DestructionManager.Instance;
            var fx = Fx.Instance;
            DynamicGI.UpdateEnvironment();
        }
    }
}
