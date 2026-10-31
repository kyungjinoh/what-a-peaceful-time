using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

[DisallowMultipleComponent]
public class GameStartReplayUI : MonoBehaviour
{
    public static bool IsRunning { get; private set; }
    public static System.Action OnRunStarted;
    public static System.Action OnRunStopped;

    // Auto-find by name to match user's hierarchy ("start" button).
    const string StartButtonName = "start";
    const string RestartButtonName = "restart";

    Button startButton;
    Button restartButton;
    GameObject startRoot;
    bool started;
    bool subscribed;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        HookScene(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        SceneManager.sceneLoaded += HookScene;
        subscribed = true;
    }

    void OnDestroy()
    {
        Unfreeze();
        if (subscribed)
            SceneManager.sceneLoaded -= HookScene;
        PlayerHealth.OnPlayerDied -= OnPlayerDied;
    }

    void Update()
    {
        // Keyboard fallback so the game never gets stuck if UI click fails.
        if (!IsRunning && !started)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.KeypadEnter))
                OnStartPressed();
        }
    }

    void HookScene(Scene scene, LoadSceneMode mode)
    {
        started = false;
        IsRunning = false;

        EnsureEventSystem();

        // Rebind UI objects each scene load.
        startRoot = FindAnyUIObjectByName(StartButtonName);
        startButton = FindButtonByName(StartButtonName);
        restartButton = FindButtonByName(RestartButtonName);

        // Scene may be missing start/restart (deleted/renamed). Create fallback UI.
        if (startButton == null)
            CreateFallbackStartUi();
        if (restartButton == null)
            CreateFallbackRestartUi();

        if (startButton != null)
        {
            startButton.onClick.RemoveListener(OnStartPressed);
            startButton.onClick.AddListener(OnStartPressed);
        }

        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(OnRestartPressed);
            restartButton.onClick.AddListener(OnRestartPressed);
        }

        PlayerHealth.OnPlayerDied -= OnPlayerDied;
        PlayerHealth.OnPlayerDied += OnPlayerDied;

        ShowStartUI(isReplay: false);
        Freeze();
    }

    void OnStartPressed()
    {
        if (started && IsRunning) return;
        started = true;
        HideStartUI();
        Unfreeze();
        IsRunning = true;
        GameAudioManager.Instance.StartBgmIfNeeded();
        OnRunStarted?.Invoke();
    }

    void OnRestartPressed()
    {
        RestartScene();
    }

    void OnPlayerDied()
    {
        ShowStartUI(isReplay: true);
        Freeze();
        IsRunning = false;
        OnRunStopped?.Invoke();
    }

    void ShowStartUI(bool isReplay)
    {
        if (startRoot != null)
        {
            ActivateHierarchy(startRoot);
            startRoot.SetActive(true);
        }
        if (startButton != null)
        {
            ActivateHierarchy(startButton.gameObject);
            startButton.gameObject.SetActive(true);
        }

        // If there's a separate restart button, show it only on replay.
        if (restartButton != null) restartButton.gameObject.SetActive(isReplay);
    }

    void HideStartUI()
    {
        if (startRoot != null) startRoot.SetActive(false);
        // Some setups only have a Button object (no parent panel); keep it hidden too.
        if (startButton != null) startButton.gameObject.SetActive(false);
        if (restartButton != null) restartButton.gameObject.SetActive(false);
    }

    static void ActivateHierarchy(GameObject go)
    {
        if (go == null) return;
        var t = go.transform;
        while (t != null)
        {
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            t = t.parent;
        }
    }

    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        // Prefer Input System UI module if present; otherwise old InputModule.
        var inputSystemType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
        if (inputSystemType != null)
            es.AddComponent(inputSystemType);
        else
            es.AddComponent<StandaloneInputModule>();
    }

    void CreateFallbackStartUi()
    {
        var canvas = EnsureUiCanvas();
        var btnGo = CreateUiButton(canvas.transform, StartButtonName, "START", new Vector2(0f, 40f), new Color(0.15f, 0.7f, 0.3f, 0.95f));
        startRoot = btnGo;
        startButton = btnGo.GetComponent<Button>();
    }

    void CreateFallbackRestartUi()
    {
        var canvas = EnsureUiCanvas();
        var btnGo = CreateUiButton(canvas.transform, RestartButtonName, "RESTART", new Vector2(0f, -40f), new Color(0.85f, 0.25f, 0.2f, 0.95f));
        restartButton = btnGo.GetComponent<Button>();
        btnGo.SetActive(false);
    }

    static Canvas EnsureUiCanvas()
    {
        // Prefer existing Player UI canvas if present.
        var playerUi = FindAnyUIObjectByName("Player UI");
        if (playerUi != null)
        {
            var c = playerUi.GetComponent<Canvas>();
            if (c != null) return c;
            c = playerUi.GetComponentInChildren<Canvas>(true);
            if (c != null) return c;
        }

        var existing = Object.FindFirstObjectByType<Canvas>();
        if (existing != null) return existing;

        var go = new GameObject("GameStartCanvas");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        go.AddComponent<CanvasScaler>();
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    static GameObject CreateUiButton(Transform parent, string name, string label, Vector2 anchoredPos, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(220f, 64f);
        rt.anchoredPosition = anchoredPos;

        var img = go.AddComponent<Image>();
        img.color = color;
        var sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        if (sprite != null) img.sprite = sprite;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 36;
        tmp.color = Color.white;

        return go;
    }

    static GameObject FindAnyUIObjectByName(string name)
    {
        // Finds active objects only. In our use-case, start UI is visible by default.
        var go = GameObject.Find(name);
        if (go != null) return go;

        // Also check inactive UI objects.
        var all = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i];
            if (t == null) continue;
            if (!t.gameObject.scene.IsValid()) continue;
            if (t.name == name) return t.gameObject;
        }
        return null;
    }

    static Button FindButtonByName(string name)
    {
        var go = FindAnyUIObjectByName(name);
        if (go == null) return null;
        // Button might live on this object, a child, or a parent depending on prefab structure.
        var b = go.GetComponent<Button>();
        if (b != null) return b;
        b = go.GetComponentInChildren<Button>(true);
        if (b != null) return b;
        return go.GetComponentInParent<Button>(true);
    }

    static void Freeze()
    {
        Time.timeScale = 0f;
    }

    static void Unfreeze()
    {
        Time.timeScale = 1f;
    }

    static void RestartScene()
    {
        Unfreeze();
        IsRunning = false;
        OnRunStopped?.Invoke();
        EnemyTorchDuplicator.ResetForRestart();
        SurvivedTimerUI.ResetForRestart();
        VisionLimiter.ResetForRestart();
        SpecialMoveManager.ResetForRestart();
        var scene = SceneManager.GetActiveScene();
        // In Editor, buildIndex can be -1 if the scene isn't in Build Settings.
        if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
        else SceneManager.LoadScene(scene.name);
    }
}
