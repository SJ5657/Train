using UniVRM10;
using UnityEngine;
using UnityEngine.Rendering;

namespace TrainGame
{
    /// <summary>
    /// 기관사(VRM) 3인칭 이동 + 기관차 탑승/하차.
    /// 탑승하면 기관실 안에 서서(몸은 그림자만 보이게) 카메라가 1인칭으로 바뀌고, 열차 조작이 켜진다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class DriverController : MonoBehaviour
    {
        public static DriverController Instance { get; private set; }

        [Header("Refs")]
        public Animator animator;
        public GameCamera cam;
        public TrainController train;
        public Vrm10Instance vrm;

        [Header("Move")]
        public float walkSpeed = 1.7f;
        public float runSpeed = 5.2f;
        public float jumpSpeed = 4.8f;
        public float gravity = 18f;
        public float accel = 14f;

        [Header("Cab")]
        [Tooltip("기관차 로컬 좌표: 기관실 바닥에서 기관사가 서는 곳")] public Vector3 cabStandLocal = new Vector3(-0.55f, 0.6f, -0.42f);
        [Tooltip("기관차 로컬 좌표: 기관실 좌/우 출입구")] public Vector3 doorLocal = new Vector3(-0.4f, 1.0f, 1.32f);
        public float boardDistance = 3.4f;

        [Header("Look (MToon 반사광 조절, 1 = 원본)")]
        [Range(0f, 1f)] public float matcapStrength = 0.15f;
        [Range(0f, 1f)] public float rimStrength = 0.3f;
        [Range(0f, 1f)] public float emissionStrength = 0.25f;
        [Tooltip("그림자 쪽 색 배율 (원본 그림자색이 거의 흰색이라 음영이 안 생김)")]
        public Color shadeTint = new Color(0.72f, 0.7f, 0.76f, 1f);
        [Range(0f, 1f)] public float giEqualization = 0.5f;
        [Tooltip("옷 밑 피부를 지우는 판정 거리 (m). 살이 계속 뚫고 나오면 키운다")]
        public float skinCoverDistance = 0.05f;

        public bool InCab { get; private set; }
        public bool CanBoard { get; private set; }
        public bool CanExit { get { return InCab && train != null && train.SpeedKmh < 1f; } }
        public float Speed { get; private set; }

        CharacterController cc;
        Vector3 velocity;
        bool grounded;
        Renderer[] renderers;
        int playerLayer;

        void Awake()
        {
            Instance = this;
            cc = GetComponent<CharacterController>();
            cc.radius = 0.3f; cc.height = 1.62f; cc.center = new Vector3(0f, 0.83f, 0f);
            cc.stepOffset = 0.35f; cc.slopeLimit = 50f; cc.skinWidth = 0.03f;
            playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0) SetLayerRecursive(transform, playerLayer);
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        void Start()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null) { animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
            if (vrm == null) vrm = GetComponentInChildren<Vrm10Instance>();
            if (train == null) train = TrainController.Instance;
            if (cam == null && Camera.main != null) cam = Camera.main.GetComponent<GameCamera>();
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                SkinUnderClothCuller.Apply(smr, skinCoverDistance);
            ToneDownShine();
        }

        /// <summary>VRM 재질의 매트캡/림라이트/발광이 야외 조명에서 너무 번들거려 세기를 낮춘다.</summary>
        void ToneDownShine()
        {
            foreach (var r in renderers)
            {
                if (r == null) continue;
                foreach (var m in r.materials)
                {
                    if (m.HasProperty("_MatcapColor")) m.SetColor("_MatcapColor", m.GetColor("_MatcapColor") * matcapStrength);
                    if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", m.GetColor("_RimColor") * rimStrength);
                    if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", m.GetColor("_EmissionColor") * emissionStrength);
                    if (m.HasProperty("_ShadeColor")) m.SetColor("_ShadeColor", m.GetColor("_ShadeColor") * shadeTint);
                    if (m.HasProperty("_GiEqualization")) m.SetFloat("_GiEqualization", giEqualization);
                }
            }
        }

        static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayerRecursive(c, layer);
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (InCab) UpdateCab();
            else UpdateWalk(dt);
            UpdateAnimator(dt);
        }

        // ───────────────────────── 걷기 (3인칭) ─────────────────────────

        void UpdateWalk(float dt)
        {
            Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude > 1f) input.Normalize();
            float camYaw = cam != null ? cam.Yaw : transform.eulerAngles.y;
            Vector3 wish = Quaternion.Euler(0f, camYaw, 0f) * new Vector3(input.x, 0f, input.y);
            bool run = Input.GetKey(KeyCode.LeftShift);
            float top = run ? runSpeed : walkSpeed;

            Vector3 hv = new Vector3(velocity.x, 0f, velocity.z);
            hv = Vector3.MoveTowards(hv, wish * top, accel * dt);
            float vy = velocity.y;
            if (grounded && vy < 0f) vy = -3f;
            vy -= gravity * dt;
            if (grounded && Input.GetKeyDown(KeyCode.Space)) vy = jumpSpeed;
            velocity = new Vector3(hv.x, Mathf.Max(vy, -40f), hv.z);

            cc.Move(velocity * dt);
            grounded = cc.isGrounded;
            Speed = hv.magnitude;

            if (hv.sqrMagnitude > 0.05f)
            {
                float yaw = Mathf.LerpAngle(transform.eulerAngles.y, Mathf.Atan2(hv.x, hv.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-12f * dt));
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            if (transform.position.y < -20f) Teleport(new Vector3(transform.position.x, 3f, transform.position.z));

            CanBoard = train != null && NearestDoorDistance() < boardDistance;
            if (CanBoard && Input.GetKeyDown(KeyCode.F)) Board();
        }

        float NearestDoorDistance()
        {
            var loco = train.Loco;
            Vector3 a = loco.TransformPoint(doorLocal);
            Vector3 b = loco.TransformPoint(new Vector3(doorLocal.x, doorLocal.y, -doorLocal.z));
            Vector3 p = transform.position + Vector3.up * 1f;
            return Mathf.Min(Vector3.Distance(p, a), Vector3.Distance(p, b));
        }

        // ───────────────────────── 기관실 (1인칭) ─────────────────────────

        void UpdateCab()
        {
            CanBoard = false;
            Speed = 0f;
            if (!Input.GetKeyDown(KeyCode.F)) return;
            if (CanExit) Exit();
            else if (RouteManager.Instance != null) RouteManager.Instance.Show("열차가 완전히 멈춘 뒤에 내릴 수 있습니다", new Color(1f, 0.8f, 0.5f), 2.5f);
        }

        public void Board()
        {
            if (InCab || train == null) return;
            InCab = true;
            cc.enabled = false;
            velocity = Vector3.zero;
            transform.SetParent(train.Loco, false);
            transform.localPosition = cabStandLocal;
            transform.localRotation = Quaternion.Euler(0f, 90f, 0f);   // 기관차 +X(진행방향)를 본다
            SetBodyVisible(false);
            ResetSprings();
            train.Driving = true;
            if (cam != null) cam.EnterCab(train.cabAnchor);
            if (RouteManager.Instance != null)
                RouteManager.Instance.Show("기관실 탑승 — D: 리버서 전진, Q: 브레이크 풀기, W: 레귤레이터 열기", new Color(0.7f, 0.95f, 1f), 7f);
        }

        public void Exit()
        {
            if (!InCab) return;
            var loco = train.Loco;
            train.Driving = false;
            train.Secure();

            // 승강장 쪽(더 높은 바닥)으로 내린다
            Vector3 best = Vector3.zero; float bestY = float.MinValue; float side = 1f;
            foreach (float sd in new[] { 1f, -1f })
            {
                Vector3 p = loco.TransformPoint(new Vector3(doorLocal.x, 0f, sd * (doorLocal.z + 1.1f)));
                float y = GroundHeight(p);
                if (y > bestY + 0.05f) { bestY = y; best = new Vector3(p.x, y, p.z); side = sd; }
            }

            InCab = false;
            transform.SetParent(null, true);
            Vector3 outward = loco.TransformDirection(new Vector3(0f, 0f, side));
            outward.y = 0f;
            transform.SetPositionAndRotation(best + Vector3.up * 0.05f, Quaternion.LookRotation(outward.normalized, Vector3.up));
            cc.enabled = true;
            velocity = Vector3.zero;
            grounded = true;
            SetBodyVisible(true);
            ResetSprings();
            if (cam != null) cam.EnterThirdPerson(transform.eulerAngles.y + 180f, transform);
        }

        float GroundHeight(Vector3 p)
        {
            int mask = ~((playerLayer >= 0 ? 1 << playerLayer : 0) | (1 << 2));
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(p.x, p.y + 4f, p.z), Vector3.down, 12f, mask, QueryTriggerInteraction.Ignore);
            float y = 0f; bool any = false;
            foreach (var h in hits)
            {
                if (IsTrainPart(h.collider.transform)) continue;
                if (h.point.y > p.y + 2.5f) continue;   // 승강장 지붕 등은 무시
                if (!any || h.point.y > y) { y = h.point.y; any = true; }
            }
            return any ? y : 0f;
        }

        bool IsTrainPart(Transform t)
        {
            foreach (var v in train.vehicles) if (v.root != null && t.IsChildOf(v.root)) return true;
            return false;
        }

        void SetBodyVisible(bool visible)
        {
            foreach (var r in renderers)
                if (r != null) r.shadowCastingMode = visible ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
        }

        void ResetSprings()
        {
            if (vrm == null) return;
            try { vrm.Runtime.SpringBone.RestoreInitialTransform(); } catch (System.Exception) { }
        }

        public void Teleport(Vector3 p)
        {
            cc.enabled = false;
            transform.position = p;
            cc.enabled = !InCab;
            velocity = Vector3.zero;
            ResetSprings();
        }

        void UpdateAnimator(float dt)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            animator.SetFloat("Speed", Speed, 0.08f, dt);
            animator.SetBool("Grounded", InCab || grounded);
        }
    }
}
