using UnityEngine;

[DisallowMultipleComponent]
public class PlayerEnterAttack : MonoBehaviour
{
    [Tooltip("Attack radius around the player.")]
    public float attackRadius = 2.6f;

    [Tooltip("How many enemies can be hit per attack.")]
    public int maxTargets = 16;

    [Tooltip("Hits applied per press.")]
    public int hitsPerAttack = 1;

    readonly Collider2D[] results = new Collider2D[64];
    readonly int[] seenEnemyIds = new int[64];
    int seenCount;
    LineRenderer radiusRenderer;
    const int CircleSegments = 80;
    SpriteRenderer fillRenderer;
    static Sprite cachedFillSprite;
    Color fillNormal = new Color(1f, 0f, 0f, 0.18f);
    Color fillPressed = new Color(0.6f, 0f, 0f, 0.30f);
    float pressedUntil;

    void Awake()
    {
        // Ensure circle attacks can hit Trigger colliders too (some enemies use triggers).
        Physics2D.queriesHitTriggers = true;
        EnsureRadiusRenderer();
        EnsureFillRenderer();
        UpdateRadiusRenderer();
        UpdateFillRenderer();
    }

    void OnEnable()
    {
        EnsureRadiusRenderer();
        EnsureFillRenderer();
        if (radiusRenderer != null) radiusRenderer.enabled = true;
        if (fillRenderer != null) fillRenderer.enabled = true;
        UpdateRadiusRenderer();
        UpdateFillRenderer();
    }

    void OnDisable()
    {
        if (radiusRenderer != null) radiusRenderer.enabled = false;
        if (fillRenderer != null) fillRenderer.enabled = false;
    }

    void Update()
    {
        UpdateRadiusRenderer();
        UpdateFillRenderer();

        if (!Input.GetKeyDown(KeyCode.Space))
            return;

        GameAudioManager.Instance.PlayPlayerAttack();
        pressedUntil = Time.time + 0.12f;

        int count = Physics2D.OverlapCircleNonAlloc(transform.position, attackRadius, results);
        int hit = 0;
        seenCount = 0;

        for (int i = 0; i < count && hit < maxTargets; i++)
        {
            var col = results[i];
            if (col == null) continue;

            // Dedupe by enemy root so one press = one hit per enemy (even with multiple colliders).
            EnemyAI ai = null;
            // Prefer attached rigidbody root if present (common for compound colliders).
            if (col.attachedRigidbody != null)
                ai = col.attachedRigidbody.GetComponent<EnemyAI>() ?? col.attachedRigidbody.GetComponentInParent<EnemyAI>();
            if (ai == null)
                ai = col.GetComponentInParent<EnemyAI>();
            // Fallback: sometimes colliders live on a sibling object; use scene root.
            if (ai == null && col.transform.root != null)
                ai = col.transform.root.GetComponent<EnemyAI>() ?? col.transform.root.GetComponentInChildren<EnemyAI>(true);
            if (ai == null) continue;
            int id = ai.gameObject.GetInstanceID();
            if (AlreadySeen(id)) continue;
            MarkSeen(id);

            var enemy = ai.GetComponent<EnemyHitCounter>();
            if (enemy == null) enemy = ai.gameObject.AddComponent<EnemyHitCounter>();

            enemy.TakeHit(hitsPerAttack);
            hit++;
        }
    }

    bool AlreadySeen(int id)
    {
        for (int i = 0; i < seenCount; i++)
            if (seenEnemyIds[i] == id) return true;
        return false;
    }

    void MarkSeen(int id)
    {
        if (seenCount >= seenEnemyIds.Length) return;
        seenEnemyIds[seenCount++] = id;
    }

    void EnsureRadiusRenderer()
    {
        if (radiusRenderer != null) return;

        var go = new GameObject("AttackRadius");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        radiusRenderer = go.AddComponent<LineRenderer>();
        radiusRenderer.useWorldSpace = true;
        radiusRenderer.loop = true;
        radiusRenderer.positionCount = CircleSegments;
        radiusRenderer.widthMultiplier = 0.03f;
        radiusRenderer.numCapVertices = 4;
        radiusRenderer.numCornerVertices = 2;
        radiusRenderer.sortingOrder = 9999;
        // Match player's sorting layer so it always shows.
        var playerSr = GetComponent<SpriteRenderer>();
        if (playerSr != null)
            radiusRenderer.sortingLayerID = playerSr.sortingLayerID;

        var mat = new Material(Shader.Find("Sprites/Default"));
        radiusRenderer.material = mat;
        radiusRenderer.startColor = new Color(1f, 0f, 0f, 0.75f);
        radiusRenderer.endColor = new Color(1f, 0f, 0f, 0.75f);
    }

    void EnsureFillRenderer()
    {
        if (fillRenderer != null) return;

        var go = new GameObject("AttackRadiusFill");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        fillRenderer = go.AddComponent<SpriteRenderer>();
        fillRenderer.sortingOrder = 9998; // behind outline
        var playerSr = GetComponent<SpriteRenderer>();
        if (playerSr != null)
            fillRenderer.sortingLayerID = playerSr.sortingLayerID;
        fillRenderer.sprite = GetOrCreateFillSprite();
        fillRenderer.color = fillNormal;
    }

    static Sprite GetOrCreateFillSprite()
    {
        if (cachedFillSprite != null) return cachedFillSprite;

        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        var pixels = new Color32[size * size];
        float r = (size - 2) * 0.5f;
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        float r2 = r * r;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                bool inside = (dx * dx + dy * dy) <= r2;
                pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        cachedFillSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        cachedFillSprite.name = "AttackRadiusFillSprite";
        return cachedFillSprite;
    }

    void UpdateRadiusRenderer()
    {
        if (radiusRenderer == null) return;

        float r = Mathf.Max(0.01f, attackRadius);
        Vector3 center = transform.position;
        for (int i = 0; i < CircleSegments; i++)
        {
            float a = (i / (float)CircleSegments) * Mathf.PI * 2f;
            radiusRenderer.SetPosition(i, center + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
        }
    }

    void UpdateFillRenderer()
    {
        if (fillRenderer == null) return;

        // Scale sprite so its diameter matches attackRadius*2.
        float d = Mathf.Max(0.02f, attackRadius * 2f);
        fillRenderer.transform.position = transform.position;
        float spriteDiameter = fillRenderer.sprite != null ? fillRenderer.sprite.bounds.size.x : 1f;
        float s = spriteDiameter > 0.0001f ? (d / spriteDiameter) : d;
        fillRenderer.transform.localScale = new Vector3(s, s, 1f);

        fillRenderer.color = Time.time <= pressedUntil ? fillPressed : fillNormal;
    }
}

