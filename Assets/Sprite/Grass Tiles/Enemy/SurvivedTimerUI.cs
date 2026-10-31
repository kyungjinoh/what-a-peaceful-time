using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class SurvivedTimerUI : MonoBehaviour
{
    const string SurvivedObjectName = "survived";

    TMP_Text text;
    float survivedSeconds;

    public static void ResetForRestart()
    {
        // This UI is DontDestroyOnLoad, so restarting the scene won't call Awake again.
        // Reset any existing timer instances explicitly.
        var all = Resources.FindObjectsOfTypeAll<SurvivedTimerUI>();
        for (int i = 0; i < all.Length; i++)
        {
            var ui = all[i];
            if (ui == null) continue;
            ui.ResetTimerInternal();
        }
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        PlayerHealth.OnPlayerDied += OnPlayerDied;
        ResolveText();
        survivedSeconds = 0f;
    }

    void OnDestroy()
    {
        PlayerHealth.OnPlayerDied -= OnPlayerDied;
    }

    void Update()
    {
        ResolveText();

        if (!GameStartReplayUI.IsRunning) return;

        survivedSeconds += Time.unscaledDeltaTime;
        if (text != null)
            text.text = $"Survived: {survivedSeconds:0.0}s";
    }

    void OnPlayerDied()
    {
        // Stop incrementing; UI stays showing last survived time.
        if (text != null)
            text.text = $"Survived: {survivedSeconds:0.0}s";
    }

    void ResetTimerInternal()
    {
        survivedSeconds = 0f;
        ResolveText();
        if (text != null)
            text.text = $"Survived: {survivedSeconds:0.0}s";
    }

    void ResolveText()
    {
        if (text != null) return;

        var go = GameObject.Find(SurvivedObjectName);
        if (go != null) text = go.GetComponentInChildren<TMP_Text>(true);

        if (text == null)
        {
            var all = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;
                if (!t.gameObject.scene.IsValid()) continue;
                if (t.name != SurvivedObjectName) continue;
                text = t.GetComponentInChildren<TMP_Text>(true);
                if (text != null) break;
            }
        }
    }
}

