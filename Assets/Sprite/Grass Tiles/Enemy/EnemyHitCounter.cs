using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyHitCounter : MonoBehaviour
{
    public static System.Action<EnemyHitCounter> OnKilled;
    [Tooltip("How many hits this enemy can take before dying.")]
    public int hitsToDie = 3;

    [Tooltip("How long to flash red after being hit.")]
    public float hitFlashSeconds = 0.12f;

    [Tooltip("Color used to flash when hit.")]
    public Color hitColor = new Color(1f, 0.15f, 0.15f, 1f);

    int hitsTaken;
    SpriteRenderer sr;
    Color originalColor = Color.white;
    Coroutine flashRoutine;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null) originalColor = sr.color;
    }

    public void TakeHit(int amount = 1)
    {
        hitsTaken += Mathf.Max(1, amount);

        if (sr != null)
        {
            if (flashRoutine != null) StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(Flash());
        }

        if (hitsTaken >= hitsToDie)
            KillRootEnemy();
    }

    void KillRootEnemy()
    {
        // Always destroy the actual enemy root (the object that owns EnemyAI),
        // not just whichever child happened to receive this component.
        var ai = GetComponent<EnemyAI>();
        if (ai == null) ai = GetComponentInParent<EnemyAI>();
        if (ai == null) ai = GetComponentInChildren<EnemyAI>();

        GameObject root = ai != null ? ai.gameObject : transform.root.gameObject;

        // Prevent further hits/attacks immediately.
        var melee = root.GetComponent<EnemyMeleeAttack>();
        if (melee != null) melee.enabled = false;
        var combat = root.GetComponent<EnemyCombat>();
        if (combat != null) combat.enabled = false;
        if (ai != null) ai.enabled = false;

        Destroy(root);
    }

    void OnDestroy()
    {
        // Only count as "killed" if we reached the death threshold.
        if (hitsTaken >= hitsToDie)
            OnKilled?.Invoke(this);
    }

    IEnumerator Flash()
    {
        sr.color = hitColor;
        yield return new WaitForSeconds(Mathf.Max(0.01f, hitFlashSeconds));
        sr.color = originalColor;
        flashRoutine = null;
    }
}

