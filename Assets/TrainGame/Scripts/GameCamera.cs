using UnityEngine;

namespace TrainGame
{
    /// <summary>
    /// 3인칭 오빗 카메라 ↔ 기관실 1인칭 카메라. 모드가 바뀌면 이전 시점에서 부드럽게 이어 붙인다.
    /// 1인칭에선 기관차를 기준으로 마우스로 둘러보고, 속도에 따라 레일 진동이 섞인다.
    /// </summary>
    public class GameCamera : MonoBehaviour
    {
        public static GameCamera Instance { get; private set; }

        public enum Mode { ThirdPerson, Cab }

        public Mode CurrentMode { get; private set; }
        public Transform target;
        public Transform cabAnchor;

        [Header("Third person")]
        public Vector3 pivotOffset = new Vector3(0f, 1.45f, 0f);
        public float shoulder = 0.35f;
        public float distance = 4.2f;
        public float minDistance = 1.6f, maxDistance = 14f;
        public float minPitch = -40f, maxPitch = 75f;
        public float thirdFov = 60f;

        [Header("Cab")]
        public float cabFov = 72f;
        public float maxCabYaw = 165f;
        public float minCabPitch = -55f, maxCabPitch = 60f;
        [Tooltip("창밖으로 몸을 내밀 때 시점 이동량 (cabAnchor 로컬, +X = 오른쪽 창)")]
        public Vector3 leanOffset = new Vector3(1.13f, -0.04f, 0.15f);

        [Header("Common")]
        public float sensitivity = 2.2f;
        public float blendTime = 0.7f;
        public LayerMask collisionMask = ~0;

        float yaw, pitch = 12f, curDist;
        float cabYaw, cabPitch = 4f;
        float lean01;
        bool leanToggle;

        /// <summary>기관실에서 창밖으로 몸을 내밀고 있는가 (우클릭 누르는 동안 또는 C 토글).</summary>
        public bool Leaning { get { return CurrentMode == Mode.Cab && (leanToggle || Input.GetMouseButton(1)); } }
        Vector3 smoothPivot;
        bool pivotInit;
        Camera cam;

        float blend = 1f;
        Vector3 fromPos;
        Quaternion fromRot;
        float fromFov;

        public float Yaw { get { return CurrentMode == Mode.Cab && cabAnchor != null ? cabAnchor.eulerAngles.y + cabYaw : yaw; } }
        public Camera Cam { get { return cam; } }

        void Awake()
        {
            Instance = this;
            cam = GetComponent<Camera>();
            curDist = distance;
            if (target != null) yaw = target.eulerAngles.y;
        }

        void Start()
        {
            SetCursor(true);
        }

        public void SetAngles(float newYaw, float newPitch)
        {
            yaw = newYaw;
            pitch = Mathf.Clamp(newPitch, minPitch, maxPitch);
        }

        void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void BeginBlend()
        {
            fromPos = transform.position;
            fromRot = transform.rotation;
            fromFov = cam != null ? cam.fieldOfView : thirdFov;
            blend = 0f;
        }

        public void EnterCab(Transform anchor)
        {
            BeginBlend();
            cabAnchor = anchor;
            cabYaw = 0f;
            cabPitch = 4f;
            lean01 = 0f;
            leanToggle = false;
            CurrentMode = Mode.Cab;
        }

        public void EnterThirdPerson(float facingYaw, Transform newTarget = null)
        {
            BeginBlend();
            if (newTarget != null) target = newTarget;
            yaw = facingYaw;
            pitch = 14f;
            curDist = distance;
            pivotInit = false;
            CurrentMode = Mode.ThirdPerson;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) SetCursor(false);
            else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked) SetCursor(true);
            if (CurrentMode == Mode.Cab && Input.GetKeyDown(KeyCode.C)) leanToggle = !leanToggle;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            bool look = Cursor.lockState == CursorLockMode.Locked;
            float mx = look ? Input.GetAxis("Mouse X") * sensitivity : 0f;
            float my = look ? Input.GetAxis("Mouse Y") * sensitivity : 0f;

            Vector3 pos; Quaternion rot; float fov;
            if (CurrentMode == Mode.Cab && cabAnchor != null) CabPose(dt, mx, my, out pos, out rot, out fov);
            else if (target != null) ThirdPose(dt, mx, my, look, out pos, out rot, out fov);
            else return;

            if (blend < 1f)
            {
                blend = Mathf.Min(1f, blend + dt / blendTime);
                float t = blend * blend * (3f - 2f * blend);
                pos = Vector3.Lerp(fromPos, pos, t);
                rot = Quaternion.Slerp(fromRot, rot, t);
                fov = Mathf.Lerp(fromFov, fov, t);
            }

            transform.SetPositionAndRotation(pos, rot);
            if (cam != null)
            {
                cam.fieldOfView = fov;
                cam.nearClipPlane = CurrentMode == Mode.Cab ? 0.04f : 0.15f;
            }
        }

        void ThirdPose(float dt, float mx, float my, bool look, out Vector3 pos, out Quaternion rot, out float fov)
        {
            yaw += mx;
            pitch = Mathf.Clamp(pitch - my, minPitch, maxPitch);
            if (look)
            {
                float wheel = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(wheel) > 0.001f) distance = Mathf.Clamp(distance - wheel * 5f, minDistance, maxDistance);
            }

            Vector3 rawPivot = target.position + pivotOffset;
            if (!pivotInit) { smoothPivot = rawPivot; pivotInit = true; }
            smoothPivot = Vector3.Lerp(smoothPivot, rawPivot, 1f - Mathf.Exp(-20f * dt));

            rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pivot = smoothPivot + rot * Vector3.right * shoulder;
            Vector3 back = -(rot * Vector3.forward);
            float allowed = distance;
            RaycastHit hit;
            if (Physics.SphereCast(pivot, 0.2f, back, out hit, distance + 0.3f, collisionMask, QueryTriggerInteraction.Ignore))
                allowed = Mathf.Max(0.4f, hit.distance - 0.15f);
            curDist = allowed < curDist ? allowed : Mathf.Lerp(curDist, allowed, 1f - Mathf.Exp(-5f * dt));
            pos = pivot + back * curDist;
            fov = thirdFov;
        }

        void CabPose(float dt, float mx, float my, out Vector3 pos, out Quaternion rot, out float fov)
        {
            cabYaw = Mathf.Clamp(cabYaw + mx, -maxCabYaw, maxCabYaw);
            cabPitch = Mathf.Clamp(cabPitch - my, minCabPitch, maxCabPitch);

            // 레일 진동 + 곡선에서 몸이 쏠리는 느낌
            var train = TrainController.Instance;
            float spd = train != null ? Mathf.Abs(train.Velocity) : 0f;
            float t = Time.time;
            float amp = Mathf.Clamp01(spd / 22f);
            Vector3 shake = new Vector3(
                (Mathf.PerlinNoise(t * 2.1f, 0.3f) - 0.5f) * 0.012f,
                (Mathf.PerlinNoise(0.7f, t * (6f + spd * 0.5f)) - 0.5f) * 0.03f,
                0f) * amp;
            float sway = train != null ? Mathf.Clamp(train.LateralAccel * 0.012f, -0.04f, 0.04f) : 0f;
            lean01 = Mathf.MoveTowards(lean01, Leaning ? 1f : 0f, dt * 2.8f);
            float l = lean01 * lean01 * (3f - 2f * lean01);

            pos = cabAnchor.TransformPoint(shake + new Vector3(sway, 0f, 0f) + leanOffset * l);
            rot = cabAnchor.rotation * Quaternion.Euler(cabPitch, cabYaw, (Mathf.PerlinNoise(t * 1.3f, 5f) - 0.5f) * 0.8f * amp);
            fov = cabFov;
        }
    }
}
