using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyDangerTint : MonoBehaviour
{
    [Tooltip("How often to change tint (seconds).")]
    public float intervalSeconds = 0.1f;

    [Tooltip("Tint applied when flashing.")]
    public Color dangerColor = new Color(1f, 0.15f, 0.15f, 1f);

    SpriteRenderer sr;
    Color original;
    Coroutine loop;
    bool on;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null) original = sr.color;
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
        if (sr != null) sr.color = original;
        on = false;
    }

    IEnumerator Loop()
    {
        var wait = new WaitForSeconds(Mathf.Max(0.02f, intervalSeconds));
        while (true)
        {
            if (sr != null)
            {
                on = !on;
                sr.color = on ? dangerColor : original;
            }
            yield return wait;
        }
    }
}

