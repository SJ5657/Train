using UnityEngine;

namespace SuperHero
{
    /// <summary>3인칭 오빗 카메라. 마우스로 회전, 휠로 거리, 건물에 파고들지 않고, 속도에 따라 FOV가 벌어진다.</summary>
    public class HeroCamera : MonoBehaviour
    {
        public static HeroCamera Instance { get; private set; }
        static float shake;
        /// <summary>마우스를 다시 잠근 프레임 (그 프레임의 클릭은 스킬로 쓰지 않는다).</summary>
        public static int LockFrame = -1;

        public Transform target;
        public Vector3 pivotOffset = new Vector3(0f, 1.55f, 0f);
        public float shoulder = 0.55f;
        public float distance = 6.5f;
        public float minDistance = 2.5f, maxDistance = 16f;
        public float sensitivity = 2.4f;
        public float minPitch = -70f, maxPitch = 80f;
        public float baseFov = 65f;
        public float maxFovBoost = 20f;
        public bool lockCursor = true;

        float yaw, pitch = 12f, curDist, fovExtra;
        Camera cam;
        HeroController hero;
        Vector3 smoothPivot;
        bool inited;

        public float Yaw { get { return yaw; } }
        public float Pitch { get { return pitch; } }
        public Camera Cam { get { return cam; } }

        public static void AddShake(float amount) { shake = Mathf.Min(shake + amount, 2.2f); }

        public void SetAngles(float newYaw, float newPitch) { yaw = newYaw; pitch = Mathf.Clamp(newPitch, minPitch, maxPitch); }

        void Awake()
        {
            Instance = this;
            cam = GetComponent<Camera>();
            curDist = distance;
        }

        void Start()
        {
            if (target != null) hero = target.GetComponentInParent<HeroController>();
            if (cam != null) { cam.fieldOfView = baseFov; cam.nearClipPlane = 0.2f; cam.farClipPlane = 6000f; }
            SetCursor(lockCursor);
        }

        void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            if (locked) LockFrame = Time.frameCount;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) SetCursor(false);
            else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked) SetCursor(true);
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (hero == null) hero = target.GetComponentInParent<HeroController>();

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                yaw += Input.GetAxis("Mouse X") * sensitivity;
                pitch -= Input.GetAxis("Mouse Y") * sensitivity;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
                float wheel = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(wheel) > 0.001f) distance = Mathf.Clamp(distance - wheel * 6f, minDistance, maxDistance);
            }

            Vector3 rawPivot = target.position + pivotOffset;
            if (!inited) { smoothPivot = rawPivot; inited = true; }
            // 빠른 비행 중에도 따라붙도록 피벗을 거의 즉시 따라가게 한다
            smoothPivot = Vector3.Lerp(smoothPivot, rawPivot, 1f - Mathf.Exp(-30f * Time.deltaTime));

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pivot = smoothPivot + rot * Vector3.right * shoulder;
            float want = distance;

            // 속도감: 빠를수록 카메라가 조금 더 멀어지고 FOV가 넓어진다
            float spd01 = hero != null ? hero.SpeedBoost01 : 0f;
            want += spd01 * 1.8f;
            fovExtra = Mathf.Lerp(fovExtra, spd01 * maxFovBoost, 1f - Mathf.Exp(-5f * Time.deltaTime));
            if (cam != null) cam.fieldOfView = baseFov + fovExtra;

            // 카메라 충돌 (지면/건물)
            Vector3 back = -(rot * Vector3.forward);
            float allowed = want;
            RaycastHit hit;
            if (Physics.SphereCast(pivot, 0.25f, back, out hit, want + 0.3f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
                allowed = Mathf.Max(0.6f, hit.distance - 0.2f);
            curDist = allowed < curDist ? allowed : Mathf.Lerp(curDist, allowed, 1f - Mathf.Exp(-6f * Time.deltaTime));

            Vector3 pos = pivot + back * curDist;
            if (shake > 0.001f)
            {
                float t = Time.time * 45f;
                pos += (transform.right * (Mathf.PerlinNoise(t, 0.3f) - 0.5f) + transform.up * (Mathf.PerlinNoise(0.7f, t) - 0.5f)) * shake * 0.5f;
                shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 2.2f);
            }
            transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>화면 중앙(조준점)이 가리키는 월드 좌표. 영웅 뒤쪽 장애물은 무시.</summary>
        public Vector3 AimPoint(float maxDist = 900f)
        {
            if (cam == null) cam = GetComponent<Camera>();
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float skip = target != null ? Vector3.Distance(ray.origin, target.position) : 0f;
            Vector3 origin = ray.origin + ray.direction * skip;
            RaycastHit hit;
            if (Physics.Raycast(origin, ray.direction, out hit, maxDist, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
                return hit.point;
            return origin + ray.direction * maxDist;
        }
    }
}
