using UnityEngine;
using TMPro;

public class PlayerHealth : MonoBehaviour
{
    public static System.Action OnPlayerDied;

    public int currentHealth;
    public int maxHealth = 100;
    public TMP_Text healthText;

    // Auto-heal (no Inspector setup required)
    const float HealDelayAfterDamageSeconds = 1.3f;
    const float HealPerSecond = 9.0f;
    float lastDamagedTime = -999f;
    float healAccumulator;
    bool died;

    void Awake() {
        TryResolveHealthText();
        _ = GameAudioManager.Instance;

        // Ensure Enter-attack exists without manual setup.
        if (GetComponent<PlayerEnterAttack>() == null)
            gameObject.AddComponent<PlayerEnterAttack>();

        // Ensure game start/replay UI manager exists without manual setup.
        if (FindAnyGameStartReplayUI() == null)
            new GameObject("GameStartReplayUI").AddComponent<GameStartReplayUI>();

        // Ensure survived timer exists without manual setup.
        if (Object.FindFirstObjectByType<SurvivedTimerUI>() == null)
            new GameObject("SurvivedTimerUI").AddComponent<SurvivedTimerUI>();

        // Ensure periodic vision limiter exists without manual setup.
        if (Object.FindFirstObjectByType<VisionLimiter>() == null)
            new GameObject("VisionLimiter").AddComponent<VisionLimiter>();

        // Ensure special move manager exists without manual setup.
        if (Object.FindFirstObjectByType<SpecialMoveManager>() == null)
            new GameObject("SpecialMoveManager").AddComponent<SpecialMoveManager>();
    }

    static GameStartReplayUI FindAnyGameStartReplayUI()
    {
        var mgr = Object.FindFirstObjectByType<GameStartReplayUI>();
        if (mgr != null) return mgr;
        var all = Resources.FindObjectsOfTypeAll<GameStartReplayUI>();
        for (int i = 0; i < all.Length; i++)
        {
            var m = all[i];
            if (m == null) continue;
            if (!m.gameObject.scene.IsValid()) continue;
            return m;
        }
        return null;
    }

    private void Start() {
        // Initialize to full health on scene start.
        currentHealth = Mathf.Clamp(maxHealth, 0, int.MaxValue);
        TryResolveHealthText();
        UpdateHealthText();
    }

    void Update()
    {
        if (currentHealth <= 0) return;
        if (currentHealth >= maxHealth) return;
        if (Time.time - lastDamagedTime < HealDelayAfterDamageSeconds) return;

        healAccumulator += HealPerSecond * Time.deltaTime;
        int healInt = Mathf.FloorToInt(healAccumulator);
        if (healInt <= 0) return;

        healAccumulator -= healInt;
        currentHealth = Mathf.Clamp(currentHealth + healInt, 0, maxHealth);
        UpdateHealthText();
    }

    public void ChangeHealth(int amount)
    {
        // Any damage (negative amount) pauses healing for a short window.
        if (amount < 0)
        {
            GameAudioManager.Instance.PlayPlayerHurt();
            lastDamagedTime = Time.time;
            healAccumulator = 0f;
        }

        currentHealth = Mathf.Clamp(currentHealth + amount, 0, maxHealth);

        UpdateHealthText();

        if (currentHealth <= 0)
        {
            if (!died)
            {
                died = true;
                OnPlayerDied?.Invoke();
            }
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;

            var col = GetComponent<Collider2D>();
            if (col != null) col.enabled = false;

            var movement = GetComponent<PlayerMovement>();
            if (movement != null) movement.enabled = false;
        }
    }

    void UpdateHealthText()
    {
        if (healthText != null)
        {
            healthText.text = $"HP: {currentHealth}/{maxHealth}";
        }
    }

    bool TryResolveHealthText() {
        if (healthText != null) return true;

        // Works for active objects.
        var healthUi = GameObject.Find("Health UI");
        if (healthUi != null) {
            healthText = healthUi.GetComponentInChildren<TMP_Text>(true);
            if (healthText != null) return true;
        }

        // Works even if UI objects are inactive.
        var allTexts = Resources.FindObjectsOfTypeAll<TMP_Text>();
        for (int i = 0; i < allTexts.Length; i++) {
            var t = allTexts[i];
            if (t == null) continue;
            if (!t.gameObject.scene.IsValid()) continue; // skip project assets/prefabs

            if (t.transform.parent != null && t.transform.parent.name == "Health UI") {
                healthText = t;
                return true;
            }
        }

        return false;
    }
}