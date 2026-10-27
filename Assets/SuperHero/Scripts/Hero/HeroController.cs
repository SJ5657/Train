using UnityEngine;

namespace SuperHero
{
    /// <summary>
    /// 3인칭 슈퍼히어로 이동: 달리기/점프/전력질주 + 자유 비행(부스트) + 고속 돌진 시 건물 관통.
    /// 점프 중 스페이스 한 번 더, 또는 F 로 비행 전환. 비행 중 W/A/S/D는 카메라가 보는 방향으로 날아간다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class HeroController : MonoBehaviour
    {
        public static HeroController Instance { get; private set; }

        [Header("Refs")]
        public Transform modelPivot;
        public Animator animator;
        public HeroCamera cam;

        [Header("Ground")]
        public float runSpeed = 9f;
        public float sprintSpeed = 26f;
        public float jumpSpeed = 12f;
        public float gravity = 30f;
        public float groundAccel = 55f;
        public float airAccel = 16f;

        [Header("Flight")]
        public float cruiseSpeed = 42f;
        public float boostSpeed = 170f;
        public float ascendSpeed = 30f;
        public float flyAccel = 80f;
        public float boostAccel = 160f;
        public float hoverDecel = 45f;

        [Header("Ram (고속 돌진 파괴)")]
        public float ramSpeed = 60f;

        public bool IsFlying { get; private set; }
        public bool Grounded { get; private set; }
        public Vector3 Velocity { get { return velocity; } }
        public float Speed
        {
            get
            {
                if (Grounded && !IsFlying) return Mathf.Sqrt(velocity.x * velocity.x + velocity.z * velocity.z);
                return velocity.magnitude;
            }
        }
        /// <summary>0..1 카메라 FOV 연출용 (비행 속도 / 전력질주).</summary>
        public float SpeedBoost01 { get { return speedBoost01; } }
        public bool Boosting { get { return boost01 > 0.5f; } }

        /// <summary>스킬이 직접 속도를 지정(슬램 도약/급강하 등)할 때 켠다.</summary>
        public bool Driven { get; private set; }
        /// <summary>조준 중이면 몸이 카메라 방향을 본다.</summary>
        public bool AimLock { get; set; }
        public bool Frozen { get; set; }

        CharacterController cc;
        Vector3 velocity, drivenVelocity;
        float boost01, speedBoost01, pitchCur, twistCur, flySpeedAnim, yawRate;
        float groundedGrace;
        bool wasGrounded = true;
        float airTime;

        // 본 캐시
        public Transform Head, LeftHand, RightHand, LeftEye, RightEye, Chest;

        public Vector3 Center { get { return transform.position + Vector3.up * 0.95f; } }

        void Awake()
        {
            Instance = this;
            cc = GetComponent<CharacterController>();
            cc.radius = 0.35f; cc.height = 1.7f; cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.4f; cc.slopeLimit = 55f; cc.skinWidth = 0.03f;
            SetLayerRecursive(transform, GameLayers.Hero);
        }

        void Start()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Head = animator.GetBoneTransform(HumanBodyBones.Head);
                LeftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                RightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                LeftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
                RightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
                Chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
                if (Chest == null) Chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            }
            if (cam == null && Camera.main != null) cam = Camera.main.GetComponent<HeroCamera>();
        }

        static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayerRecursive(c, layer);
        }

        float CamYaw { get { return cam != null ? cam.Yaw : transform.eulerAngles.y; } }

        Vector3 CamForward { get { return cam != null ? cam.transform.forward : transform.forward; } }
        Vector3 CamRight { get { return cam != null ? cam.transform.right : transform.right; } }

        // ───────────────────────── 업데이트 ─────────────────────────

        void Update()
        {
            if (!SuperCityGenerator.Ready || Frozen) { UpdateAnimator(Time.deltaTime, 0f); return; }
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            if (Input.GetKeyDown(KeyCode.Backspace) && SuperCityGenerator.Instance != null) { Driven = false; Land(); Teleport(SuperCityGenerator.Instance.SpawnPoint); }
            if (transform.position.y < -80f) { transform.position = SuperCityGenerator.Instance != null ? SuperCityGenerator.Instance.SpawnPoint : Vector3.up * 3f; velocity = Vector3.zero; }

            if (Driven) UpdateDriven(dt);
            else if (IsFlying) UpdateFlight(dt);
            else UpdateGround(dt);

            UpdateAnimator(dt, Mathf.Sqrt(velocity.x * velocity.x + velocity.z * velocity.z));
            Sfx.SetWind(IsFlying ? Mathf.Clamp01(velocity.magnitude / 110f) : Mathf.Clamp01((Speed - 12f) / 40f) * 0.6f);
        }

        // ───────────────────────── 지상 ─────────────────────────

        void UpdateGround(float dt)
        {
            Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude > 1f) input.Normalize();
            Vector3 wish = Quaternion.Euler(0f, CamYaw, 0f) * new Vector3(input.x, 0f, input.y);
            bool sprint = Input.GetKey(KeyCode.LeftShift) && input.sqrMagnitude > 0.01f;

            float topSpeed = sprint ? sprintSpeed : runSpeed;
            Vector3 hv = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 targetV = wish * topSpeed;
            float accel = Grounded ? groundAccel * (sprint ? 1.8f : 1f) : airAccel;
            hv = Vector3.MoveTowards(hv, targetV, accel * dt);

            float vy = velocity.y;
            if (Grounded && vy < 0f) vy = -4f;
            vy -= gravity * dt;

            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (Grounded) { vy = jumpSpeed; Grounded = false; groundedGrace = 0.1f; }
                else StartFlight(false);
            }
            if (Input.GetKeyDown(KeyCode.F)) StartFlight(true);
            if (IsFlying) return;

            velocity = new Vector3(hv.x, Mathf.Max(vy, -70f), hv.z);
            speedBoost01 = Mathf.Lerp(speedBoost01, sprint ? 0.35f : 0f, 1f - Mathf.Exp(-6f * dt));
            boost01 = 0f;

            cc.Move(velocity * dt);
            bool g = cc.isGrounded;
            if (groundedGrace > 0f) { groundedGrace -= dt; g = false; }
            Grounded = g;
            if (Grounded && !wasGrounded && airTime > 0.6f) Fx.Dust(transform.position, 1.6f, 3, 2f);
            airTime = Grounded ? 0f : airTime + dt;
            wasGrounded = Grounded;

            // 이동 방향을 본다 (조준 중이면 카메라 방향)
            float targetYaw = AimLock ? CamYaw : (hv.sqrMagnitude > 1f ? Mathf.Atan2(hv.x, hv.z) * Mathf.Rad2Deg : transform.eulerAngles.y);
            float yaw = Mathf.LerpAngle(transform.eulerAngles.y, targetYaw, 1f - Mathf.Exp(-(AimLock ? 25f : 14f) * dt));
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            SmoothModelPose(dt, 0f, 0f);
        }

        // ───────────────────────── 비행 ─────────────────────────

        public void StartFlight(bool hop)
        {
            if (IsFlying) return;
            IsFlying = true;
            Grounded = false;
            if (hop || velocity.y < 6f) velocity.y = hop ? 9f : Mathf.Max(velocity.y, 5f);
            Fx.Ring(transform.position + Vector3.up * 0.1f, Vector3.up, 0.5f, 5f, 0.45f, new Color(1f, 0.95f, 0.6f, 0.7f));
            if (Grounded || hop) Fx.Dust(transform.position, 2f, 4, 3f);
        }

        public void Land()
        {
            if (!IsFlying) return;
            IsFlying = false;
            boost01 = 0f;
            velocity.x *= 0.3f; velocity.z *= 0.3f;
            if (velocity.y > 0f) velocity.y = 0f;
        }

        void UpdateFlight(float dt)
        {
            Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            Vector3 wish = CamForward * input.y + CamRight * input.x;
            float vert = (Input.GetKey(KeyCode.Space) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C) ? 1f : 0f);
            wish += Vector3.up * vert;
            bool hasInput = wish.sqrMagnitude > 0.01f;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            bool wantBoost = Input.GetKey(KeyCode.LeftShift) && hasInput;
            boost01 = Mathf.MoveTowards(boost01, wantBoost ? 1f : 0f, dt * (wantBoost ? 1.1f : 2.5f));
            float topSpeed = Mathf.Lerp(cruiseSpeed, boostSpeed, boost01);
            float vertScale = Mathf.Abs(vert) > 0.1f && input.sqrMagnitude < 0.01f ? ascendSpeed / topSpeed : 1f;

            Vector3 targetV = wish * topSpeed * vertScale;
            float accel = hasInput ? Mathf.Lerp(flyAccel, boostAccel, boost01) : hoverDecel;
            velocity = Vector3.MoveTowards(velocity, targetV, accel * dt);
            if (!hasInput && velocity.sqrMagnitude < 4f) velocity.y += Mathf.Sin(Time.time * 2.2f) * 0.6f * dt * 10f;   // 제자리 비행의 출렁임

            speedBoost01 = Mathf.Lerp(speedBoost01, Mathf.Clamp01(velocity.magnitude / boostSpeed) * (0.35f + 0.65f * boost01), 1f - Mathf.Exp(-5f * dt));

            if (Input.GetKeyDown(KeyCode.F)) { Land(); return; }

            MoveFlying(dt);

            // 낮게 날다가 땅에 닿으면 착지
            if (Grounded && vert <= 0f && velocity.magnitude < 28f && !wantBoost) { Land(); return; }
        }

        void UpdateDriven(float dt)
        {
            velocity = drivenVelocity;
            boost01 = Mathf.MoveTowards(boost01, 1f, dt * 3f);
            speedBoost01 = Mathf.Lerp(speedBoost01, 1f, 1f - Mathf.Exp(-6f * dt));
            MoveFlying(dt);
        }

        void MoveFlying(float dt)
        {
            TryRam(dt);
            var flags = cc.Move(velocity * dt);
            Grounded = (flags & CollisionFlags.Below) != 0;
            if (flags != CollisionFlags.None && !Driven)
                velocity = Vector3.Lerp(velocity, cc.velocity, 0.6f);
            UpdateFlightOrientation(dt);
        }

        void UpdateFlightOrientation(float dt)
        {
            Vector3 hv = new Vector3(velocity.x, 0f, velocity.z);
            float spd = velocity.magnitude;
            float curYaw = transform.eulerAngles.y;
            float targetYaw = (hv.magnitude > 2.5f && !AimLock) ? Mathf.Atan2(hv.x, hv.z) * Mathf.Rad2Deg : CamYaw;
            float newYaw = Mathf.LerpAngle(curYaw, targetYaw, 1f - Mathf.Exp(-(AimLock ? 25f : 9f) * dt));
            yawRate = Mathf.DeltaAngle(curYaw, newYaw) / Mathf.Max(dt, 0.0001f);
            transform.rotation = Quaternion.Euler(0f, newYaw, 0f);

            float lean = AimLock ? 0f : Mathf.InverseLerp(4f, 22f, spd);
            float elev = Mathf.Atan2(velocity.y, hv.magnitude) * Mathf.Rad2Deg;
            float targetPitch = Mathf.Lerp(0f, Mathf.Clamp(90f - elev, 12f, 170f), lean);
            float targetTwist = Mathf.Clamp(-yawRate * 0.12f, -35f, 35f) * lean;
            SmoothModelPose(dt, targetPitch, targetTwist);
        }

        void SmoothModelPose(float dt, float targetPitch, float targetTwist)
        {
            pitchCur = Mathf.Lerp(pitchCur, targetPitch, 1f - Mathf.Exp(-9f * dt));
            twistCur = Mathf.Lerp(twistCur, targetTwist, 1f - Mathf.Exp(-6f * dt));
            if (modelPivot != null) modelPivot.localRotation = Quaternion.Euler(pitchCur, 0f, 0f) * Quaternion.Euler(0f, twistCur, 0f);
        }

        // ───────────────────────── 고속 돌진 파괴 ─────────────────────────

        void TryRam(float dt)
        {
            float spd = velocity.magnitude;
            if (spd < ramSpeed) return;
            Vector3 dir = velocity / spd;
            RaycastHit hit;
            if (!Physics.SphereCast(Center, 0.6f, dir, out hit, spd * dt + 2.5f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore)) return;
            var b = hit.collider.GetComponentInParent<DestructibleBuilding>();
            if (b == null) return;
            float power = Mathf.InverseLerp(ramSpeed, boostSpeed, spd);
            int broke = DestructionManager.Explode(hit.point + dir * 1.5f, 5.5f + power * 5f, 280f + power * 500f, 16f + power * 18f);
            if (broke > 0)
            {
                velocity *= 1f - 0.08f * Mathf.Min(broke, 6) * 0.5f;
                Fx.Sparks(hit.point, -dir, 8, 20f);
                Fx.Dust(hit.point, 5f, 3, 6f);
                Fx.Shake(hit.point, 0.45f, 200f);
            }
        }

        // ───────────────────────── 스킬용 API ─────────────────────────

        public void BeginDriven(Vector3 vel) { Driven = true; IsFlying = true; drivenVelocity = vel; velocity = vel; }
        public void SetDrivenVelocity(Vector3 vel) { drivenVelocity = vel; }
        public void EndDriven(bool keepFlying)
        {
            Driven = false;
            if (!keepFlying) { IsFlying = false; velocity.x *= 0.2f; velocity.z *= 0.2f; velocity.y = 0f; }
        }

        public void Impulse(Vector3 v) { velocity += v; }

        public void Teleport(Vector3 p)
        {
            cc.enabled = false; transform.position = p; cc.enabled = true; velocity = Vector3.zero;
        }

        // ───────────────────────── 애니메이터 ─────────────────────────

        void UpdateAnimator(float dt, float hSpeed)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            float flySpeed = IsFlying ? Mathf.Clamp01(Mathf.InverseLerp(2f, 24f, velocity.magnitude)) : 0f;
            flySpeedAnim = Mathf.Lerp(flySpeedAnim, flySpeed, 1f - Mathf.Exp(-8f * dt));
            animator.SetFloat("Speed", hSpeed / runSpeed, 0.06f, dt);
            animator.SetFloat("FlySpeed", flySpeedAnim);
            animator.SetBool("Flying", IsFlying);
            animator.SetBool("Grounded", Grounded || (!IsFlying && airTime < 0.12f));
            animator.speed = IsFlying ? 1f : Mathf.Clamp(hSpeed / runSpeed, 1f, 1.5f);
        }
    }
}
