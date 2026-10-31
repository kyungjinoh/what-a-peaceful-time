using UnityEngine;

[DisallowMultipleComponent]
public class GameAudioManager : MonoBehaviour
{
    static GameAudioManager instance;

    AudioSource bgmSource;
    AudioSource sfxSource;

    AudioClip bgmClip;
    AudioClip playerHurtClip;
    AudioClip enemyAttackClip;
    AudioClip playerAttackClip;

    bool bgmStarted;
    bool loggedMissing;

    // Build-safe loading:
    // Unity does NOT include arbitrary assets referenced only by string paths.
    // To make audio work in builds, put the clips under: Assets/Resources/Audio/
    // Then they can be loaded with Resources.Load using these keys (no extension).
    const string BgmResourcesKey = "Audio/soundgallerybydmitrytaras-retro-501327";
    const string PlayerHurtResourcesKey = "Audio/phatphrogstudio-rpg-female-attack-grunt-no-ai-481720";
    const string EnemyAttackResourcesKey = "Audio/freesound-community-knife-stab-pull-7005";
    const string PlayerAttackResourcesKey = "Audio/freesounds123-heavy-metal-crush-351840";

#if UNITY_EDITOR
    const string BgmAssetPath = "Assets/Sprite/Grass Tiles/Enemy/soundgallerybydmitrytaras-retro-501327.mp3";
    const string PlayerHurtAssetPath = "Assets/phatphrogstudio-rpg-female-attack-grunt-no-ai-481720_rD5srsux.mp3";
    const string EnemyAttackAssetPath = "Assets/freesound-community-knife-stab-pull-7005_rXLxzLd5.mp3";
    const string PlayerAttackAssetPath = "Assets/freesounds123-heavy-metal-crush-351840_dZbcONkH.mp3";
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureInstanceOnPlay()
    {
        _ = Instance;
    }

    public static GameAudioManager Instance
    {
        get
        {
            if (instance != null) return instance;
            var go = new GameObject("GameAudioManager");
            instance = go.AddComponent<GameAudioManager>();
            return instance;
        }
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);

        bgmSource = gameObject.AddComponent<AudioSource>();
        sfxSource = gameObject.AddComponent<AudioSource>();

        bgmSource.loop = true;
        bgmSource.playOnAwake = false;
        bgmSource.volume = 0.45f;

        sfxSource.loop = false;
        sfxSource.playOnAwake = false;
        sfxSource.volume = 1f;

        ResolveClips();
    }

    void ResolveClips()
    {
        bgmClip = LoadClip(
#if UNITY_EDITOR
            BgmAssetPath,
#else
            null,
#endif
            BgmResourcesKey,
            "soundgallerybydmitrytaras-retro-501327"
        );
        playerHurtClip = LoadClip(
#if UNITY_EDITOR
            PlayerHurtAssetPath,
#else
            null,
#endif
            PlayerHurtResourcesKey,
            "phatphrogstudio-rpg-female-attack-grunt-no-ai-481720"
        );
        enemyAttackClip = LoadClip(
#if UNITY_EDITOR
            EnemyAttackAssetPath,
#else
            null,
#endif
            EnemyAttackResourcesKey,
            "freesound-community-knife-stab-pull-7005"
        );
        playerAttackClip = LoadClip(
#if UNITY_EDITOR
            PlayerAttackAssetPath,
#else
            null,
#endif
            PlayerAttackResourcesKey,
            "freesounds123-heavy-metal-crush-351840"
        );

        if (!loggedMissing && (bgmClip == null || playerHurtClip == null || enemyAttackClip == null || playerAttackClip == null))
        {
            loggedMissing = true;
            Debug.LogWarning(
                "[GameAudioManager] One or more clips were not found.\n" +
                "In builds, put the audio files under Assets/Resources/Audio/ so Resources.Load can find them.\n" +
                "Keys used: " + BgmResourcesKey + ", " + PlayerHurtResourcesKey + ", " + EnemyAttackResourcesKey + ", " + PlayerAttackResourcesKey
            );
        }
    }

    static AudioClip LoadClip(string assetPathOrNull, string resourcesKey, string fallbackNamePart)
    {
#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(assetPathOrNull))
        {
            var direct = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(assetPathOrNull);
            if (direct != null) return direct;
        }
#endif

        if (!string.IsNullOrEmpty(resourcesKey))
        {
            var r = Resources.Load<AudioClip>(resourcesKey);
            if (r != null) return r;
        }

        // Runtime fallback: tries currently loaded clips by name.
        var all = Resources.FindObjectsOfTypeAll<AudioClip>();
        for (int i = 0; i < all.Length; i++)
        {
            var c = all[i];
            if (c == null) continue;
            if (c.name.Contains(fallbackNamePart)) return c;
        }
        return null;
    }

    public void StartBgmIfNeeded()
    {
        if (bgmStarted) return;
        if (bgmClip == null) ResolveClips();
        if (bgmClip == null)
        {
            Debug.LogWarning("[GameAudioManager] BGM clip missing.");
            return;
        }

        bgmSource.clip = bgmClip;
        bgmSource.Play();
        bgmStarted = true;
    }

    public void PauseBgm()
    {
        if (bgmSource == null) return;
        if (!bgmSource.isPlaying) return;
        bgmSource.Pause();
    }

    public void ResumeBgm()
    {
        if (bgmSource == null) return;
        // If bgm never started yet, starting is the most intuitive "resume".
        if (!bgmStarted) { StartBgmIfNeeded(); return; }
        if (bgmSource.clip == null) bgmSource.clip = bgmClip;
        if (!bgmSource.isPlaying) bgmSource.UnPause();
    }

    public void PlayPlayerHurt()
    {
        PlayOneShot(playerHurtClip);
    }

    public void PlayEnemyAttack()
    {
        PlayOneShot(enemyAttackClip);
    }

    public void PlayPlayerAttack()
    {
        PlayOneShot(playerAttackClip);
    }

    void PlayOneShot(AudioClip clip)
    {
        if (clip == null) ResolveClips();
        if (clip == null)
        {
            Debug.LogWarning("[GameAudioManager] SFX clip missing.");
            return;
        }
        sfxSource.PlayOneShot(clip);
    }
}

