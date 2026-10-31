using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemySpeedPulse : MonoBehaviour
{
    [Tooltip("Multiplier during sudden boost.")]
    public float burstMultiplier = 5f;

    [Tooltip("How long each boost lasts.")]
    public float burstDurationSeconds = 0.5f;

    [Tooltip("Time between boosts.")]
    public float burstCooldownSeconds = 3f;

    EnemyAI enemyAI;
    Coroutine loop;

    void Awake()
    {
        enemyAI = GetComponent<EnemyAI>();
    }

    void OnEnable()
    {
        if (loop == null) loop = StartCoroutine(Loop());
    }

    void OnDisable()
    {
        if (loop != null)
        {
            StopCoroutine(loop);
            loop = null;
        }

        if (enemyAI != null)
            enemyAI.speedMultiplier = 1f;
    }

    IEnumerator Loop()
    {
        // Let other components initialize first.
        yield return null;

        while (true)
        {
            if (!GameStartReplayUI.IsRunning)
            {
                if (enemyAI != null) enemyAI.speedMultiplier = 1f;
                yield return null;
                continue;
            }

            if (enemyAI != null)
                enemyAI.speedMultiplier = Mathf.Max(0f, burstMultiplier);

            yield return new WaitForSeconds(Mathf.Max(0.01f, burstDurationSeconds));

            if (enemyAI != null)
                enemyAI.speedMultiplier = 1f;

            yield return new WaitForSeconds(Mathf.Max(0.01f, burstCooldownSeconds));
        }
    }
}

