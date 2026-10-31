using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

[DisallowMultipleComponent]
public class SpecialMoveManager : MonoBehaviour
{
    static SpecialMoveManager instance;

    [Header("Input")]
    public KeyCode specialKey = KeyCode.Q;

    [Header("Cooldown")]
    [Tooltip("Seconds after run start before special is first available.")]
    public float initialUnlockSeconds = 20f;
    [Tooltip("Cooldown seconds after using special.")]
    public float cooldownSeconds = 60f;

    [Header("Video")]
    [Tooltip("Filename in StreamingAssets to play for the special move.")]
    public string streamingAssetsVideoFileName = "grok-video-aa61f764-1670-4ed5-be2f-341e6c06a2c2.mp4";

    [Header("Special Fade")]
    [Tooltip("Seconds to fade to/from black when starting/ending the special video.")]
    public float specialFadeSeconds = 0.5f;

    [Header("Spawner Boost (after special)")]
    [Tooltip("Multiply spawn intervals by this after special (smaller = faster spawns).")]
    [Range(0.1f, 1f)]
    public float spawnIntervalMultiplierAfterSpecial = 0.6f;

    [Header("Progress Indicator (Square SpriteRenderer)")]
    [Tooltip("Scene object name of the progress indicator (SpriteRenderer). Example: 'Square'.")]
    public string progressObjectName = "Square";
    [Tooltip("If true, pins the indicator to bottom-right of the camera view.")]
    public bool pinToScreen = true;
    [Tooltip("Bottom-right margin in screen pixels (x from right, y from bottom).")]
    public Vector2 screenMarginPixels = new Vector2(24f, 24f);
    [Tooltip("On-screen size of the indicator in pixels (width, height).")]
    public Vector2 indicatorSizePixels = new Vector2(90f, 14f);
    [Tooltip("Sorting order used to force indicator on top.")]
    public int progressSortingOrder = 32750;
    [Tooltip("When empty, keep a tiny visible sliver so you can still see it.")]
    [Range(0f, 0.2f)]
    public float emptyVisibleSliver = 0.03f;
    [Tooltip("Color while NOT ready (charging).")]
    public Color chargingColor = new Color(1.0f, 0.35f, 0.75f, 0.95f); // pink
    [Tooltip("Base color when ready (filled).")]
    public Color readyColor = new Color(1f, 0.15f, 0.15f, 1f); // red
    [Tooltip("Flicker highlight color when ready (lerps with Ready Color).")]
    public Color readyFlickerHighlightColor = new Color(1f, 1f, 1f, 1f);
    [Tooltip("Blink speed (higher = faster).")]
    public float readyBlinkSpeed = 3.5f;

    float readyAtUnscaledTime;
    float lastCooldownDuration;
    bool lastRunning;

    // Video playback
    VideoPlayer videoPlayer;
    AudioSource videoAudio;
    bool playingVideo;

    // Fade overlay
    SpriteRenderer fadeRenderer;
    static Sprite onePixelSprite;

    // Progress SpriteRenderer (your Square)
    SpriteRenderer progressRenderer;
    Vector3 progressInitialScale = Vector3.one;
    Vector3 progressInitialPos = Vector3.zero;
    Color progressInitialColor = Color.white;
    bool initializedIndicatorSize;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;
        GameStartReplayUI.OnRunStarted -= HandleRunStarted;
        GameStartReplayUI.OnRunStarted += HandleRunStarted;
        GameStartReplayUI.OnRunStopped -= HandleRunStopped;
        GameStartReplayUI.OnRunStopped += HandleRunStopped;

        EnsureProgressRenderer();
        EnsureVideo();
        HandleRunStopped(); // start in "not running" state
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
        // Re-bind camera in case Camera.main changed across scenes.
        if (videoPlayer != null)
            videoPlayer.targetCamera = Camera.main;

        // Re-bind progress renderer (scene objects change on reload).
        progressRenderer = null;
        initializedIndicatorSize = false;
        EnsureProgressRenderer();

        // Re-bind camera for fade overlay.
        if (fadeRenderer != null)
            UpdateFadeOverlayTransform();
    }

    void HandleRunStarted()
    {
        float now = Time.unscaledTime;
        lastCooldownDuration = Mathf.Max(0.01f, initialUnlockSeconds);
        readyAtUnscaledTime = now + lastCooldownDuration;
        lastRunning = true;
        EnsureProgressRenderer();
        SetProgress01(0f, now, isReady: false); // always start blank
        UpdateUi(now);
    }

    void HandleRunStopped()
    {
        lastRunning = false;
        playingVideo = false;
        if (videoPlayer != null)
        {
            videoPlayer.Stop();
            videoPlayer.targetCameraAlpha = 0f;
        }
        if (videoAudio != null) videoAudio.Stop();
        // Hide progress while not running.
        if (progressRenderer != null) progressRenderer.enabled = false;
        // Never leave a black overlay covering the start UI.
        if (fadeRenderer != null)
            fadeRenderer.color = new Color(0f, 0f, 0f, 0f);
    }

    void Update()
    {
        bool running = GameStartReplayUI.IsRunning;
        if (!running)
        {
            if (lastRunning) HandleRunStopped();
            lastRunning = false;
            return;
        }

        if (!lastRunning)
        {
            // Fallback if events missed.
            HandleRunStarted();
        }

        float now = Time.unscaledTime;
        UpdateUi(now);
        UpdateProgressScreenPin();

        if (playingVideo) return;

        if (Input.GetKeyDown(specialKey) && now >= readyAtUnscaledTime)
        {
            StartCoroutine(DoSpecial());
        }

        lastRunning = true;
    }

    IEnumerator DoSpecial()
    {
        playingVideo = true;

        // Put special on cooldown immediately.
        float now = Time.unscaledTime;
        lastCooldownDuration = Mathf.Max(0.01f, cooldownSeconds);
        readyAtUnscaledTime = now + lastCooldownDuration;
        UpdateUi(now);

        // Freeze gameplay while the video plays.
        float prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        // Pause BGM while the video plays (restored on end).
        GameAudioManager.Instance.PauseBgm();

        // Fade game out smoothly, then fade the video in.
        EnsureFadeOverlay();
        yield return FadeOverlayTo(1f, specialFadeSeconds);

        EnsureVideo();
        if (videoPlayer == null)
        {
            Time.timeScale = prevTimeScale;
            GameAudioManager.Instance.ResumeBgm();
            playingVideo = false;
            yield break;
        }

        string url = GetStreamingAssetsVideoUrl();
        if (!string.IsNullOrEmpty(url))
            videoPlayer.url = url;

        // Start with video invisible (alpha 0) and crossfade from black overlay.
        videoPlayer.targetCameraAlpha = 0f;

        bool prepared = false;
        bool ended = false;

        void OnPrepared(VideoPlayer vp) => prepared = true;
        void OnEnded(VideoPlayer vp) => ended = true;

        videoPlayer.prepareCompleted += OnPrepared;
        videoPlayer.loopPointReached += OnEnded;

        videoPlayer.Prepare();
        float prepareTimeoutAt = Time.unscaledTime + 5f;
        while (!prepared && Time.unscaledTime < prepareTimeoutAt)
            yield return null;

        // Play even if not fully prepared (best effort).
        videoPlayer.Play();

        // Crossfade: black overlay -> transparent while video alpha -> 1
        yield return CrossFadeVideoAndOverlay(toVideoAlpha: 1f, toOverlayAlpha: 0f, specialFadeSeconds);

        // Wait until it ends (or fails fast).
        float safetyTimeoutAt = Time.unscaledTime + 60f;
        while (!ended && Time.unscaledTime < safetyTimeoutAt && videoPlayer.isPlaying)
            yield return null;

        // Fade out video back to black.
        yield return CrossFadeVideoAndOverlay(toVideoAlpha: 0f, toOverlayAlpha: 1f, specialFadeSeconds);

        videoPlayer.prepareCompleted -= OnPrepared;
        videoPlayer.loopPointReached -= OnEnded;
        videoPlayer.Stop();

        // Fade black back to gameplay.
        yield return FadeOverlayTo(0f, specialFadeSeconds);

        // Restore gameplay.
        Time.timeScale = prevTimeScale <= 0f ? 1f : prevTimeScale;

        // Kill all enemies, then boost respawn speed.
        KillAllEnemies();
        BoostRespawnAfterSpecial();

        // Resume BGM (blackout system may pause it separately later).
        GameAudioManager.Instance.ResumeBgm();

        playingVideo = false;
    }

    void KillAllEnemies()
    {
        var all = Object.FindObjectsOfType<EnemyAI>();
        for (int i = 0; i < all.Length; i++)
        {
            var ai = all[i];
            if (ai == null) continue;
            Destroy(ai.gameObject);
        }
    }

    void BoostRespawnAfterSpecial()
    {
        var dup = Object.FindFirstObjectByType<EnemyTorchDuplicator>();
        if (dup == null) return;
        dup.ApplySpecialSpawnBoost(Mathf.Clamp(spawnIntervalMultiplierAfterSpecial, 0.1f, 1f));
    }

    string GetStreamingAssetsVideoUrl()
    {
        if (string.IsNullOrWhiteSpace(streamingAssetsVideoFileName))
            return null;
        string path = Path.Combine(Application.streamingAssetsPath, streamingAssetsVideoFileName);
        if (!File.Exists(path)) return path; // still return path; platform may stream it even if File.Exists fails
        return path;
    }

    void EnsureVideo()
    {
        if (videoPlayer != null) return;

        videoPlayer = gameObject.GetComponent<VideoPlayer>();
        if (videoPlayer == null) videoPlayer = gameObject.AddComponent<VideoPlayer>();

        // Audio
        videoAudio = gameObject.GetComponent<AudioSource>();
        if (videoAudio == null) videoAudio = gameObject.AddComponent<AudioSource>();
        videoAudio.playOnAwake = false;
        videoAudio.loop = false;
        videoAudio.spatialBlend = 0f;

        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = false;
        videoPlayer.source = VideoSource.Url;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        videoPlayer.EnableAudioTrack(0, true);
        videoPlayer.SetTargetAudioSource(0, videoAudio);

        // Fullscreen on the main camera.
        videoPlayer.renderMode = VideoRenderMode.CameraNearPlane;
        videoPlayer.targetCamera = Camera.main;
        // Fill the whole screen regardless of aspect ratio (may crop edges).
        videoPlayer.aspectRatio = VideoAspectRatio.FitOutside;
        videoPlayer.targetCameraAlpha = 1f;
    }

    void EnsureFadeOverlay()
    {
        if (fadeRenderer != null) return;

        var go = new GameObject("SpecialFadeOverlay");
        go.transform.SetParent(transform, false);
        fadeRenderer = go.AddComponent<SpriteRenderer>();
        fadeRenderer.sprite = GetOrCreateOnePixelSprite();
        fadeRenderer.color = new Color(0f, 0f, 0f, 0f);
        fadeRenderer.maskInteraction = SpriteMaskInteraction.None;

        // Ensure overlay is always on top of everything.
        int bestLayerId = fadeRenderer.sortingLayerID;
        int bestLayerValue = SortingLayer.GetLayerValueFromID(bestLayerId);
        var layers = SortingLayer.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            int v = layers[i].value;
            if (v > bestLayerValue)
            {
                bestLayerValue = v;
                bestLayerId = layers[i].id;
            }
        }
        fadeRenderer.sortingLayerID = bestLayerId;
        fadeRenderer.sortingOrder = 32767;

        UpdateFadeOverlayTransform();
    }

    void UpdateFadeOverlayTransform()
    {
        if (fadeRenderer == null) return;
        var cam = Camera.main;
        if (cam == null) return;

        var p = cam.transform.position;
        fadeRenderer.transform.position = new Vector3(p.x, p.y, 0f);
        fadeRenderer.transform.localScale = new Vector3(2000f, 2000f, 1f);
    }

    IEnumerator FadeOverlayTo(float targetAlpha, float seconds)
    {
        EnsureFadeOverlay();
        UpdateFadeOverlayTransform();

        float dur = Mathf.Max(0.0001f, seconds);
        float start = fadeRenderer != null ? fadeRenderer.color.a : 0f;
        float t0 = Time.unscaledTime;
        float endAt = t0 + dur;
        while (Time.unscaledTime < endAt)
        {
            UpdateFadeOverlayTransform();
            float u = Mathf.InverseLerp(t0, endAt, Time.unscaledTime);
            float a = Mathf.Lerp(start, targetAlpha, u);
            if (fadeRenderer != null)
                fadeRenderer.color = new Color(0f, 0f, 0f, a);
            yield return null;
        }
        if (fadeRenderer != null)
            fadeRenderer.color = new Color(0f, 0f, 0f, targetAlpha);
    }

    IEnumerator CrossFadeVideoAndOverlay(float toVideoAlpha, float toOverlayAlpha, float seconds)
    {
        EnsureFadeOverlay();
        UpdateFadeOverlayTransform();

        float dur = Mathf.Max(0.0001f, seconds);
        float startOverlay = fadeRenderer != null ? fadeRenderer.color.a : 0f;
        float startVideo = videoPlayer != null ? videoPlayer.targetCameraAlpha : 0f;

        float t0 = Time.unscaledTime;
        float endAt = t0 + dur;
        while (Time.unscaledTime < endAt)
        {
            UpdateFadeOverlayTransform();
            float u = Mathf.InverseLerp(t0, endAt, Time.unscaledTime);

            float oa = Mathf.Lerp(startOverlay, toOverlayAlpha, u);
            float va = Mathf.Lerp(startVideo, toVideoAlpha, u);

            if (fadeRenderer != null)
                fadeRenderer.color = new Color(0f, 0f, 0f, oa);
            if (videoPlayer != null)
                videoPlayer.targetCameraAlpha = va;

            yield return null;
        }

        if (fadeRenderer != null)
            fadeRenderer.color = new Color(0f, 0f, 0f, toOverlayAlpha);
        if (videoPlayer != null)
            videoPlayer.targetCameraAlpha = toVideoAlpha;
    }

    static Sprite GetOrCreateOnePixelSprite()
    {
        if (onePixelSprite != null) return onePixelSprite;
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Point;
        tex.SetPixel(0, 0, Color.white);
        tex.Apply(false, true);
        onePixelSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        onePixelSprite.name = "SpecialFadePixel";
        return onePixelSprite;
    }

    void EnsureProgressRenderer()
    {
        if (progressRenderer != null) return;
        if (string.IsNullOrWhiteSpace(progressObjectName)) return;

        var go = FindSceneObjectByName(progressObjectName);
        if (go == null) return;

        progressRenderer = go.GetComponent<SpriteRenderer>();
        if (progressRenderer == null) return;

        progressInitialScale = progressRenderer.transform.localScale;
        progressInitialPos = progressRenderer.transform.localPosition;
        progressInitialColor = progressRenderer.color;

        ApplyTopmostSorting(progressRenderer);
        progressRenderer.enabled = GameStartReplayUI.IsRunning;
        progressRenderer.color = chargingColor;
        InitializeIndicatorSizeIfNeeded();
        // Start empty by default; UpdateUi will drive actual value.
        SetProgress01(0f, Time.unscaledTime, isReady: false);
        UpdateProgressScreenPin();
    }

    void InitializeIndicatorSizeIfNeeded()
    {
        if (initializedIndicatorSize) return;
        if (!pinToScreen) return;
        if (progressRenderer == null) return;
        var cam = Camera.main;
        if (cam == null) return;

        // Convert desired pixel size to world size at the indicator's Z plane.
        float z = progressRenderer.transform.position.z;
        float dist = Mathf.Abs(cam.transform.position.z - z);
        Vector3 a = cam.ScreenToWorldPoint(new Vector3(0f, 0f, dist));
        Vector3 b = cam.ScreenToWorldPoint(new Vector3(indicatorSizePixels.x, indicatorSizePixels.y, dist));
        Vector3 worldSize = b - a;

        // Compute scale needed to make the sprite match that world size.
        if (progressRenderer.sprite != null)
        {
            Vector2 spriteSize = progressRenderer.sprite.bounds.size;
            float sx = spriteSize.x > 0.0001f ? (worldSize.x / spriteSize.x) : 1f;
            float sy = spriteSize.y > 0.0001f ? (worldSize.y / spriteSize.y) : 1f;
            progressInitialScale = new Vector3(Mathf.Abs(sx), Mathf.Abs(sy), progressRenderer.transform.localScale.z);
            progressRenderer.transform.localScale = progressInitialScale;
        }

        progressInitialPos = progressRenderer.transform.localPosition;
        initializedIndicatorSize = true;
    }

    void UpdateProgressScreenPin()
    {
        if (!pinToScreen) return;
        if (progressRenderer == null) return;
        var cam = Camera.main;
        if (cam == null) return;

        InitializeIndicatorSizeIfNeeded();

        float z = progressRenderer.transform.position.z;
        float dist = Mathf.Abs(cam.transform.position.z - z);
        // screenMarginPixels is from the screen edges, but our SpriteRenderer is centered.
        // Inset by half the indicator size so it never gets cut off.
        float halfW = Mathf.Max(0f, indicatorSizePixels.x * 0.5f);
        float halfH = Mathf.Max(0f, indicatorSizePixels.y * 0.5f);
        Vector3 screenPos = new Vector3(
            Screen.width - screenMarginPixels.x - halfW,
            screenMarginPixels.y + halfH,
            dist
        );
        Vector3 world = cam.ScreenToWorldPoint(screenPos);
        world.z = z;
        progressRenderer.transform.position = world;
    }

    void ApplyTopmostSorting(SpriteRenderer sr)
    {
        if (sr == null) return;
        int bestLayerId = sr.sortingLayerID;
        int bestLayerValue = SortingLayer.GetLayerValueFromID(bestLayerId);
        var layers = SortingLayer.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            int v = layers[i].value;
            if (v > bestLayerValue)
            {
                bestLayerValue = v;
                bestLayerId = layers[i].id;
            }
        }
        sr.sortingLayerID = bestLayerId;
        sr.sortingOrder = progressSortingOrder;
    }

    static GameObject FindSceneObjectByName(string name)
    {
        // Active objects only
        var go = GameObject.Find(name);
        if (go != null) return go;

        // Inactive objects too
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

    void SetProgress01(float t01, float nowUnscaled, bool isReady)
    {
        if (progressRenderer == null) return;

        float t = Mathf.Clamp01(t01);
        if (t <= 0.0001f && emptyVisibleSliver > 0f)
            t = Mathf.Clamp01(emptyVisibleSliver);
        progressRenderer.enabled = GameStartReplayUI.IsRunning;

        // "Start blank and fill": scale the square in X from 0..1 while keeping Y/Z.
        // We also offset position so the square appears to fill from left to right.
        float sx0 = progressInitialScale.x;
        float sx = sx0 * t;
        var s = progressRenderer.transform.localScale;
        progressRenderer.transform.localScale = new Vector3(sx, progressInitialScale.y, progressInitialScale.z);

        float worldWidth = 1f;
        if (progressRenderer.sprite != null)
            worldWidth = Mathf.Abs(progressRenderer.sprite.bounds.size.x) * Mathf.Abs(sx0);

        // Shift so left edge stays in place while scaling.
        float dx = (worldWidth * 0.5f) * (1f - t);
        progressRenderer.transform.localPosition = new Vector3(progressInitialPos.x - dx, progressInitialPos.y, progressInitialPos.z);

        if (isReady)
        {
            float speed = Mathf.Max(0.1f, readyBlinkSpeed);
            float pulse = 0.5f + 0.5f * Mathf.Sin(nowUnscaled * speed * Mathf.PI * 2f);
            progressRenderer.color = Color.Lerp(readyColor, readyFlickerHighlightColor, pulse);
        }
        else
        {
            progressRenderer.color = chargingColor;
        }
    }

    void UpdateUi(float nowUnscaled)
    {
        EnsureProgressRenderer();
        if (progressRenderer == null) return;
        if (!GameStartReplayUI.IsRunning) return;

        float remaining = Mathf.Max(0f, readyAtUnscaledTime - nowUnscaled);
        float duration = Mathf.Max(0.01f, lastCooldownDuration);
        float t = 1f - Mathf.Clamp01(remaining / duration); // 0..1 fill
        bool ready = remaining <= 0.0001f && !playingVideo;
        SetProgress01(t, nowUnscaled, ready);
    }

    public static void ResetForRestart()
    {
        var all = Resources.FindObjectsOfTypeAll<SpecialMoveManager>();
        for (int i = 0; i < all.Length; i++)
        {
            var s = all[i];
            if (s == null) continue;
            Destroy(s.gameObject);
        }
        instance = null;
    }
}

