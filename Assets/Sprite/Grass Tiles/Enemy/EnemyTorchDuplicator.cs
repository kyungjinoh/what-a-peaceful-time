using System.Collections;
using UnityEngine;
using Pathfinding;
using UnityEngine.SceneManagement;
using System.Reflection;

[DisallowMultipleComponent]
public class EnemyTorchDuplicator : MonoBehaviour
{
    const string EnemyContainerName = "Enemy";
    const string EnemyUnitName = "Enemy_torch"; // legacy expected name (case-insensitive matching is used)
    [Tooltip("Prefab asset to duplicate repeatedly (recommended).")]
    public GameObject enemyTorchPrefab;

    [Tooltip("If prefab is null, this scene object will be duplicated instead.")]
    public GameObject enemyTorchSceneObject;

    [Tooltip("Seconds between duplicates.")]
    public float duplicateEverySeconds = 5f;

    [Header("Adaptive Difficulty")]
    [Tooltip("Fastest spawn interval when player is killing quickly.")]
    public float minDuplicateSeconds = 1.2f;
    [Tooltip("Seconds of kill history used to judge how 'good' the player is.")]
    public float killWindowSeconds = 8f;
    [Tooltip("How many kills within the window should reach the minimum spawn interval.")]
    public int killsForMaxPressure = 6;

    [Tooltip("Where to spawn. If null, spawns at this object's position.")]
    public Transform spawnPoint;
    [Tooltip("If true, uses typed X/Y coordinates instead of Spawn Point transform.")]
    public bool useSpawnCoordinates = true;
    [Tooltip("Spawn center X when Use Spawn Coordinates is enabled.")]
    public float spawnX = 0f;
    [Tooltip("Spawn center Y when Use Spawn Coordinates is enabled.")]
    public float spawnY = 0f;

    [Header("Population")]
    [Tooltip("Maximum number of alive monsters allowed.")]
    public int maxAlive = 20;
    [Tooltip("Seconds after a monster dies before respawning (slot refill).")]
    public float respawnDelaySeconds = 0.25f;
    [Tooltip("Do not spawn enemies closer than this distance to the player.")]
    public float minSpawnDistanceFromPlayer = 4f;

    [Header("Time Difficulty")]
    [Tooltip("Linear speed growth per second. 0.025 means x2.5 at 60 seconds.")]
    public float globalSpeedRampPerSecond = 0.025f;
    [Tooltip("Safety cap for global speed multiplier.")]
    public float maxGlobalSpeedMultiplier = 12f;

    [Header("Special Variants")]
    [Tooltip("If true, the 8th alive enemy gets teleport ability. Turn off to keep all clones identical to original.")]
    public bool enableEighthTeleport = false;
    [Tooltip("From clone #3 onward, sudden speed burst multiplier.")]
    public float cloneBurstMultiplier = 3f;
    [Tooltip("From clone #3 onward, sudden speed burst duration.")]
    public float cloneBurstDurationSeconds = 0.5f;
    [Tooltip("Base enemy damage magnitude (stored as negative in EnemyCombat).")]
    public int baseDamage = 5;
    [Tooltip("From spawn #10 onward, big enemy scale multiplier.")]
    public float bigEnemyScaleMultiplier = 2f;
    [Tooltip("Big enemy damage multiplier.")]
    public float bigEnemyDamageMultiplier = 2f;
    [Tooltip("Maximum number of big enemies alive at once.")]
    public int maxBigEnemiesAlive = 5;
    [Tooltip("From spawn #15 onward, teleport enemies every N seconds.")]
    public float teleportCooldownSeconds = 10f;

    [Header("Spawn Slots (A* safe)")]
    [Tooltip("How many walkable spawn slots to precompute (should match maxAlive).")]
    public int spawnSlots = 20;
    [Tooltip("Extra random samples to find walkable spawn slots.")]
    public int spawnSlotSearchAttempts = 80;

    int spawnCount;
    GameObject template;
    static EnemyTorchDuplicator instance;
    float currentInterval;
    readonly System.Collections.Generic.Queue<float> killTimes = new System.Collections.Generic.Queue<float>();
    float nextAllowedSpawnTime;
    readonly System.Collections.Generic.List<Vector3> cachedSpawnSlots = new System.Collections.Generic.List<Vector3>();
    readonly System.Collections.Generic.Dictionary<int, int> enemyIdToSlot = new System.Collections.Generic.Dictionary<int, int>();

    Vector3 GetSpawnCenter()
    {
        if (useSpawnCoordinates) return new Vector3(spawnX, spawnY, 0f);
        return spawnPoint != null ? spawnPoint.position : transform.position;
    }

    void Awake()
    {
        // Keep exactly one duplicator alive even if all enemies die.
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        // User-requested baseline.
        maxAlive = Mathf.Max(20, maxAlive);
        spawnSlots = Mathf.Max(spawnSlots, maxAlive);
        currentInterval = Mathf.Max(0.01f, duplicateEverySeconds);
        // First clone appears after the duplicate interval (e.g. 5 seconds).
        nextAllowedSpawnTime = Time.time + Mathf.Max(0.01f, duplicateEverySeconds);

        // If this duplicator is accidentally placed on an enemy that can die,
        // move duplication onto a separate runtime object so spawning continues.
        if (GetComponent<EnemyAI>() != null || GetComponent<EnemyCombat>() != null)
        {
            var host = new GameObject("EnemyTorchDuplicatorRuntime");
            host.transform.position = transform.position;
            DontDestroyOnLoad(host);

            var dup = host.AddComponent<EnemyTorchDuplicator>();
            dup.enemyTorchPrefab = enemyTorchPrefab;
            dup.enemyTorchSceneObject = enemyTorchSceneObject;
            dup.duplicateEverySeconds = duplicateEverySeconds;
            dup.spawnPoint = spawnPoint != null ? spawnPoint : transform;
            dup.useSpawnCoordinates = useSpawnCoordinates;
            dup.spawnX = spawnX;
            dup.spawnY = spawnY;

            Destroy(this);
            return;
        }

        // Cache a template so duplication still works if the original scene object gets destroyed.
        NormalizeEnemyHierarchy();
        EnsureTemplate();

        BuildSpawnSlots();
    }

    public static void ResetForRestart()
    {
        // Destroy persistent runtime objects so a scene reload truly resets.
        var all = Resources.FindObjectsOfTypeAll<EnemyTorchDuplicator>();
        for (int i = 0; i < all.Length; i++)
        {
            var d = all[i];
            if (d == null) continue;
            if (d.template != null) Object.Destroy(d.template);
            Object.Destroy(d.gameObject);
        }
        instance = null;
    }

    void OnEnable()
    {
        EnemyHitCounter.OnKilled += HandleEnemyKilled;
    }

    void OnDisable()
    {
        EnemyHitCounter.OnKilled -= HandleEnemyKilled;
    }

    void Start()
    {
        StartCoroutine(Loop());
    }

    IEnumerator Loop()
    {
        while (true)
        {
            // Maintain population; never exceed maxAlive.
            yield return new WaitForSeconds(0.15f);
            MaintainPopulation();
        }
    }

    void HandleEnemyKilled(EnemyHitCounter killed)
    {
        float now = Time.time;
        killTimes.Enqueue(now);

        float window = Mathf.Max(0.5f, killWindowSeconds);
        while (killTimes.Count > 0 && now - killTimes.Peek() > window)
            killTimes.Dequeue();

        int k = killTimes.Count;
        int target = Mathf.Max(1, killsForMaxPressure);
        float t = Mathf.Clamp01(k / (float)target);

        float baseInterval = Mathf.Max(0.01f, duplicateEverySeconds);
        float minInterval = Mathf.Clamp(minDuplicateSeconds, 0.05f, baseInterval);

        // More kills => faster spawns.
        currentInterval = Mathf.Lerp(baseInterval, minInterval, t);

        // Allow respawn after a small delay, then use adaptive interval as additional spacing.
        nextAllowedSpawnTime = Time.time + Mathf.Max(0f, respawnDelaySeconds);

        // Re-evaluate which alive enemy is the special #8.
        if (enableEighthTeleport) AssignEighthTeleport();

        // Free slot if we tracked it.
        if (killed != null)
        {
            int id = killed.GetInstanceID();
            if (enemyIdToSlot.ContainsKey(id))
                enemyIdToSlot.Remove(id);
        }
    }

    void MaintainPopulation()
    {
        if (!GameStartReplayUI.IsRunning) return;

        // Keep scene hierarchy normalized: logic must live on Enemy/Enemy_torch, not Enemy container.
        NormalizeEnemyHierarchy();

        // Apply time-based speed ramp to all alive enemies.
        ApplyGlobalSpeedRamp();

        int alive = CountAliveEnemies();
        if (alive >= Mathf.Max(1, maxAlive))
        {
            if (enableEighthTeleport) AssignEighthTeleport();
            return;
        }

        if (Time.time < nextAllowedSpawnTime) return;
        // Spawn continuously by base duplicate interval, even if player doesn't kill enemies.
        nextAllowedSpawnTime = Time.time + Mathf.Max(0.01f, duplicateEverySeconds);

        DuplicateOnce();
        if (enableEighthTeleport) AssignEighthTeleport();
    }

    public void ApplySpecialSpawnBoost(float intervalMultiplier)
    {
        float m = Mathf.Clamp(intervalMultiplier, 0.1f, 1f);
        duplicateEverySeconds = Mathf.Max(0.15f, duplicateEverySeconds * m);
        minDuplicateSeconds = Mathf.Max(0.10f, minDuplicateSeconds * m);
        respawnDelaySeconds = Mathf.Max(0f, respawnDelaySeconds * m);
        currentInterval = Mathf.Max(0.01f, currentInterval * m);
        // Allow spawns sooner after special clears the screen.
        nextAllowedSpawnTime = Mathf.Min(nextAllowedSpawnTime, Time.time + 0.05f);
    }

    int CountAliveEnemies()
    {
        // Count active enemies in the scene (ignores inactive template).
        var all = Object.FindObjectsOfType<EnemyAI>();
        int count = 0;
        for (int i = 0; i < all.Length; i++)
        {
            var ai = all[i];
            if (ai == null) continue;
            if (!ai.isActiveAndEnabled) continue;
            count++;
        }
        return count;
    }

    void ApplyGlobalSpeedRamp()
    {
        float t = Time.unscaledTime; // unaffected by timeScale pauses
        float growth = Mathf.Max(0f, globalSpeedRampPerSecond);
        float mult = 1f + (growth * t);
        if (mult < 1f) mult = 1f;
        if (maxGlobalSpeedMultiplier > 1f) mult = Mathf.Min(mult, maxGlobalSpeedMultiplier);

        var all = Object.FindObjectsOfType<EnemyAI>();
        for (int i = 0; i < all.Length; i++)
        {
            var ai = all[i];
            if (ai == null) continue;
            if (!ai.isActiveAndEnabled) continue;
            ai.globalSpeedMultiplier = mult;
        }
    }

    void AssignEighthTeleport()
    {
        if (!enableEighthTeleport) return;
        // Exactly the 8th alive enemy gets the heading-teleport ability.
        var all = Object.FindObjectsOfType<EnemyAI>();
        // Stable ordering.
        System.Array.Sort(all, (a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

        int idx = 0;
        EnemyAI eighth = null;
        for (int i = 0; i < all.Length; i++)
        {
            var ai = all[i];
            if (ai == null || !ai.isActiveAndEnabled) continue;
            idx++;
            if (idx == 8) { eighth = ai; break; }
        }

        // Remove teleport from everyone else, add to 8th.
        for (int i = 0; i < all.Length; i++)
        {
            var ai = all[i];
            if (ai == null || !ai.isActiveAndEnabled) continue;
            if (ai == eighth)
            {
                var tp2 = ai.GetComponent<EnemyTeleportAheadOfPlayer>();
                if (tp2 == null) tp2 = ai.gameObject.AddComponent<EnemyTeleportAheadOfPlayer>();
                tp2.cooldownSeconds = 12f;
            }
            else
            {
                var tp2 = ai.GetComponent<EnemyTeleportAheadOfPlayer>();
                if (tp2 != null) Object.Destroy(tp2);
            }
        }
    }

    void DuplicateOnce()
    {
        EnsureTemplate();
        GameObject source = enemyTorchPrefab != null ? enemyTorchPrefab : (template != null ? template : enemyTorchSceneObject);
        if (source == null)
        {
            Debug.LogWarning("EnemyTorchDuplicator: No spawn source. Assign enemyTorchPrefab (preferred) or ensure an Enemy_Torch exists at startup.", this);
            return;
        }

        // Never spawn the container object by mistake.
        source = ExtractEnemyUnit(source);

        if (cachedSpawnSlots.Count == 0)
            BuildSpawnSlots();

        Vector3 pos = PickFreeSlotPosition();

        spawnCount++;
        var go = Instantiate(source, pos, Quaternion.identity);
        // If source/template lives in DontDestroyOnLoad scene, clones will also be created there.
        // Move clones into the active scene so they behave the same as the original enemy.
        SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
        // Keep hierarchy consistent: parent all enemies under the "Enemy" object.
        var enemyRoot = GameObject.Find(EnemyContainerName);
        if (enemyRoot != null)
            go.transform.SetParent(enemyRoot.transform, true);

        // Keep original unit name (do not force casing).
        go.name = source.name.Replace("_Template", "");
        // If the source is an inactive template, Instantiate will produce an inactive clone.
        // Ensure clones are active/visible.
        if (!go.activeSelf) go.SetActive(true);

        // Prevent physics from pinning the agent on obstacles (AILerp moves the Transform).
        var rb = go.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        // If we spawned from a template that was previously made inert, components may be disabled.
        // Ensure core movement/pathfinding components are enabled.
        var enemyAIEnabled = go.GetComponent<EnemyAI>();
        if (enemyAIEnabled != null) enemyAIEnabled.enabled = true;
        var seekerEnabled = go.GetComponent<Seeker>();
        if (seekerEnabled != null) seekerEnabled.enabled = true;
        var dstEnabled = go.GetComponent<AIDestinationSetter>();
        if (dstEnabled != null) dstEnabled.enabled = true;
        var aiLerpEnabled = go.GetComponent<AILerp>();
        if (aiLerpEnabled != null) aiLerpEnabled.enabled = true;

        // Debug aid: warn if spawned far from any walkable node (common reason clones "don't pathfind").
        if (AstarPath.active != null)
        {
            var c = NNConstraint.Default;
            c.constrainWalkability = true;
            c.walkable = true;
            var nn = AstarPath.active.GetNearest(go.transform.position, c);
            if (nn.node == null)
                Debug.LogWarning("Spawned clone has no nearby walkable node (pathfinding will fail).", go);
        }

        // Ensure enemies can be killed by player's Space attack.
        var hitCounter = go.GetComponent<EnemyHitCounter>();
        if (hitCounter == null) hitCounter = go.AddComponent<EnemyHitCounter>();

        // Ensure clones can deal damage.
        if (go.GetComponent<EnemyCombat>() == null)
            go.AddComponent<EnemyCombat>();
        if (go.GetComponent<EnemyDamageEventReceiver>() == null)
            go.AddComponent<EnemyDamageEventReceiver>();

        // If an enemy gets stuck on obstacles for a few seconds, transport near the player.
        if (go.GetComponent<EnemyStuckTransport>() == null)
            go.AddComponent<EnemyStuckTransport>();

        // Proactively ensure pathfinding components exist and are configured.
        // Relying on EnemyAI.Start() order can cause clones to spawn "not chasing".
        var playerT = GameObject.FindWithTag("Player")?.transform;
        if (go.GetComponent<Seeker>() == null) go.AddComponent<Seeker>();

        var dst = go.GetComponent<AIDestinationSetter>();
        if (dst == null) dst = go.AddComponent<AIDestinationSetter>();
        if (dst.target == null) dst.target = playerT;

        var aiLerp = go.GetComponent<AILerp>();
        if (aiLerp == null) aiLerp = go.AddComponent<AILerp>();
        aiLerp.canSearch = true;
        aiLerp.canMove = true;
        // Force an initial path calculation right after spawn.
        aiLerp.autoRepath.interval = 0.25f;
        aiLerp.SearchPath();

        // Also force EnemyAI's internal target (works once destinationSetter exists).
        var enemyAI = go.GetComponent<EnemyAI>();
        if (enemyAI != null) enemyAI.SetTarget(playerT);

        // Mirror settings from the original Enemy/Enemy_torch exactly.
        SyncCloneToOriginalUnit(go);

        // Final hard-assert: sync/migration can disable/remove components.
        // Rebuild pathfinding stack at the very end so clones always move.
        EnsurePathfindingReady(go, playerT);

        // Track which slot this enemy occupies so we can free it on death.
        if (hitCounter != null)
        {
            int id = hitCounter.GetInstanceID();
            int slot = FindNearestSlotIndex(go.transform.position);
            enemyIdToSlot[id] = slot;
        }

        // No per-clone stat mutations here: keep replicas identical to original.

        // From 3rd clone onward, add sudden speed boost bursts.
        if (spawnCount >= 3)
        {
            var pulse = go.GetComponent<EnemySpeedPulse>();
            if (pulse == null) pulse = go.AddComponent<EnemySpeedPulse>();
            pulse.burstMultiplier = Mathf.Max(1f, cloneBurstMultiplier);
            pulse.burstDurationSeconds = Mathf.Max(0.05f, cloneBurstDurationSeconds);
        }

        // Baseline damage for normal enemies.
        // IMPORTANT: Even if the scene instance has an overridden baseDamage in the Inspector,
        // we clamp the runtime baseline so clones don't deal huge damage.
        int dmgBase = Mathf.Clamp(Mathf.Abs(baseDamage), 1, 5);
        var cbt = go.GetComponent<EnemyCombat>();
        if (cbt != null)
            cbt.damage = -dmgBase;

        // From spawn #10 onward: big danger enemy (capped to maxBigEnemiesAlive).
        if (spawnCount >= 10 && CountAliveBigEnemies() < Mathf.Max(0, maxBigEnemiesAlive))
        {
            go.transform.localScale = go.transform.localScale * Mathf.Max(1f, bigEnemyScaleMultiplier);
            if (cbt != null)
                cbt.damage = -Mathf.RoundToInt(dmgBase * Mathf.Max(1f, bigEnemyDamageMultiplier));

            var danger = go.GetComponent<EnemyDangerTint>();
            if (danger == null) danger = go.AddComponent<EnemyDangerTint>();
            danger.intervalSeconds = 0.1f;
        }

        // From spawn #15 onward: teleport enemy (ahead of player's heading) every 10 seconds.
        if (spawnCount >= 15)
        {
            var tp = go.GetComponent<EnemyTeleportAheadOfPlayer>();
            if (tp == null) tp = go.AddComponent<EnemyTeleportAheadOfPlayer>();
            tp.cooldownSeconds = Mathf.Max(0.1f, teleportCooldownSeconds);
        }
    }

    int CountAliveBigEnemies()
    {
        var all = Object.FindObjectsOfType<EnemyDangerTint>();
        int count = 0;
        for (int i = 0; i < all.Length; i++)
        {
            var d = all[i];
            if (d == null || !d.isActiveAndEnabled) continue;
            count++;
        }
        return count;
    }

    void BuildSpawnSlots()
    {
        cachedSpawnSlots.Clear();
        Vector3 center = GetSpawnCenter();
        center.z = 0f;

        int want = Mathf.Clamp(spawnSlots, 1, Mathf.Max(1, maxAlive));
        int attempts = Mathf.Max(want * 5, spawnSlotSearchAttempts);

        if (AstarPath.active == null)
        {
            // No A*: just scatter points in boundary.
            for (int i = 0; i < want; i++)
            {
                Vector2 o = Random.insideUnitCircle * EnemyBoundaryClamp.BoundaryRadius;
                cachedSpawnSlots.Add(center + new Vector3(o.x, o.y, 0f));
            }
            return;
        }

        var constraint = NNConstraint.Default;
        constraint.constrainWalkability = true;
        constraint.walkable = true;

        // Always include the nearest node to center as first slot.
        var first = AstarPath.active.GetNearest(center, constraint);
        if (first.node != null)
            cachedSpawnSlots.Add((Vector3)first.position);

        for (int a = 0; a < attempts && cachedSpawnSlots.Count < want; a++)
        {
            Vector2 o = Random.insideUnitCircle * EnemyBoundaryClamp.BoundaryRadius;
            Vector3 candidate = center + new Vector3(o.x, o.y, 0f);
            var nn = AstarPath.active.GetNearest(candidate, constraint);
            if (nn.node == null) continue;

            Vector3 p = (Vector3)nn.position;

            // Keep slots separated so enemies don't spawn on top of each other.
            bool tooClose = false;
            for (int i = 0; i < cachedSpawnSlots.Count; i++)
            {
                if (Vector2.Distance(cachedSpawnSlots[i], p) < 1.2f) { tooClose = true; break; }
            }
            if (tooClose) continue;

            cachedSpawnSlots.Add(p);
        }

        // If we couldn't find any slot, fallback to a guaranteed-valid center near player/enemy.
        if (cachedSpawnSlots.Count == 0)
        {
            Vector3 fallbackCenter = ResolveFallbackGraphCenter(center);
            var nn = AstarPath.active.GetNearest(fallbackCenter, constraint);
            if (nn.node != null)
                cachedSpawnSlots.Add((Vector3)nn.position);
            else
                cachedSpawnSlots.Add(fallbackCenter);

            Debug.LogWarning("EnemyTorchDuplicator: Spawn center had no nearby walkable nodes. Using fallback graph center.", this);
        }

        // If we still couldn't find enough, duplicate existing slots.
        while (cachedSpawnSlots.Count < want && cachedSpawnSlots.Count > 0)
            cachedSpawnSlots.Add(cachedSpawnSlots[cachedSpawnSlots.Count - 1]);
    }

    Vector3 ResolveFallbackGraphCenter(Vector3 preferred)
    {
        // 1) Player position (usually guaranteed on graph in gameplay)
        var player = GameObject.FindWithTag("Player");
        if (player != null) return player.transform.position;

        // 2) Any alive enemy
        var ai = Object.FindFirstObjectByType<EnemyAI>();
        if (ai != null) return ai.transform.position;

        // 3) Original center
        return preferred;
    }

    Vector3 PickFreeSlotPosition()
    {
        if (cachedSpawnSlots.Count == 0)
        {
            Vector3 center = GetSpawnCenter();
            return center;
        }

        Transform player = GameObject.FindWithTag("Player")?.transform;

        bool[] used = new bool[cachedSpawnSlots.Count];
        foreach (var kv in enemyIdToSlot)
        {
            int slot = kv.Value;
            if (slot >= 0 && slot < used.Length) used[slot] = true;
        }

        for (int i = 0; i < used.Length; i++)
        {
            if (used[i]) continue;
            if (player != null)
            {
                Vector3 p = player.position;
                p.z = cachedSpawnSlots[i].z;
                if (Vector3.Distance(cachedSpawnSlots[i], p) < Mathf.Max(0f, minSpawnDistanceFromPlayer))
                    continue;
            }
            return cachedSpawnSlots[i];
        }

        // If all slots used, just return a random slot (should be rare due to maxAlive limit).
        // Prefer a far slot from player when possible.
        if (player != null)
        {
            int best = 0;
            float bestD = -1f;
            for (int i = 0; i < cachedSpawnSlots.Count; i++)
            {
                Vector3 p = player.position;
                p.z = cachedSpawnSlots[i].z;
                float d = Vector3.Distance(cachedSpawnSlots[i], p);
                if (d > bestD) { bestD = d; best = i; }
            }
            return cachedSpawnSlots[best];
        }
        return cachedSpawnSlots[Random.Range(0, cachedSpawnSlots.Count)];
    }

    int FindNearestSlotIndex(Vector3 p)
    {
        if (cachedSpawnSlots.Count == 0) return 0;
        int best = 0;
        float bestD = float.PositiveInfinity;
        for (int i = 0; i < cachedSpawnSlots.Count; i++)
        {
            float d = (cachedSpawnSlots[i] - p).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    void EnsureTemplate()
    {
        // If a "prefab" reference is actually a scene object, Unity will lose it when the scene object dies.
        // Convert it into a hidden template immediately.
        if (template == null)
        {
            GameObject sceneSource = null;

            if (enemyTorchPrefab != null && enemyTorchPrefab.scene.IsValid())
                sceneSource = enemyTorchPrefab;
            else if (enemyTorchSceneObject != null)
                sceneSource = enemyTorchSceneObject;
            else
                sceneSource = FindAnyEnemyTorchInScene();

            if (sceneSource != null)
            {
                sceneSource = ExtractEnemyUnit(sceneSource);
                template = Instantiate(sceneSource);
                template.name = $"{sceneSource.name}_Template";
                template.SetActive(false);
                DontDestroyOnLoad(template);
                // NOTE: Do NOT disable components on the template.
                // Disabled components would be inherited by instantiated clones, causing them to not move/act.
                // Keeping the template GameObject inactive is enough to prevent it from running.

                // Clear scene refs so we don't depend on them after they die.
                if (enemyTorchPrefab != null && enemyTorchPrefab.scene.IsValid()) enemyTorchPrefab = null;
                if (enemyTorchSceneObject != null) enemyTorchSceneObject = null;
            }
        }
    }

    static GameObject FindAnyEnemyTorchInScene()
    {
        // Best-effort: look for a scene enemy that has our EnemyAI script (EnemyAI is unique to enemies here).
        // This runs only when no explicit prefab/scene reference exists.
        var all = Object.FindObjectsOfType<EnemyAI>(true);
        for (int i = 0; i < all.Length; i++)
        {
            var ai = all[i];
            if (ai == null) continue;
            if (!ai.gameObject.scene.IsValid()) continue;
            // Prefer names that look like torch enemies.
            if (NameEqualsIgnoreCase(ai.gameObject.name, EnemyUnitName)) return ai.gameObject;
            if (ai.gameObject.name.ToLowerInvariant().Contains("torch")) return ExtractEnemyUnit(ai.gameObject);
        }
        // Fallback: any enemy.
        for (int i = 0; i < all.Length; i++)
        {
            var ai = all[i];
            if (ai == null) continue;
            if (!ai.gameObject.scene.IsValid()) continue;
            return ExtractEnemyUnit(ai.gameObject);
        }
        return null;
    }

    static GameObject ExtractEnemyUnit(GameObject maybeContainerOrUnit)
    {
        if (maybeContainerOrUnit == null) return null;
        if (NameEqualsIgnoreCase(maybeContainerOrUnit.name, EnemyUnitName)) return maybeContainerOrUnit;
        if (maybeContainerOrUnit.name.ToLowerInvariant().Contains("torch")) return maybeContainerOrUnit;

        // If user accidentally referenced the container, pick its Enemy_torch child.
        if (NameEqualsIgnoreCase(maybeContainerOrUnit.name, EnemyContainerName))
        {
            var child = FindChildByNameIgnoreCase(maybeContainerOrUnit.transform, EnemyUnitName);
            if (child != null) return child.gameObject;
        }

        // If it has a child Enemy_torch, prefer that.
        var t = FindChildByNameIgnoreCase(maybeContainerOrUnit.transform, EnemyUnitName);
        if (t != null) return t.gameObject;

        // Fallback: first child that looks like torch.
        for (int i = 0; i < maybeContainerOrUnit.transform.childCount; i++)
        {
            var c = maybeContainerOrUnit.transform.GetChild(i);
            if (c == null) continue;
            if (c.name.ToLowerInvariant().Contains("torch")) return c.gameObject;
        }

        return maybeContainerOrUnit;
    }

    static Transform FindChildByNameIgnoreCase(Transform parent, string name)
    {
        if (parent == null) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            var c = parent.GetChild(i);
            if (c == null) continue;
            if (NameEqualsIgnoreCase(c.name, name)) return c;
        }
        return null;
    }

    static bool NameEqualsIgnoreCase(string a, string b)
    {
        return string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
    }

    static void NormalizeEnemyHierarchy()
    {
        var container = GameObject.Find(EnemyContainerName);
        if (container == null) return;

        var unitTr = container.transform.Find(EnemyUnitName);
        if (unitTr == null) return;
        var unit = unitTr.gameObject;

        // If core logic already on unit, nothing to do.
        if (unit.GetComponent<EnemyAI>() != null && container.GetComponent<EnemyAI>() == null) return;

        // Migrate logic components from container -> unit.
        MigrateComponent<EnemyAI>(container, unit);
        MigrateComponent<EnemyCombat>(container, unit);
        MigrateComponent<EnemyMeleeAttack>(container, unit);
        MigrateComponent<EnemyHitCounter>(container, unit);
        MigrateComponent<EnemyDamageEventReceiver>(container, unit);
        MigrateComponent<EnemyStuckTransport>(container, unit);
        MigrateComponent<EnemyBoundaryClamp>(container, unit);
        MigrateComponent<EnemyDangerTint>(container, unit);
        MigrateComponent<EnemyTeleportAheadOfPlayer>(container, unit);
        MigrateComponent<Seeker>(container, unit);
        MigrateComponent<AILerp>(container, unit);
        MigrateComponent<AIDestinationSetter>(container, unit);

        // Remove migrated logic components from container so only unit acts.
        RemoveIfExists<EnemyAI>(container);
        RemoveIfExists<EnemyCombat>(container);
        RemoveIfExists<EnemyMeleeAttack>(container);
        RemoveIfExists<EnemyHitCounter>(container);
        RemoveIfExists<EnemyDamageEventReceiver>(container);
        RemoveIfExists<EnemyStuckTransport>(container);
        RemoveIfExists<EnemyBoundaryClamp>(container);
        RemoveIfExists<EnemyDangerTint>(container);
        RemoveIfExists<EnemyTeleportAheadOfPlayer>(container);
        RemoveIfExists<Seeker>(container);
        RemoveIfExists<AILerp>(container);
        RemoveIfExists<AIDestinationSetter>(container);
    }

    static void MigrateComponent<T>(GameObject src, GameObject dst) where T : Component
    {
        var s = src.GetComponent<T>();
        if (s == null) return;

        var d = dst.GetComponent<T>();
        if (d == null) d = dst.AddComponent<T>();

        CopyAllFields(s, d);
        var b = d as Behaviour;
        if (b != null) b.enabled = true;
    }

    static void RemoveIfExists<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c != null) Object.Destroy(c);
    }

    static void CopyAllFields(object src, object dst)
    {
        if (src == null || dst == null) return;
        var t = src.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var fields = t.GetFields(flags);
        for (int i = 0; i < fields.Length; i++)
        {
            var f = fields[i];
            if (f.IsStatic) continue;
            // Copy public fields + [SerializeField] private fields.
            bool serializable = f.IsPublic || f.GetCustomAttribute<SerializeField>() != null;
            if (!serializable) continue;
            try { f.SetValue(dst, f.GetValue(src)); } catch { }
        }
    }

    void SyncCloneToOriginalUnit(GameObject clone)
    {
        if (clone == null) return;
        var original = FindOriginalUnitInScene(clone);
        if (original == null) return;

        // Match transform-scale (leave position as spawn slot position).
        clone.transform.localScale = original.transform.localScale;

        // Ensure clone has exactly the same core components/settings as the original unit.
        EnsureComponentMatchesOriginal<EnemyAI>(original, clone);
        EnsureComponentMatchesOriginal<EnemyCombat>(original, clone);
        EnsureComponentMatchesOriginal<EnemyMeleeAttack>(original, clone);
        EnsureComponentMatchesOriginal<EnemyHitCounter>(original, clone);
        EnsureComponentMatchesOriginal<EnemyDamageEventReceiver>(original, clone);
        EnsureComponentMatchesOriginal<EnemyStuckTransport>(original, clone);
        EnsureComponentMatchesOriginal<EnemyBoundaryClamp>(original, clone);
        EnsureComponentMatchesOriginal<EnemyDangerTint>(original, clone);
        EnsureComponentMatchesOriginal<EnemyTeleportAheadOfPlayer>(original, clone);
        EnsureComponentMatchesOriginal<Seeker>(original, clone);
        EnsureComponentMatchesOriginal<AILerp>(original, clone);
        EnsureComponentMatchesOriginal<AIDestinationSetter>(original, clone);
    }

    static GameObject FindOriginalUnitInScene(GameObject exclude)
    {
        var container = GameObject.Find(EnemyContainerName);
        if (container == null) return null;

        var t = container.transform.Find(EnemyUnitName);
        if (t != null && t.gameObject != exclude) return t.gameObject;

        // Fallback: any other EnemyAI under Enemy container.
        var ais = container.GetComponentsInChildren<EnemyAI>(true);
        for (int i = 0; i < ais.Length; i++)
        {
            var ai = ais[i];
            if (ai == null) continue;
            if (ai.gameObject == exclude) continue;
            return ai.gameObject;
        }
        return null;
    }

    static void EnsureComponentMatchesOriginal<T>(GameObject original, GameObject clone) where T : Component
    {
        var src = original.GetComponent<T>();
        var dst = clone.GetComponent<T>();

        if (src == null)
        {
            if (dst != null) Object.Destroy(dst);
            return;
        }

        if (dst == null) dst = clone.AddComponent<T>();
        CopyAllFields(src, dst);

        var b = dst as Behaviour;
        if (b != null)
        {
            var srcB = src as Behaviour;
            b.enabled = srcB != null ? srcB.enabled : true;
        }
    }

    static void EnsurePathfindingReady(GameObject go, Transform playerT)
    {
        if (go == null) return;

        var ai = go.GetComponent<EnemyAI>();
        if (ai == null) ai = go.AddComponent<EnemyAI>();
        ai.enabled = true;

        var seeker = go.GetComponent<Seeker>();
        if (seeker == null) seeker = go.AddComponent<Seeker>();
        seeker.enabled = true;

        var dst = go.GetComponent<AIDestinationSetter>();
        if (dst == null) dst = go.AddComponent<AIDestinationSetter>();
        dst.enabled = true;
        if (dst.target == null) dst.target = playerT;

        var aiLerp = go.GetComponent<AILerp>();
        if (aiLerp == null) aiLerp = go.AddComponent<AILerp>();
        aiLerp.enabled = true;
        aiLerp.canSearch = true;
        aiLerp.canMove = true;
        aiLerp.autoRepath.interval = 0.25f;
        aiLerp.SearchPath();

        if (ai != null) ai.SetTarget(playerT);
    }
}

