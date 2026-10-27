using UnityEngine;

// 게임 시작 화면 → 성별(거인 모델) 선택 → 게임 시작.
// 메뉴 동안에는 게임을 멈추고(Time.timeScale = 0) 게임 HUD를 숨기며, 배경에 거인을 보여주면서 카메라가 천천히 흔들린다.
// 성별 카드에 마우스를 올리면 배경의 거인이 그 모델로 바뀌어 미리보기됨. 성별을 고른 뒤에만 '게임 시작'이 활성화.
// 시작 화면: 게임 시작(HP 0이면 패배) / 무한모드(HP 0이 되어도 죽지 않음) / 게임 종료.
// 사망 후 R로 재시작하면 시작 화면 없이 같은 성별·같은 모드로 바로 시작.
// 게임 중 ESC: 게임을 멈추고 일시정지 메뉴(계속하기 / 시작 화면으로 / 게임 종료).
// [ExecuteAlways]: 재생 전(편집 모드)에도 Game 창에 시작 화면을 미리보기로 그림 — 화면 그리기와 카메라 배치만 하고,
// 모델 교체·시간 정지·입력·버튼 동작 같은 게임 로직은 재생 중에만 실행.
[ExecuteAlways]
public class GameStartMenu : MonoBehaviour
{
    public static GameStartMenu Instance { get; private set; }
    public static bool InMenu => Instance != null && Instance.state != State.Playing;
    // 성별 선택 화면 (거인을 정면에서 크게 보여줌)
    public static bool InSelect => Instance != null && Instance.state == State.Select;
    // 게임 중 HUD(점수·돈·아이템·새장 등)를 숨김. 시작 메뉴·상점 등 조작 화면은 그대로
    public static bool HideHud = true;

    [System.Serializable]
    public class GiantOption
    {
        public string label = "여자 거인";
        [TextArea(2, 4)] public string description = "";
        [Tooltip("Giant 오브젝트 아래에 있는 이 성별의 모델")] public GameObject model;
        [Tooltip("충돌 캡슐 높이 배율 (모델 키에 맞춤)")] public float collisionHeightFactor = 1f;
        public Color color = Color.white;
    }

    [Header("타이틀")]
    public string gameTitle = "Stop That Giant";
    public string subtitle = "도시를 짓밟고, 몰려드는 군대를 물리쳐라!";

    [Header("시작 화면 카메라 (거인 키 기준 비율) — 발목 아래만 보이게")]
    public float titleCameraHeight = 0.02f;
    public float titleCameraDistance = 0.28f;
    public float titleFocusHeight = 0.05f;

    [Header("연결")]
    public GiantController giant;
    public GiantCamera giantCamera;
    public GiantOption[] options;

    enum State { Title, Select, Playing }
    State state = State.Title;
    int selected = -1, previewing = -1, hover = -1;
    float stateAt;
    bool started, camInit;
    Vector3 menuFocus;

    static int lastChoice = -1; // 재시작 시 같은 성별로 바로 시작
    static bool lastInfinite;   // 재시작 시 같은 모드로
    // 무한모드: HP가 0이 되어도 죽지 않음 (시작 화면의 '무한모드' 버튼으로 시작). 일반 '게임 시작'은 HP 0이면 패배
    public static bool InfiniteMode { get; private set; }
    // 지금 고른 성별 (세이브용)
    public static int CurrentChoice => Instance != null && Instance.selected >= 0 ? Instance.selected : Mathf.Max(0, lastChoice);
    bool pendingInfinite; // 시작 화면에서 고른 모드 (성별 선택 후 시작할 때 확정)

    // ───── 설정 (시작 화면의 '설정' 버튼) ─────
    // 스트리머 모드: 켜면 거인의 중요 부위를 검은 사각형으로 가림 (StreamerCensor). 다음 실행 때도 유지 (PlayerPrefs)
    public static bool StreamerMode
    {
        get => PlayerPrefs.GetInt("StreamerMode", 0) == 1;
        set { PlayerPrefs.SetInt("StreamerMode", value ? 1 : 0); PlayerPrefs.Save(); }
    }
    bool settingsOpen;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { lastChoice = -1; lastInfinite = false; InfiniteMode = false; }

    float referenceScale = 1f; // 첫 번째(여자) 모델 크기 = 게임 기준 크기

    // 일시정지 메뉴 (게임 중 ESC): 계속하기 / 시작 화면으로 / 게임 종료
    public static bool Paused => Instance != null && Instance.paused;
    bool paused;
    float pausePrevScale = 1f;

    void Awake()
    {
        if (!Application.isPlaying) return; // 편집 모드: 미리보기만
        Instance = this;
        if (!giant) giant = FindObjectOfType<GiantController>();
        if (!giantCamera) giantCamera = FindObjectOfType<GiantCamera>();
        if (options == null || options.Length == 0) { state = State.Playing; return; }

        if (lastChoice >= 0 && lastChoice < options.Length)
        {
            selected = lastChoice;
            InfiniteMode = lastInfinite;
            state = State.Playing;
            Time.timeScale = 1f;
            SetActiveModel(selected);
        }
        else
        {
            state = State.Title;
            InfiniteMode = false;
            Time.timeScale = 0f;
            SetActiveModel(0);
        }
        stateAt = Time.unscaledTime;
    }

    void Start()
    {
        if (!Application.isPlaying) return;
        started = true;
        if (options == null || options.Length == 0) return;
        // 기준 크기는 모든 Awake 뒤에 잼 (GiantController가 Awake에서 시작 크기 배율을 적용함)
        if (options[0].model) referenceScale = options[0].model.transform.localScale.y;
        if (state == State.Playing) FinalizeChoice();
        else { Rebind(previewing); SetAnimUnscaled(true); }
    }

    void OnDestroy()
    {
        if (!Application.isPlaying) return;
        RestoreSpringBones();
        if (Instance != this) return;
        if (InMenu || paused) Time.timeScale = 1f;
        Instance = null;
    }

    // ───────────── 모델 교체 ─────────────
    void SetActiveModel(int i)
    {
        for (int j = 0; j < options.Length; j++)
            if (options[j].model) options[j].model.SetActive(j == i);
        previewing = i;
        if (started) { Rebind(i); SetAnimUnscaled(state != State.Playing); }
    }

    // 메뉴 중(시간 정지)에도 대기 애니메이션이 움직이도록
    void SetAnimUnscaled(bool unscaled)
    {
        var m = options[previewing].model;
        var an = m ? m.GetComponent<Animator>() : null;
        if (an) an.updateMode = unscaled ? AnimatorUpdateMode.UnscaledTime : AnimatorUpdateMode.Normal;
    }

    // 거인 관련 스크립트가 새 모델(애니메이터/뼈)을 쓰도록 다시 연결
    void Rebind(int i)
    {
        var o = options[i];
        if (giant)
        {
            // 화면상 키를 맞추려고 모델 크기가 달라도, 게임 수치는 기준 크기로 동일하게
            giant.scaleCompensation = o.model ? referenceScale / o.model.transform.localScale.y : 1f;
            giant.Rebind(o.collisionHeightFactor);
            var stomp = giant.GetComponent<GiantStomp>(); if (stomp) stomp.Rebind();
            var hp = giant.GetComponent<GiantHealth>(); if (hp) hp.Rebind();
        }
        if (giantCamera && o.model) giantCamera.target = o.model.transform;
    }

    void FinalizeChoice()
    {
        SetActiveModel(selected);
        for (int j = 0; j < options.Length; j++)
            if (j != selected && options[j].model) Destroy(options[j].model); // 고르지 않은 모델 제거
    }

    // ───────────── 흐름 ─────────────
    void GoSelect() { state = State.Select; stateAt = Time.unscaledTime; }
    void GoTitle() { state = State.Title; selected = -1; stateAt = Time.unscaledTime; }

    // 시작 화면 없이 이 성별·모드로 씬을 다시 불러옴
    public static void RestartWith(int choice, bool infinite)
    {
        lastChoice = choice; lastInfinite = infinite;
        Time.timeScale = 1f;
        UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }
    void Choose(int i) { if (i >= 0 && i < options.Length) selected = i; }

    void StartGame()
    {
        if (selected < 0) return;
        RestoreSpringBones();
        lastChoice = selected;
        InfiniteMode = pendingInfinite;
        lastInfinite = InfiniteMode;
        state = State.Playing;
        FinalizeChoice();
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ───────────── 일시정지 ─────────────
    void UpdatePause()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        if (paused) { Resume(); return; }
        // 상점·카드 선택 중에는 ESC가 그 창을 닫는 데 쓰이므로 열지 않음 (이번 프레임에 상점이 닫힌 경우 포함)
        if (GiantHome.IsOpen || GiantHome.ClosedFrame == Time.frameCount || CardDraft.IsOpen) return;
        if (GiantHealth.Instance && GiantHealth.Instance.IsDead) return;
        paused = true;
        stateAt = Time.unscaledTime;
        pausePrevScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    void Resume()
    {
        paused = false;
        Time.timeScale = pausePrevScale;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    // 시작 화면으로: 씬을 다시 불러오되 '같은 성별로 바로 시작'을 지워서 시작 화면부터
    void ReturnToTitle()
    {
        paused = false;
        lastChoice = -1; lastInfinite = false; InfiniteMode = false;
        Time.timeScale = 1f;
        UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (!InMenu) { UpdatePause(); return; }
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        if (Time.timeScale != 0f) Time.timeScale = 0f;
        if (Time.unscaledTime - stateAt < 0.2f) return;

        if (state == State.Title && settingsOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) settingsOpen = false;
        }
        else if (state == State.Title)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) { pendingInfinite = false; GoSelect(); }
        }
        else if (state == State.Select)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1) || Input.GetKeyDown(KeyCode.LeftArrow)) Choose(0);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2) || Input.GetKeyDown(KeyCode.RightArrow)) Choose(1);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) StartGame();
            else if (Input.GetKeyDown(KeyCode.Escape)) GoTitle();
        }
    }

    // 메뉴 카메라: 거인 정면에서 천천히 좌우로 흔들림. 게임이 시작되면 GiantCamera가 이어받아 뒤쪽으로 부드럽게 이동
    void LateUpdate()
    {
        if (!Application.isPlaying) { PreviewCamera(); return; }
        if (!InMenu) return;

        int want = state == State.Select ? (hover >= 0 ? hover : (selected >= 0 ? selected : previewing)) : previewing;
        if (want != previewing) SetActiveModel(want);
        MenuSpringBones();

        if (!giantCamera || !giant) return;
        var cam = giantCamera.transform;
        var m = options[previewing].model ? options[previewing].model.transform : giant.transform;
        float h = giant.GameScale * 1.7f;

        Vector3 basePos = giant.transform.position;
        Vector3 fwd = giant.transform.forward; fwd.y = 0; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 dir = Quaternion.Euler(0, Mathf.Sin(Time.unscaledTime * 0.25f) * 18f, 0) * fwd;

        Vector3 focus, desired;
        if (state == State.Title)
        {
            // 시작 화면: 땅바닥 높이에서 거인의 발목 아래만 (소인의 눈높이), 발은 화면 오른쪽 (왼쪽엔 제목)
            Vector3 feetDir = Quaternion.Euler(0, Mathf.Sin(Time.unscaledTime * 0.25f) * 6f, 0) * fwd;
            focus = basePos + Vector3.up * h * titleFocusHeight + right * h * 0.06f;
            desired = basePos + Vector3.up * h * titleCameraHeight + feetDir * h * titleCameraDistance;
        }
        else
        {
            focus = basePos + Vector3.up * h * 0.55f;
            desired = basePos + Vector3.up * h * 0.62f + dir * h * 1.45f;
        }

        if (!camInit) { cam.position = desired; menuFocus = focus; camInit = true; }
        float k = 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime);
        cam.position = Vector3.Lerp(cam.position, desired, k);
        menuFocus = Vector3.Lerp(menuFocus, focus, k);
        cam.rotation = Quaternion.LookRotation(menuFocus - cam.position);
    }

    // 메뉴 중 머리카락·가슴(VRM 스프링본): 메뉴는 Time.timeScale = 0이라 VRM이 Time.deltaTime(0)으로 스프링본을 계산하면
    // 되돌아가는 힘(강성·중력)은 0이 되고 관성만 남아 비정상적으로 꿈틀거림 → 메뉴 동안엔 VRM 자동 갱신을 끄고
    // 실제 경과 시간(unscaledDeltaTime)으로 스프링본만 직접 갱신. (이 모델들은 컨스트레인트·컨트롤 리그가 없음)
    UniVRM10.Vrm10Instance menuVrm;

    void MenuSpringBones()
    {
        var m = previewing >= 0 && previewing < options.Length ? options[previewing].model : null;
        var vrm = m ? m.GetComponent<UniVRM10.Vrm10Instance>() : null;
        if (vrm != menuVrm) { RestoreSpringBones(); menuVrm = vrm; }
        if (!vrm) return;
        vrm.UpdateType = UniVRM10.Vrm10Instance.UpdateTypes.None;
        vrm.Runtime.SpringBone.Process(Mathf.Min(Time.unscaledDeltaTime, 1f / 30f));
    }

    void RestoreSpringBones()
    {
        if (menuVrm) menuVrm.UpdateType = UniVRM10.Vrm10Instance.UpdateTypes.LateUpdate;
        menuVrm = null;
    }

    // 편집 모드 미리보기: 시작 화면과 같은 발목 구도로 카메라를 놓음 (흔들림 없음).
    // 위치가 실제로 달라질 때만 옮겨서 씬이 매 프레임 변경됨으로 표시되지 않게 함
    void PreviewCamera()
    {
        if (!giant) giant = FindObjectOfType<GiantController>();
        if (!giantCamera) giantCamera = FindObjectOfType<GiantCamera>();
        if (!giant || !giantCamera || options == null || options.Length == 0 || !options[0].model) return;
        float h = options[0].model.transform.lossyScale.y * 1.7f; // 편집 모드에선 GameScale 대신 모델 크기로
        Vector3 basePos = giant.transform.position;
        Vector3 fwd = giant.transform.forward; fwd.y = 0; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 focus = basePos + Vector3.up * h * titleFocusHeight + right * h * 0.06f;
        Vector3 pos = basePos + Vector3.up * h * titleCameraHeight + fwd * h * titleCameraDistance;
        Quaternion rot = Quaternion.LookRotation(focus - pos);
        var cam = giantCamera.transform;
        if ((cam.position - pos).sqrMagnitude > 0.0001f || Quaternion.Angle(cam.rotation, rot) > 0.01f)
            cam.SetPositionAndRotation(pos, rot);
    }

    // ───────────── 화면 ─────────────
    void OnGUI()
    {
        GUI.depth = -200;
        float W = Screen.width, H = Screen.height;
        float fade = Mathf.Clamp01((Time.unscaledTime - stateAt) / 0.35f);
        if (Application.isPlaying && !InMenu) // 편집 모드에선 항상 시작 화면 미리보기
        {
            if (paused) DrawPause(W, H, fade);
            GUI.color = Color.white;
            return;
        }
        if (state == State.Title) DrawTitle(W, H, fade);
        else DrawSelect(W, H, fade);
        GUI.color = Color.white;
    }

    void DrawTitle(float W, float H, float fade)
    {
        // 왼쪽이 어두운 그라데이션
        const int steps = 32;
        float pw = W * 0.6f / steps;
        for (int i = 0; i < steps; i++)
        {
            float a = 1f - i / (float)steps;
            Box(new Rect(i * pw, 0, pw + 1, H), new Color(0, 0, 0, 0.8f * a * a));
        }

        float x = W * 0.07f;
        ShadowLabel(new Rect(x, H * 0.18f, W * 0.9f, H * 0.13f), gameTitle,
            Style(Mathf.RoundToInt(H * 0.09f), FontStyle.Bold, TextAnchor.MiddleLeft), new Color(1f, 0.82f, 0.28f, fade));
        ShadowLabel(new Rect(x + 4, H * 0.31f, W * 0.7f, H * 0.05f), subtitle,
            Style(Mathf.RoundToInt(H * 0.028f), FontStyle.Bold, TextAnchor.MiddleLeft), new Color(1f, 1f, 1f, 0.9f * fade));

        float bw = Mathf.Clamp(W * 0.22f, 240f, 340f), bh = Mathf.Clamp(H * 0.075f, 48f, 72f);
        float y = H * 0.45f;
        if (settingsOpen) { DrawSettings(W, H, fade); return; }
        if (MenuButton(new Rect(x, y, bw, bh), "게임 시작", new Color(1f, 0.55f, 0.15f), true, fade)) { pendingInfinite = false; GoSelect(); return; }
        if (MenuButton(new Rect(x, y + bh * 1.3f, bw, bh), "무한모드", new Color(0.55f, 0.4f, 1f), true, fade)) { pendingInfinite = true; GoSelect(); return; }
        if (MenuButton(new Rect(x, y + bh * 2.6f, bw, bh), "설정", new Color(0.25f, 0.62f, 0.55f), true, fade)) { settingsOpen = true; return; }
        if (MenuButton(new Rect(x, y + bh * 3.9f, bw, bh), "게임 종료", new Color(0.42f, 0.45f, 0.52f), true, fade)) { Quit(); return; }

        ShadowLabel(new Rect(x, H - 50, W * 0.5f, 30), "Enter : 게임 시작",
            Style(14, FontStyle.Normal, TextAnchor.MiddleLeft), new Color(1, 1, 1, 0.6f * fade));
    }

    // 설정 창: 가운데 패널 + 체크박스 목록 + 닫기
    void DrawSettings(float W, float H, float fade)
    {
        float pw = Mathf.Clamp(W * 0.36f, 380f, 520f), ph = Mathf.Clamp(H * 0.42f, 260f, 360f);
        var panel = new Rect(W * 0.5f - pw * 0.5f, H * 0.5f - ph * 0.5f, pw, ph);
        Box(new Rect(panel.x - 3, panel.y - 3, panel.width + 6, panel.height + 6), new Color(0f, 0f, 0f, 0.7f * fade));
        Box(panel, new Color(0.13f, 0.14f, 0.17f, 0.95f * fade));
        ShadowLabel(new Rect(panel.x, panel.y + 14, panel.width, 44), "설정",
            Style(Mathf.RoundToInt(Mathf.Clamp(H * 0.045f, 26f, 40f)), FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1f, 0.82f, 0.28f, fade));

        float rowY = panel.y + 80;
        bool on = StreamerMode;
        if (Checkbox(new Rect(panel.x + 30, rowY, panel.width - 60, 40), "스트리머 모드", on, fade)) StreamerMode = !on;
        ShadowLabel(new Rect(panel.x + 30 + 44, rowY + 40, panel.width - 104, 44), "거인의 중요 부위를 검은 사각형으로 가립니다.",
            Style(14, FontStyle.Normal, TextAnchor.UpperLeft), new Color(1f, 1f, 1f, 0.7f * fade));

        float bw = 180f, bh = 50f;
        if (MenuButton(new Rect(panel.center.x - bw * 0.5f, panel.yMax - bh - 22, bw, bh), "닫기", new Color(0.42f, 0.45f, 0.52f), true, fade)) settingsOpen = false;
    }

    // 체크박스 한 줄 (칸 + 이름). 클릭하면 true
    bool Checkbox(Rect r, string label, bool value, float alpha)
    {
        bool hov = r.Contains(Event.current.mousePosition);
        var box = new Rect(r.x, r.y + (r.height - 30) * 0.5f, 30, 30);
        Box(new Rect(box.x - 2, box.y - 2, box.width + 4, box.height + 4), new Color(1f, 1f, 1f, (hov ? 0.9f : 0.6f) * alpha));
        Box(box, new Color(0.08f, 0.08f, 0.1f, alpha));
        if (value) Box(new Rect(box.x + 6, box.y + 6, box.width - 12, box.height - 12), new Color(0.35f, 0.85f, 0.6f, alpha));
        ShadowLabel(new Rect(box.xMax + 14, r.y, r.width - 44, r.height), label,
            Style(20, FontStyle.Bold, TextAnchor.MiddleLeft), new Color(1f, 1f, 1f, alpha));
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    void DrawPause(float W, float H, float fade)
    {
        Box(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.6f * fade));
        ShadowLabel(new Rect(0, H * 0.25f, W, H * 0.1f), "일시정지",
            Style(Mathf.RoundToInt(H * 0.07f), FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1f, 0.82f, 0.28f, fade));
        float bw = Mathf.Clamp(W * 0.22f, 240f, 340f), bh = Mathf.Clamp(H * 0.075f, 48f, 72f);
        float x = W * 0.5f - bw * 0.5f, y = H * 0.42f;
        if (MenuButton(new Rect(x, y, bw, bh), "계속하기", new Color(1f, 0.55f, 0.15f), true, fade)) { Resume(); return; }
        if (MenuButton(new Rect(x, y + bh * 1.3f, bw, bh), "시작 화면으로", new Color(0.55f, 0.4f, 1f), true, fade)) { ReturnToTitle(); return; }
        if (MenuButton(new Rect(x, y + bh * 2.6f, bw, bh), "게임 종료", new Color(0.42f, 0.45f, 0.52f), true, fade)) { Quit(); return; }
        ShadowLabel(new Rect(0, y + bh * 3.9f, W, 30), "ESC : 계속하기",
            Style(14, FontStyle.Normal, TextAnchor.MiddleCenter), new Color(1, 1, 1, 0.6f * fade));
    }

    void DrawSelect(float W, float H, float fade)
    {
        Box(new Rect(0, 0, W, H * 0.17f), new Color(0, 0, 0, 0.55f * fade));
        Box(new Rect(0, H * 0.8f, W, H * 0.2f), new Color(0, 0, 0, 0.55f * fade));

        ShadowLabel(new Rect(0, H * 0.025f, W, H * 0.08f), "거인을 선택하세요",
            Style(Mathf.RoundToInt(H * 0.055f), FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1, 1, 1, fade));
        ShadowLabel(new Rect(0, H * 0.105f, W, H * 0.04f), "카드를 클릭하거나 1 / 2 키로 선택   ·   마우스를 올리면 미리보기",
            Style(Mathf.RoundToInt(Mathf.Clamp(H * 0.022f, 13, 20)), FontStyle.Normal, TextAnchor.MiddleCenter), new Color(1, 1, 1, 0.7f * fade));

        var e = Event.current;
        float cw = Mathf.Clamp(W * 0.2f, 220f, 320f), ch = cw * 0.85f;
        int newHover = -1;
        for (int i = 0; i < options.Length; i++)
        {
            float cx = options.Length == 2 ? (i == 0 ? W * 0.06f : W * 0.94f - cw)
                                           : W * 0.5f - (options.Length * (cw + 20f)) * 0.5f + i * (cw + 20f);
            var r = new Rect(cx, H * 0.47f - ch * 0.5f, cw, ch);
            bool hov = r.Contains(e.mousePosition) && Time.unscaledTime - stateAt > 0.2f;
            if (hov) newHover = i;
            DrawCard(r, options[i], i, selected == i, hov, fade);
            if (hov && e.type == EventType.MouseDown && e.button == 0) { e.Use(); Choose(i); }
        }
        if (e.type == EventType.Repaint || e.type == EventType.MouseMove) hover = newHover;

        // 하단: 선택 표시 + 게임 시작 / 뒤로
        bool can = selected >= 0;
        ShadowLabel(new Rect(0, H * 0.815f, W, 28), can ? $"선택: {options[selected].label}" : "성별을 먼저 선택하세요",
            Style(18, FontStyle.Bold, TextAnchor.MiddleCenter), can ? new Color(options[selected].color.r, options[selected].color.g, options[selected].color.b, fade) : new Color(1, 1, 1, 0.6f * fade));
        float bw = Mathf.Clamp(W * 0.22f, 240f, 340f), bh = Mathf.Clamp(H * 0.075f, 48f, 72f);
        if (MenuButton(new Rect(W * 0.5f - bw * 0.5f, H * 0.87f, bw, bh), pendingInfinite ? "무한모드 시작" : "게임 시작", pendingInfinite ? new Color(0.55f, 0.4f, 1f) : new Color(1f, 0.55f, 0.15f), can, fade)) { StartGame(); return; }
        if (MenuButton(new Rect(24, H * 0.87f + bh * 0.2f, 130, bh * 0.7f), "← 뒤로", new Color(0.42f, 0.45f, 0.52f), true, fade)) { GoTitle(); return; }
    }

    void DrawCard(Rect r, GiantOption o, int index, bool sel, bool hov, float fade)
    {
        Color c = o.color;
        if (hov && !sel) r = new Rect(r.x - 4, r.y - 6, r.width + 8, r.height + 8);
        float bw = sel ? 5f : 3f;
        Color border = sel ? Color.Lerp(c, Color.white, Mathf.PingPong(Time.unscaledTime * 1.5f, 0.35f))
                           : new Color(c.r, c.g, c.b, hov ? 0.85f : 0.4f);
        border.a *= fade;
        Box(new Rect(r.x - bw, r.y - bw, r.width + bw * 2, r.height + bw * 2), border);
        Box(r, new Color(0.08f, 0.09f, 0.12f, 0.92f * fade));

        float bandH = r.height * 0.3f;
        Box(new Rect(r.x, r.y, r.width, bandH), new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, fade));
        ShadowLabel(new Rect(r.x, r.y, r.width, bandH), o.label,
            Style(Mathf.RoundToInt(r.width * 0.11f), FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1, 1, 1, fade));

        var desc = Style(Mathf.RoundToInt(Mathf.Clamp(r.width * 0.055f, 13, 17)), FontStyle.Normal, TextAnchor.MiddleCenter);
        desc.wordWrap = true;
        GUI.color = new Color(0.85f, 0.87f, 0.9f, fade);
        GUI.Label(new Rect(r.x + 14, r.y + bandH + 6, r.width - 28, r.height * 0.42f), o.description, desc);

        string foot = sel ? "선택됨" : $"[ {index + 1} ]";
        ShadowLabel(new Rect(r.x, r.yMax - r.height * 0.2f, r.width, r.height * 0.16f), foot,
            Style(Mathf.RoundToInt(r.width * 0.07f), FontStyle.Bold, TextAnchor.MiddleCenter),
            sel ? new Color(c.r, c.g, c.b, fade) : new Color(1, 1, 1, (hov ? 0.9f : 0.5f) * fade));
    }

    bool MenuButton(Rect rect, string text, Color color, bool enabled, float alpha)
    {
        var e = Event.current;
        bool hov = enabled && rect.Contains(e.mousePosition);
        Rect r = hov ? new Rect(rect.x - 4, rect.y - 3, rect.width + 8, rect.height + 6) : rect;
        Color c = enabled ? color * (hov ? 1f : 0.82f) : new Color(0.25f, 0.26f, 0.3f);
        Box(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0, 0, 0, 0.6f * alpha));
        Box(r, new Color(c.r, c.g, c.b, (enabled ? 0.95f : 0.7f) * alpha));
        ShadowLabel(r, text, Style(Mathf.RoundToInt(r.height * 0.4f), FontStyle.Bold, TextAnchor.MiddleCenter),
            new Color(1, 1, 1, (enabled ? 1f : 0.45f) * alpha));
        if (enabled && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition)
            && Time.unscaledTime - stateAt > 0.2f && Application.isPlaying) // 편집 모드 미리보기에선 눌리지 않음
        {
            e.Use();
            return true;
        }
        return false;
    }

    static GUIStyle Style(int size, FontStyle fs, TextAnchor anchor)
        => new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = anchor, richText = true };

    static void Box(Rect r, Color c)
    {
        var old = GUI.color; GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    static void ShadowLabel(Rect r, string text, GUIStyle s, Color c)
    {
        GUI.color = new Color(0, 0, 0, 0.7f * c.a);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, s);
        GUI.color = c;
        GUI.Label(r, text, s);
        GUI.color = Color.white;
    }
}
