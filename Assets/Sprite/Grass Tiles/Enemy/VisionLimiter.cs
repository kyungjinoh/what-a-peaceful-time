using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class VisionLimiter : MonoBehaviour
{
    static VisionLimiter instance;

    [Header("Timing")]
    [Tooltip("First blackout starts this many seconds after the run begins.")]
    public float firstTriggerDelaySeconds = 30f;
    [Tooltip("How often (seconds) the vision-limited event triggers.")]
    public float intervalSeconds = 40f;
    [Tooltip("How long (seconds) the vision-limited event lasts.")]
    public float durationSeconds = 10f;

    [Tooltip("Smooth fade in/out time for blackout (seconds).")]
    public float fadeSeconds = 0.6f;

    [Header("Vision")]
    [Tooltip("Vision radius around the player (world units).")]
    public float playerVisionRadius = 1.8f;

    [Header("Rendering")]
    [Tooltip("Sorting order for the black overlay. Must be above your world sprites.")]
    public int overlaySortingOrder = 10000;
    [Tooltip("If true, automatically move the overlay to the topmost sorting layer/order found in the scene.")]
    public bool autoUseTopmostSorting = true;
    [Tooltip("Overlay alpha (0..1). 1 = fully black outside vision.")]
    [Range(0f, 1f)]
    public float overlayAlpha = 1f;

    [Header("Audio")]
    [Tooltip("Music to play only during blackout.")]
    public AudioClip blackoutMusic;
    [Tooltip("Volume for blackout music.")]
    [Range(0f, 1f)]
    public float blackoutMusicVolume = 0.9f;

    [Header("Debug")]
    public bool debugLogs = false;

    SpriteRenderer overlayRenderer;
    readonly Dictionary<int, SpriteMask> masksById = new Dictionary<int, SpriteMask>();
    Transform player;
    bool effectActive;
    AudioSource audioSource;
    bool bgmPausedForBlackout;
    bool lastRunning;
    float runStartUnscaledTime;
    float nextTriggerUnscaledTime;
    float effectEndUnscaledTime;

    float effectBlend;        // 0..1 current fade
    float effectBlendTarget;  // 0..1 desired fade

    static Sprite cachedCircleSprite;
    static Sprite cachedOnePixelSprite;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureOverlay();
        EnsureAudio();

        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);

        GameStartReplayUI.OnRunStarted -= HandleRunStarted;
        GameStartReplayUI.OnRunStarted += HandleRunStarted;
        GameStartReplayUI.OnRunStopped -= HandleRunStopped;
        GameStartReplayUI.OnRunStopped += HandleRunStopped;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        GameStartReplayUI.OnRunStarted -= HandleRunStarted;
        GameStartReplayUI.OnRunStopped -= HandleRunStopped;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        player = GameObject.FindWithTag("Player")?.transform;
        // Keep overlay alive across scenes but hide it by default until the event starts.
        SetEffectActive(false);
    }

    void Start()
    {
        // Scheduler runs in Update (more robust than coroutines during reloads).
    }

    void HandleRunStarted()
    {
        runStartUnscaledTime = Time.unscaledTime;
        float firstDelay = Mathf.Max(0.5f, firstTriggerDelaySeconds);
        nextTriggerUnscaledTime = runStartUnscaledTime + firstDelay;
        effectEndUnscaledTime = 0f;
        SetEffectActive(false);
        lastRunning = true;
        if (debugLogs)
            Debug.Log($"VisionLimiter: run started. First blackout in {firstDelay:0.0}s (at t={nextTriggerUnscaledTime:0.00}).", this);
    }

    void HandleRunStopped()
    {
        SetEffectActive(false);
        lastRunning = false;
        nextTriggerUnscaledTime = 0f;
        effectEndUnscaledTime = 0f;
        if (debugLogs)
            Debug.Log("VisionLimiter: run stopped. Blackout scheduler cleared.", this);
    }

    void LateUpdate()
    {
        // Keep overlay centered on the camera so it always covers the view.
        if (overlayRenderer == null || !overlayRenderer.enabled) return;

        var cam = Camera.main;
        if (cam != null)
        {
            var p = cam.transform.position;
            overlayRenderer.transform.position = new Vector3(p.x, p.y, 0f);
        }
        else if (player != null)
        {
            var p = player.position;
            overlayRenderer.transform.position = new Vector3(p.x, p.y, 0f);
        }
    }

    void Update()
    {
        bool running = GameStartReplayUI.IsRunning;

        // If not running, ensure effect off and wait.
        if (!running)
        {
            if (effectActive) SetEffectActive(false);
            UpdateFade(unscaled: true);
            lastRunning = running;
            return;
        }

        // Fallback: if for some reason we missed the run-start event, schedule now.
        if (!lastRunning && nextTriggerUnscaledTime <= 0f)
        {
            runStartUnscaledTime = Time.unscaledTime;
            float firstDelay = Mathf.Max(0.5f, firstTriggerDelaySeconds);
            nextTriggerUnscaledTime = runStartUnscaledTime + firstDelay;
        }

        float now = Time.unscaledTime;
        float dur = Mathf.Clamp(durationSeconds, 0.25f, 60f);
        float interval = Mathf.Max(1f, intervalSeconds);

        if (effectActive)
        {
            if (now >= effectEndUnscaledTime)
            {
                SetEffectActive(false);
                // nextTriggerUnscaledTime is already scheduled.
            }
        }
        else
        {
            if (nextTriggerUnscaledTime > 0f && now >= nextTriggerUnscaledTime)
            {
                SetEffectActive(true);
                RefreshMasks();
                effectEndUnscaledTime = now + dur;
                nextTriggerUnscaledTime += interval;
                if (debugLogs)
                    Debug.Log($"VisionLimiter: BLACKOUT ON for {dur:0.0}s. Next trigger at t={nextTriggerUnscaledTime:0.00}.", this);
            }
        }

        UpdateFade(unscaled: true);
        lastRunning = running;
    }

    void SetEffectActive(bool on)
    {
        effectActive = on;
        effectBlendTarget = on ? 1f : 0f;
        EnsureOverlay();
        // Visual/audio are applied smoothly in UpdateFade().
    }

    void UpdateFade(bool unscaled)
    {
        EnsureOverlay();
        EnsureAudio();

        float dt = unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
        float fade = Mathf.Max(0.0001f, fadeSeconds);
        effectBlend = Mathf.MoveTowards(effectBlend, effectBlendTarget, dt / fade);

        // Overlay: keep enabled while blending so it fades smoothly.
        if (overlayRenderer != null)
        {
            float a = Mathf.Clamp01(overlayAlpha) * effectBlend;
            overlayRenderer.enabled = a > 0.0001f;
            overlayRenderer.color = new Color(0f, 0f, 0f, a);
        }

        // Masks: only need to be active when blend is visible.
        bool masksOn = effectBlend > 0.0001f;
        foreach (var kv in masksById)
        {
            if (kv.Value != null) kv.Value.gameObject.SetActive(masksOn);
        }

        // Audio: fade blackout music volume and pause/resume BGM at edges.
        if (audioSource != null)
        {
            // Re-resolve clip if it is missing for any reason.
            if (blackoutMusic == null || audioSource.clip == null)
                ResolveBlackoutClip();

            if (blackoutMusic != null && audioSource.clip != blackoutMusic)
                audioSource.clip = blackoutMusic;

            audioSource.volume = Mathf.Clamp01(blackoutMusicVolume) * effectBlend;

            if (effectBlendTarget > 0f && effectBlend > 0.0001f)
            {
                if (!bgmPausedForBlackout)
                {
                    GameAudioManager.Instance.PauseBgm();
                    bgmPausedForBlackout = true;
                }
                if (audioSource.clip != null && !audioSource.isPlaying) audioSource.Play();
            }

            if (effectBlend <= 0.0001f && effectBlendTarget <= 0f)
            {
                if (audioSource.isPlaying) audioSource.Stop();
                if (bgmPausedForBlackout)
                {
                    GameAudioManager.Instance.ResumeBgm();
                    bgmPausedForBlackout = false;
                }
            }
        }
    }

    void EnsureOverlay()
    {
        if (overlayRenderer != null) return;

        var go = new GameObject("VisionOverlay");
        go.transform.SetParent(transform, false);

        overlayRenderer = go.AddComponent<SpriteRenderer>();
        overlayRenderer.sprite = GetOrCreateOnePixelSprite();
        overlayRenderer.color = new Color(0f, 0f, 0f, 0f);
        overlayRenderer.sortingOrder = overlaySortingOrder;
        overlayRenderer.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;

        // Big enough to cover the whole camera view regardless of aspect.
        go.transform.localScale = new Vector3(2000f, 2000f, 1f);
        go.transform.position = new Vector3(0f, 0f, 0f);
    }

    void EnsureAudio()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.spatialBlend = 0f; // 2D music
        audioSource.volume = 0f;

        // Always try to resolve if missing (VisionLimiter can persist across reloads).
        if (blackoutMusic == null || audioSource.clip == null)
            ResolveBlackoutClip();

        if (blackoutMusic != null) audioSource.clip = blackoutMusic;
    }

    void ResolveBlackoutClip()
    {
        if (blackoutMusic != null) return;
#if UNITY_EDITOR
        // Auto-load the clip directly from your project asset (Editor only).
        blackoutMusic = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sprite/Grass Tiles/Enemy/leberch-thriller-511166.mp3");
#endif
        // Build-safe loading: put the clip under Assets/Resources/Audio/
        // Example path: Assets/Resources/Audio/leberch-thriller-511166.mp3
        if (blackoutMusic == null)
            blackoutMusic = Resources.Load<AudioClip>("Audio/leberch-thriller-511166");
    }

    void RefreshMasks()
    {
        EnsureOverlay();

        if (autoUseTopmostSorting)
            ApplyTopmostSorting();

        // ONLY player: outside must be fully black, so do not add enemy masks.
        if (player == null) player = GameObject.FindWithTag("Player")?.transform;
        if (player != null) EnsureMaskForTransform(player, playerVisionRadius, forcedId: -100);

        // Clean up destroyed entries.
        var toRemove = new List<int>();
        foreach (var kv in masksById)
        {
            var sm = kv.Value;
            if (sm == null) { toRemove.Add(kv.Key); continue; }
            var follow = sm.transform.parent;
            if (follow == null) { toRemove.Add(kv.Key); continue; }
        }
        for (int i = 0; i < toRemove.Count; i++)
        {
            int id = toRemove[i];
            if (masksById.TryGetValue(id, out var sm) && sm != null) Destroy(sm.gameObject);
            masksById.Remove(id);
        }

        // IMPORTANT:
        // Do NOT force overlay to player's sorting layer here.
        // When autoUseTopmostSorting is enabled, the overlay must stay on the topmost layer;
        // the masks are moved to match the overlay layer in ApplyTopmostSorting()/EnsureMaskForTransform().
    }

    void ApplyTopmostSorting()
    {
        if (overlayRenderer == null) return;

        // Find the topmost sorting layer value currently used in the scene, and set overlay above it.
        int bestLayerId = overlayRenderer.sortingLayerID;
        int bestLayerValue = SortingLayer.GetLayerValueFromID(bestLayerId);
        int bestOrder = overlayRenderer.sortingOrder;

        var all = Resources.FindObjectsOfTypeAll<Renderer>();
        for (int i = 0; i < all.Length; i++)
        {
            var r = all[i];
            if (r == null) continue;
            if (!r.gameObject.scene.IsValid()) continue; // skip assets/prefabs

            int layerId = r.sortingLayerID;
            int layerValue = SortingLayer.GetLayerValueFromID(layerId);
            int order = r.sortingOrder;

            if (layerValue > bestLayerValue || (layerValue == bestLayerValue && order > bestOrder))
            {
                bestLayerValue = layerValue;
                bestLayerId = layerId;
                bestOrder = order;
            }
        }

        overlayRenderer.sortingLayerID = bestLayerId;
        // Push overlay to (near) Unity's max sorting order so it renders above everything.
        // (SortingOrder is int16-ish in many pipelines; keep some headroom.)
        overlayRenderer.sortingOrder = 32760;

        // Update existing masks to match the overlay's sorting layer and order range.
        foreach (var kv in masksById)
        {
            var m = kv.Value;
            if (m == null) continue;
            var mr = m.GetComponent<Renderer>();
            if (mr != null) mr.sortingLayerID = bestLayerId;
            m.backSortingOrder = overlayRenderer.sortingOrder - 1;
            m.frontSortingOrder = overlayRenderer.sortingOrder + 1;
        }
    }

    void EnsureMaskForTransform(Transform follow, float radius, int forcedId)
    {
        if (follow == null) return;
        int id = forcedId;

        if (!masksById.TryGetValue(id, out var mask) || mask == null)
        {
            var go = new GameObject(forcedId == -100 ? "VisionMask_Player" : "VisionMask");
            go.transform.SetParent(follow, false);
            go.transform.localPosition = Vector3.zero;

            mask = go.AddComponent<SpriteMask>();
            mask.sprite = GetOrCreateCircleSprite();
            mask.isCustomRangeActive = true;
            int overlayOrder = overlayRenderer != null ? overlayRenderer.sortingOrder : overlaySortingOrder;
            mask.backSortingOrder = overlayOrder - 1;
            mask.frontSortingOrder = overlayOrder + 1;

            // Put masks in the same sorting layer as overlay (updated later if we have player sprite).
            if (overlayRenderer != null)
            {
                var rend = go.GetComponent<Renderer>();
                if (rend != null) rend.sortingLayerID = overlayRenderer.sortingLayerID;
            }

            masksById[id] = mask;
        }

        // Scale mask so circle radius matches world radius.
        float radiusWorld = Mathf.Max(0.1f, radius);
        float spriteRadius = mask.sprite != null ? (mask.sprite.bounds.size.x * 0.5f) : 0.5f;
        float s = spriteRadius > 0.0001f ? (radiusWorld / spriteRadius) : radiusWorld;
        mask.transform.localScale = new Vector3(s, s, 1f);

        mask.gameObject.SetActive(effectActive);
    }

    void UpdateMaskPositions()
    {
        // Masks are parented to the target transforms so they follow automatically.
        // This method exists in case we later want smoothing; for now, it's intentionally empty.
    }

    static Sprite GetOrCreateCircleSprite()
    {
        if (cachedCircleSprite != null) return cachedCircleSprite;

        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        var pixels = new Color32[size * size];
        float r = (size - 2) * 0.5f;
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        float r2 = r * r;

        // Soft edge so vision looks nicer (small feather).
        float feather = 6f;
        float inner = Mathf.Max(0f, r - feather);
        float inner2 = inner * inner;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float d2 = dx * dx + dy * dy;
                if (d2 <= inner2)
                {
                    pixels[y * size + x] = new Color32(255, 255, 255, 255);
                }
                else if (d2 <= r2)
                {
                    float d = Mathf.Sqrt(d2);
                    float a = Mathf.InverseLerp(r, inner, d); // 0 at edge, 1 at inner
                    byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
                else
                {
                    pixels[y * size + x] = new Color32(255, 255, 255, 0);
                }
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        cachedCircleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        cachedCircleSprite.name = "VisionMaskCircle";
        return cachedCircleSprite;
    }

    static Sprite GetOrCreateOnePixelSprite()
    {
        if (cachedOnePixelSprite != null) return cachedOnePixelSprite;
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Point;
        tex.SetPixel(0, 0, Color.white);
        tex.Apply(false, true);
        cachedOnePixelSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        cachedOnePixelSprite.name = "VisionOverlayPixel";
        return cachedOnePixelSprite;
    }

    public static void ResetForRestart()
    {
        var all = Resources.FindObjectsOfTypeAll<VisionLimiter>();
        for (int i = 0; i < all.Length; i++)
        {
            var v = all[i];
            if (v == null) continue;
            Destroy(v.gameObject);
        }
        instance = null;
    }
}

