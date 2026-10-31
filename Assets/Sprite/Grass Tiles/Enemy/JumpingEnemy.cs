using System.Collections;
using UnityEngine;
using Pathfinding;

[DisallowMultipleComponent]
public class JumpingEnemy : MonoBehaviour
{
    [Header("Color Tone")]
    [Tooltip("How often to change the sprite tint color.")]
    public float colorChangeInterval = 0.1f;
    [Tooltip("Saturation for the tint (0..1).")]
    [Range(0f, 1f)] public float saturation = 0.6f;
    [Tooltip("Value/brightness for the tint (0..1).")]
    [Range(0f, 1f)] public float value = 1.0f;

    [Header("Jump While Moving")]
    [Tooltip("Moves the enemy by hop-steps toward the Player (smooth transport).")]
    public bool driveMovementByHops = true; // kept for backward compatibility; jumping is always active.
    [Tooltip("Distance moved per teleport step (world units).")]
    public float teleportDistance = 0.8f;
    [Tooltip("Seconds between teleports (teleport frequency).")]
    public float teleportInterval = 0.35f;
    [Tooltip("How long the 'teleport' takes visually (seconds). Smaller = more teleport-like.")]
    public float teleportMoveDuration = 0.08f;
    [Tooltip("If true, disables EnemyAI on this object to avoid fighting over movement.")]
    public bool disableEnemyAIWhileHopping = true;

    [Header("Hop Visual")]
    [Tooltip("Enable squash/stretch during hop.")]
    public bool hopSquashStretch = true;
    [Tooltip("How strong the hop squash/stretch is (scale multiplier).")]
    public float hopStrength = 0.12f;
    [Tooltip("Minimum distance to Player before allowing a dash (added to EnemyAI.stopDistance).")]
    public float dashBufferBeyondStopDistance = 0.15f;

    [Header("Teleport Direction")]
    [Tooltip("How much to reduce vertical movement during teleports. 0 = no vertical, 1 = normal.")]
    [Range(0f, 1f)]
    public float verticalFactor = 0.25f;

    SpriteRenderer sr;
    Vector3 baseScale;
    Transform player;
    float hopT;
    bool hopping;
    Vector3 hopStart;
    Vector3 hopEnd;
    float hopProgress;
    float nextHopTime;
    Vector3 dashPrevPos;
    bool dashing;
    Vector3 pendingDashDelta;
    bool hasPendingDash;
    Coroutine colorRoutine;
    float nextPlayerSearchTime;
    Animator animator;
    static readonly int JumpTriggerHash = Animator.StringToHash("Jump");
    Rigidbody2D rb2D;
    AILerp aiLerp;
    EnemyAI enemyAI;
    bool hasJumpParam;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        baseScale = transform.localScale;
        animator = GetComponent<Animator>();
        rb2D = GetComponent<Rigidbody2D>();
        aiLerp = GetComponent<AILerp>();
        enemyAI = GetComponent<EnemyAI>();
        if (rb2D != null)
        {
            // Do not modify constraints here; EnemyAI/AILerp may be moving the Transform.
        }
        // Next time we're allowed to start a dash. When teleportInterval is 0, dashes chain with no pause.
        nextHopTime = Time.time;
        dashPrevPos = transform.position;
        dashing = false;
        pendingDashDelta = Vector3.zero;
        hasPendingDash = false;

        var playerGo = GameObject.FindWithTag("Player");
        player = playerGo != null ? playerGo.transform : null;
        nextPlayerSearchTime = Time.time + 0.25f;

        // IMPORTANT: We DO NOT disable EnemyAI anymore.
        // This script is intended to add teleport-dashes ON TOP of normal pathfinding movement.

        if (animator != null)
        {
            hasJumpParam = HasAnimatorParam(animator, "Jump");
        }
    }

    static bool HasAnimatorParam(Animator a, string name)
    {
        if (a == null) return false;
        var ps = a.parameters;
        for (int i = 0; i < ps.Length; i++)
            if (ps[i].name == name) return true;
        return false;
    }

    void OnEnable()
    {
        if (colorRoutine == null)
            colorRoutine = StartCoroutine(ColorToneLoop());
    }

    void OnDisable()
    {
        if (colorRoutine != null)
        {
            StopCoroutine(colorRoutine);
            colorRoutine = null;
        }

        // Restore original scale when disabled
        transform.localScale = baseScale;
    }

    void Update()
    {
        if (player == null)
        {
            // Player might not exist yet when this component Awake() runs (spawners, scene load order).
            if (Time.time >= nextPlayerSearchTime)
            {
                var playerGo = GameObject.FindWithTag("Player");
                player = playerGo != null ? playerGo.transform : null;
                nextPlayerSearchTime = Time.time + 0.25f;
            }
            if (player == null) return;
        }

        Vector3 pos = transform.position;
        Vector3 target = new Vector3(player.position.x, player.position.y, pos.z);
        Vector3 toTarget = target - pos;
        float dist = toTarget.magnitude;

        // Make base movement/attack EXACTLY like EnemyAI (torch):
        // - do not set InAttackRange here
        // - do not flip sprite here
        // - do not stop/override movement here

        float stop = enemyAI != null ? enemyAI.stopDistance : 0.7f;
        float minDashDist = stop + Mathf.Max(0f, dashBufferBeyondStopDistance);
        if (dist <= minDashDist)
        {
            // Too close: let EnemyAI handle stopping/attacking. We only reset hop visuals.
            hopping = false;
            hopProgress = 0f;
            transform.localScale = baseScale;
            dashing = false;
            pendingDashDelta = Vector3.zero;
            hasPendingDash = false;
            return;
        }

        // Start a new hop if not currently hopping and the post-dash pause has elapsed.
        if (!hopping && Time.time >= nextHopTime)
        {
            hopping = true;
            hopProgress = 0f;
            hopStart = pos;
            Vector3 dir = toTarget;
            // Bias teleports to be more horizontal by reducing vertical component.
            dir.y *= verticalFactor;
            dir.z = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.right;
            float step = Mathf.Min(teleportDistance, dist - stop);
            hopEnd = hopStart + dir * step;
            dashPrevPos = hopStart;
            dashing = true;

            // Play a dedicated jump animation trigger (separate from Attack).
            if (animator != null && hasJumpParam)
                animator.SetTrigger(JumpTriggerHash);
        }

        // Smooth, fast movement (looks like teleport) from hopStart to hopEnd.
        float moveDur = Mathf.Max(0.01f, teleportMoveDuration);
        if (hopping)
        {
            hopProgress += Time.deltaTime / moveDur;
            float t = Mathf.Clamp01(hopProgress);
            float eased = t * t * (3f - 2f * t); // SmoothStep

            // Compute additive dash displacement for this frame.
            Vector3 dashPos = Vector3.LerpUnclamped(hopStart, hopEnd, eased);
            Vector3 dashDelta = dashPos - dashPrevPos;
            dashPrevPos = dashPos;

            // Apply dash AFTER A* moves this frame (LateUpdate), so we don't erase walking.
            pendingDashDelta += dashDelta;
            hasPendingDash = true;

            // Visual hop: squash/stretch synced to the move.
            if (hopSquashStretch)
            {
                float s = Mathf.Sin(t * Mathf.PI); // 0..1..0
                float stretch = 1f + hopStrength * s;
                transform.localScale = new Vector3(baseScale.x * (1f - hopStrength * 0.3f * s), baseScale.y * stretch, baseScale.z);
            }
            else
            {
                transform.localScale = baseScale;
            }

            if (t >= 1f)
            {
                hopping = false;
                hopProgress = 0f;
                transform.localScale = baseScale;
                dashing = false;

                // Schedule the next dash AFTER this one finishes.
                // Set teleportInterval to 0 for continuous smooth-teleporting.
                nextHopTime = Time.time + Mathf.Max(0f, teleportInterval);
            }
        }
    }

    // (No LateUpdate needed now; dash is applied additively during Update.)

    void LateUpdate()
    {
        if (!hasPendingDash) return;
        if (pendingDashDelta == Vector3.zero) { hasPendingDash = false; return; }

        // LateUpdate runs after AILerp's Update movement, so this dash stacks on top of walking.
        if (rb2D != null) rb2D.position += (Vector2)pendingDashDelta;
        else transform.position += pendingDashDelta;

        pendingDashDelta = Vector3.zero;
        hasPendingDash = false;
    }

    IEnumerator ColorToneLoop()
    {
        var wait = new WaitForSeconds(colorChangeInterval);
        while (true)
        {
            if (sr != null)
            {
                float h = Random.value;
                sr.color = Color.HSVToRGB(h, saturation, value);
            }
            yield return wait;
        }
    }
}

