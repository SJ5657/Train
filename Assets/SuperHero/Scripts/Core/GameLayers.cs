using UnityEngine;

namespace SuperHero
{
    /// <summary>레이어 번호 조회 (Building / Debris는 기존 프로젝트 레이어, Hero는 셋업 때 추가됨).</summary>
    public static class GameLayers
    {
        static int building = -2, debris = -2, hero = -2;

        static int Find(string layerName)
        {
            int l = LayerMask.NameToLayer(layerName);
            return l < 0 ? 0 : l;
        }

        public static int Building { get { if (building == -2) building = Find("Building"); return building; } }
        public static int Debris { get { if (debris == -2) debris = Find("Debris"); return debris; } }
        public static int Hero { get { if (hero == -2) hero = Find("Hero"); return hero; } }

        /// <summary>빔 / 카메라 / 조준이 맞는 월드 (지면 + 건물). 영웅과 파편은 제외.</summary>
        public static int WorldMask { get { return (1 << 0) | (1 << Building); } }

        /// <summary>폭발이 밀어낼 수 있는 물리 오브젝트 (자동차 + 파편).</summary>
        public static int PushMask { get { return (1 << 0) | (1 << Debris); } }

        /// <summary>물리 레이어 충돌 설정 (영웅-파편, 파편-파편 무시). 레이어가 모두 따로 있을 때만 적용.</summary>
        public static void ConfigurePhysics()
        {
            int h = Hero, d = Debris;
            if (d != 0) Physics.IgnoreLayerCollision(d, d, true);
            if (h != 0 && d != 0) Physics.IgnoreLayerCollision(h, d, true);
        }
    }
}
