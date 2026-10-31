using UnityEngine;
using Pathfinding;

/// <summary>
/// Enemy AI that uses A* Pathfinding Project to chase the player.
/// Uses AILerp for 2D so the enemy follows path waypoints directly (avoids movement-plane issues).
/// </summary>
[RequireComponent(typeof(Seeker))]
public class EnemyAI : MonoBehaviour
{
    static readonly System.Collections.Generic.List<EnemyAI> AllEnemies = new System.Collections.Generic.List<EnemyAI>();

    [Header("Movement")]
    public float speed = 3f;
    [Tooltip("Runtime multiplier applied to speed (used by buff/debuff scripts).")]
    public float speedMultiplier = 1f;
    [Tooltip("Global multiplier applied to speed (e.g., time-based difficulty ramp).")]
    public float globalSpeedMultiplier = 1f;
    public float stopDistance = 0.7f;
    [Tooltip("How often to recalculate path (seconds)")]
    public float repathRate = 0.5f;
    [Tooltip("If false, the enemy will not rotate to face movement direction.")]
    public bool rotateTowardMovement = false;

    [Header("Attack (prep)")]
    public bool enableAttackPrep = true;
    [Tooltip("Distance at which the enemy should stop and attack.")]
    public float attackRange = 1.0f;
    [Tooltip("Seconds between attack triggers (animation/cooldown prep).")]
    public float attackCooldown = 1.0f;
    [Tooltip("If true, trigger attack immediately when entering attack range.")]
    public bool immediateAttackOnEnter = true;
    [Tooltip("Animator trigger name to fire when attacking.")]
    public string attackTrigger = "Attack";
    [Tooltip("Animator state name for attack (used to bypass transition delay on enter).")]
    public string attackStateName = "Attack";
    [Tooltip("Optional animator bool to set while in attack range.")]
    public string inRangeBool = "InAttackRange";
    [Tooltip("If true, calls EnemyCombat.DealDamage() when the attack is triggered (works even without Animation Events).")]
    public bool dealDamageOnAttackTrigger = true;
    [Tooltip("If true, directly starts the attack state when entering range to remove transition lag.")]
    public bool forceImmediateAttackStateOnEnter = true;

    [Header("Avoidance")]
    [Tooltip("Prevents multiple enemies from stacking on top of each other.")]
    public bool enableSeparation = true;
    [Tooltip("How far to look for other enemies.")]
    public float separationRadius = 0.6f;
    [Tooltip("Hard minimum distance to maintain between enemies. If an enemy gets closer than this, it will be pushed away.")]
    public float minEnemyDistance = 0.55f;
    [Tooltip("How strongly to push away from nearby enemies.")]
    public float separationStrength = 6f;
    [Tooltip("Maximum push speed applied by separation (units/sec).")]
    public float maxSeparationSpeed = 2.5f;

    [Header("Visual")]
    [Tooltip("Flip sprite when moving left/right.")]
    public bool flipSpriteByMovement = true;
    [Tooltip("If your sprite faces left by default, enable this.")]
    public bool spriteFacesLeftByDefault = false;

    [Header("A* Safety")]
    [Tooltip("If the enemy/player are off the scanned graph, snap chase positions to nearest node.")]
    public bool snapChaseToNearestNode = true;
    [Tooltip("If nearest node is farther than this, we consider the object off-graph.")]
    public float offGraphDistance = 1.0f;
    [Tooltip("If enemy transform is off-graph, move it back to nearest walkable node.")]
    public bool snapTransformBackToGraph = true;

    [Header("Route Variation")]
    [Tooltip("If enabled, each enemy will approach the player using a slightly different offset target, making paths diverge.")]
    public bool enableRouteVariation = true;
    [Tooltip("How far (left/right) to offset the approach target (world units).")]
    public float routeLateralOffset = 2.0f;
    [Tooltip("Random +/- variation applied to routeLateralOffset.")]
    public float routeLateralJitter = 1.0f;
    [Tooltip("How often to repick a new approach offset (seconds).")]
    public float routeRepickSeconds = 2.0f;
    [Tooltip("When closer than this distance to the player, stop using the offset so enemies actually connect and attack.")]
    public float routeDisableWithinDistance = 2.2f;

    private AILerp aiLerp;
    private AIDestinationSetter destinationSetter;
    private Transform player;
    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb2D;
    private Animator animator;
    private EnemyCombat combat;
    private float nextAttackTime;
    private bool wasInAttackRangeLastFrame;
    private bool loggedOffGraphWarning;
    private Vector3 separationSteerOffset;
    private Vector3 separationLateDelta;
    private bool hasInRangeParam;
    private bool hasAttackTriggerParam;
    private int attackStateHash;
    private float nextOffGraphLogTime;

    // Per-enemy route variation state
    int routeSideSign; // -1 or +1
    float nextRoutePickTime;
    Vector2 routeOffsetWorld2D;

    void OnEnable() {
        if (!AllEnemies.Contains(this)) AllEnemies.Add(this);
    }

    void OnDisable() {
        AllEnemies.Remove(this);
        // If this script gets disabled while the animator is in an "in range" state,
        // make sure we don't leave the bool stuck true.
        if (animator != null && !string.IsNullOrWhiteSpace(inRangeBool))
            animator.SetBool(inRangeBool, false);
    }

    void Start()
    {
        // Use AILerp for 2D: it follows the path waypoints directly (no movement-plane math that can cause "only goes up").
        aiLerp = GetComponent<AILerp>();
        if (aiLerp == null)
            aiLerp = gameObject.AddComponent<AILerp>();

        aiLerp.orientation = OrientationMode.YAxisForward;
        aiLerp.speed = speed;
        aiLerp.autoRepath.interval = repathRate;
        aiLerp.canMove = true;
        aiLerp.canSearch = true;
        aiLerp.enableRotation = rotateTowardMovement;

        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
        combat = GetComponent<EnemyCombat>();

        // Ensure animator starts in a safe state.
        if (animator != null)
        {
            hasInRangeParam = !string.IsNullOrWhiteSpace(inRangeBool) && HasAnimatorParam(animator, inRangeBool);
            hasAttackTriggerParam = !string.IsNullOrWhiteSpace(attackTrigger) && HasAnimatorParam(animator, attackTrigger);
            attackStateHash = Animator.StringToHash(attackStateName);
            if (hasInRangeParam) animator.SetBool(inRangeBool, false);
        }

        // So AILerp moves the Transform, not Rigidbody2D (avoids any constraint/axis issues).
        rb2D = GetComponent<Rigidbody2D>();
        if (rb2D != null)
        {
            rb2D.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb2D.bodyType = RigidbodyType2D.Kinematic;
        }

        destinationSetter = GetComponent<AIDestinationSetter>();
        if (destinationSetter == null)
            destinationSetter = gameObject.AddComponent<AIDestinationSetter>();

        player = GameObject.FindWithTag("Player")?.transform;
        if (player != null)
            destinationSetter.target = player;
        else
            Debug.LogWarning("EnemyAI: No GameObject with tag 'Player' found. Set AIDestinationSetter.target manually.");

        // Route variation: choose an initial side and offset so enemies don't all take the same line.
        routeSideSign = Random.value < 0.5f ? -1 : 1;
        PickNewRouteOffset(force: true);

        // Ensure animation events can call DealDamage on this object.
        if (GetComponent<EnemyDamageEventReceiver>() == null)
            gameObject.AddComponent<EnemyDamageEventReceiver>();

        // Ensure every enemy can be killed by the player's attack (3 hits).
        if (GetComponent<EnemyHitCounter>() == null)
            gameObject.AddComponent<EnemyHitCounter>();
    }

    void Update()
    {
        // Lazy init for runtime-spawned clones (Start() timing/order can be tricky).
        if (aiLerp == null)
        {
            aiLerp = GetComponent<AILerp>();
            if (aiLerp == null) aiLerp = gameObject.AddComponent<AILerp>();

            aiLerp.orientation = OrientationMode.YAxisForward;
            aiLerp.autoRepath.interval = repathRate;
            aiLerp.canMove = true;
            aiLerp.canSearch = true;
            aiLerp.enableRotation = rotateTowardMovement;
        }

        if (destinationSetter == null)
        {
            destinationSetter = GetComponent<AIDestinationSetter>();
            if (destinationSetter == null) destinationSetter = gameObject.AddComponent<AIDestinationSetter>();
        }

        // Ensure Rigidbody2D (if present) can't get pinned by physics collisions.
        if (rb2D == null) rb2D = GetComponent<Rigidbody2D>();
        if (rb2D != null && rb2D.bodyType != RigidbodyType2D.Kinematic)
        {
            rb2D.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb2D.bodyType = RigidbodyType2D.Kinematic;
        }

        // Reset per-frame avoidance outputs.
        separationSteerOffset = Vector3.zero;
        separationLateDelta = Vector3.zero;

        // Keep AILerp speed in sync with runtime changes.
        aiLerp.speed = speed * Mathf.Max(0f, speedMultiplier) * Mathf.Max(0f, globalSpeedMultiplier);

        if (player == null)
            player = destinationSetter != null ? destinationSetter.target : null;

        if (player == null) return;

        // Ensure destination setter always has target (runtime clones sometimes lose it).
        if (destinationSetter != null && destinationSetter.target == null)
            destinationSetter.target = player;

        // Hard guard: never pathfind from a non-walkable/off-graph start.
        if (AstarPath.active != null)
        {
            var walkableConstraint = NNConstraint.Default;
            walkableConstraint.constrainWalkability = true;
            walkableConstraint.walkable = true;

            var selfNN = AstarPath.active.GetNearest(transform.position, walkableConstraint);
            if (selfNN.node == null)
            {
                // No valid start node exists nearby. Pause this frame to avoid path fail spam.
                aiLerp.canSearch = false;
                aiLerp.isStopped = true;
                if (Time.unscaledTime >= nextOffGraphLogTime)
                {
                    Debug.LogWarning("EnemyAI: No nearby walkable node for enemy position. Check graph coverage/walkability.", this);
                    nextOffGraphLogTime = Time.unscaledTime + 1.0f;
                }
                return;
            }
            else
            {
                aiLerp.canSearch = true;

                float d = Vector3.Distance(transform.position, (Vector3)selfNN.position);
                if (snapTransformBackToGraph && d > offGraphDistance)
                {
                    Vector3 p = (Vector3)selfNN.position;
                    transform.position = new Vector3(p.x, p.y, transform.position.z);
                    aiLerp.SearchPath();
                }
            }
        }

        Vector3 flatEnemy = new Vector3(transform.position.x, transform.position.y, transform.position.z);
        Vector3 flatPlayerActual = new Vector3(player.position.x, player.position.y, transform.position.z);

        // Compute a per-enemy chase target around the player to diversify routes.
        Vector3 flatPlayerChase = flatPlayerActual;
        if (enableRouteVariation)
        {
            float actualDist = Vector3.Distance(flatEnemy, flatPlayerActual);
            if (actualDist > Mathf.Max(0.01f, routeDisableWithinDistance))
            {
                PickNewRouteOffset(force: false, enemyPos: flatEnemy, playerPos: flatPlayerActual);
                flatPlayerChase = new Vector3(
                    flatPlayerActual.x + routeOffsetWorld2D.x,
                    flatPlayerActual.y + routeOffsetWorld2D.y,
                    flatPlayerActual.z
                );
            }
        }

        if (snapChaseToNearestNode && AstarPath.active != null)
        {
            var walkableConstraint = NNConstraint.Default;
            walkableConstraint.constrainWalkability = true;
            walkableConstraint.walkable = true;
            var startNN = AstarPath.active.GetNearest(flatEnemy, walkableConstraint);
            var endNN = AstarPath.active.GetNearest(flatPlayerChase, walkableConstraint);

            bool startValid = startNN.node != null && startNN.node.Walkable;
            bool endValid = endNN.node != null && endNN.node.Walkable;

            float startDist = startValid ? Vector3.Distance(flatEnemy, (Vector3)startNN.position) : float.PositiveInfinity;
            float endDist = endValid ? Vector3.Distance(flatPlayerChase, (Vector3)endNN.position) : float.PositiveInfinity;

            // Critical fix: if transform itself is off-graph, path start will fail repeatedly.
            // Move the enemy back onto a valid node and request a fresh path.
            if (snapTransformBackToGraph && startValid && startDist > offGraphDistance)
            {
                Vector3 nodePos = (Vector3)startNN.position;
                transform.position = new Vector3(nodePos.x, nodePos.y, transform.position.z);
                flatEnemy = transform.position;
                if (aiLerp != null) aiLerp.SearchPath();
            }

            if (startValid && startDist > offGraphDistance) flatEnemy = (Vector3)startNN.position;
            if (endValid && endDist > offGraphDistance) flatPlayerChase = (Vector3)endNN.position;

            if (!loggedOffGraphWarning && ((startValid && startDist > offGraphDistance) || (endValid && endDist > offGraphDistance) || !startValid || !endValid))
            {
                Debug.LogWarning(
                    $"EnemyAI: Off-graph chase detected (startValid={startValid}, startDist={startDist:0.00}, endValid={endValid}, endDist={endDist:0.00}). " +
                    "Snapping chase positions to nearest nodes. If this persists, expand/align your scanned graph to cover the scene and ensure the nodes are walkable."
                );
                loggedOffGraphWarning = true;
            }
        }
        // IMPORTANT:
        // - Use REAL player position for attack decisions (so enemies actually hit you)
        // - Use CHASE position (offset) for movement/pathfinding variety
        Vector3 toPlayerActual = flatPlayerActual - flatEnemy;
        float distActual = toPlayerActual.magnitude;

        Vector3 toPlayerChase = flatPlayerChase - flatEnemy;
        float distChase = toPlayerChase.magnitude;

        bool inAttackRangeNow = enableAttackPrep && distActual <= attackRange;
        if (animator != null && hasInRangeParam)
            animator.SetBool(inRangeBool, inAttackRangeNow);

        if (inAttackRangeNow)
        {
            // Stop movement while preparing to attack
            aiLerp.destination = flatEnemy;
            aiLerp.isStopped = true;

            // Trigger attack animation on a cooldown and apply damage in code (not AnimationEvents).
            // Damage is applied ONLY by Animation Event -> EnemyDamageEventReceiver.DealDamage().
            bool justEnteredAttackRange = !wasInAttackRangeLastFrame;
            if (immediateAttackOnEnter && justEnteredAttackRange && Time.time < nextAttackTime)
                nextAttackTime = Time.time;

            if (Time.time >= nextAttackTime)
            {
                if (justEnteredAttackRange && forceImmediateAttackStateOnEnter && animator != null)
                {
                    if (animator.HasState(0, attackStateHash))
                        animator.Play(attackStateHash, 0, 0f);
                }

                if (animator != null && hasAttackTriggerParam)
                    animator.SetTrigger(attackTrigger);
                nextAttackTime = Time.time + attackCooldown;
            }
        }
        else
        {
            aiLerp.isStopped = false;
        }

        if (!inAttackRangeNow)
        {
            if (distActual <= stopDistance || distChase <= 0.001f)
                aiLerp.destination = flatEnemy;
            else
                aiLerp.destination = flatEnemy + toPlayerChase.normalized * (distChase - stopDistance);

            // Let avoidance influence where the pathfinder steers, so it doesn't immediately undo separation.
            if (enableSeparation && separationSteerOffset != Vector3.zero)
                aiLerp.destination += separationSteerOffset;
        }

        if (enableSeparation && separationRadius > 0.001f)
        {
            Vector2 pos2D = transform.position;
            Vector2 pushDir = Vector2.zero;
            // NOTE: We intentionally avoid "hard push" position correction here.
            // Hard position pushes can fight pathfinding around obstacles and make agents look stuck.

            for (int i = 0; i < AllEnemies.Count; i++)
            {
                var otherEnemy = AllEnemies[i];
                if (otherEnemy == null || otherEnemy == this || !otherEnemy.isActiveAndEnabled) continue;

                Vector2 otherPos = otherEnemy.transform.position;
                Vector2 away = pos2D - otherPos;
                float d = away.magnitude;
                if (d < 0.0001f) continue;
                if (d > separationRadius) continue;

                // Soft repulsion inside separationRadius
                float w = 1f - (d / separationRadius); // 0..1
                pushDir += (away / d) * w;

                // Hard correction if they get closer than minEnemyDistance
                // (disabled)
            }

            if (pushDir != Vector2.zero)
            {
                Vector2 dir = pushDir != Vector2.zero ? pushDir.normalized : (Vector2)Random.insideUnitCircle.normalized;

                // Steering offset (soft): biases the A* destination away from crowds.
                // This reduces "separation breaking" because the pathfinder cooperates.
                float steerScale = Mathf.Clamp01(pushDir.magnitude);
                separationSteerOffset = (Vector3)(dir * (separationRadius * 0.5f * steerScale));
            }
        }

        if (flipSpriteByMovement && spriteRenderer != null)
        {
            float vx = aiLerp.velocity.x;
            if (Mathf.Abs(vx) > 0.001f)
            {
                bool movingLeft = vx < 0f;
                spriteRenderer.flipX = spriteFacesLeftByDefault ? !movingLeft : movingLeft;
            }
        }

        wasInAttackRangeLastFrame = inAttackRangeNow;
    }

    void LateUpdate()
    {
        // Intentionally empty: separation is steering-only to avoid fighting pathfinding.
    }

    static bool HasAnimatorParam(Animator a, string name)
    {
        if (a == null || string.IsNullOrEmpty(name)) return false;
        var ps = a.parameters;
        for (int i = 0; i < ps.Length; i++)
            if (ps[i].name == name) return true;
        return false;
    }

    /// <summary>Call from other scripts to change chase target at runtime.</summary>
    public void SetTarget(Transform target)
    {
        if (destinationSetter != null)
            destinationSetter.target = target;
        player = target;
    }

    /// <summary>Whether the enemy has reached its current destination.</summary>
    public bool ReachedDestination => aiLerp != null && aiLerp.reachedDestination;

    void PickNewRouteOffset(bool force, Vector3 enemyPos = default, Vector3 playerPos = default)
    {
        if (!enableRouteVariation) { routeOffsetWorld2D = Vector2.zero; return; }

        float repick = Mathf.Max(0.1f, routeRepickSeconds);
        if (!force && Time.unscaledTime < nextRoutePickTime) return;
        nextRoutePickTime = Time.unscaledTime + repick;

        // If not provided, derive positions.
        if (enemyPos == default) enemyPos = transform.position;
        if (playerPos == default && player != null) playerPos = player.position;

        Vector2 dir = ((Vector2)(playerPos - enemyPos));
        if (dir.sqrMagnitude < 0.0001f) dir = Random.insideUnitCircle;
        dir.Normalize();

        // Choose a consistent side per enemy, but occasionally flip for more variety.
        if (!force && Random.value < 0.15f) routeSideSign = -routeSideSign;

        Vector2 perp = new Vector2(-dir.y, dir.x) * routeSideSign;

        float baseLat = Mathf.Max(0f, routeLateralOffset);
        float jitter = Mathf.Clamp(routeLateralJitter, 0f, 100f);
        float lat = Mathf.Max(0f, baseLat + Random.Range(-jitter, jitter));

        // Small forward/back jitter so they don't form perfect “lanes”.
        float forward = Random.Range(-0.6f, 0.6f);

        routeOffsetWorld2D = perp * lat + dir * forward;
    }
}
