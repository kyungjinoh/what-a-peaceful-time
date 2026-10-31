using UnityEngine;
using Pathfinding;

[DisallowMultipleComponent]
public class EnemyStuckTransport : MonoBehaviour
{
    [Tooltip("Seconds of near-zero movement before transporting.")]
    public float stuckSeconds = 2.5f;

    [Tooltip("If enemy moves less than this per second, we consider it stuck.")]
    public float minMoveSpeed = 0.08f;

    [Tooltip("Do not transport if already this close to the player.")]
    public float minPlayerDistance = 1.2f;

    [Tooltip("How far from the player to place the enemy when transporting.")]
    public float teleportOffset = 1.4f;

    float stuckTimer;
    Vector3 lastPos;
    Transform player;
    AILerp aiLerp;
    EnemyAI enemyAI;

    void Awake()
    {
        lastPos = transform.position;
        aiLerp = GetComponent<AILerp>();
        enemyAI = GetComponent<EnemyAI>();
    }

    void Update()
    {
        if (player == null)
            player = GameObject.FindWithTag("Player")?.transform;
        if (player == null) return;

        // If EnemyAI is disabled (or game paused), don't accumulate stuck time.
        if (enemyAI != null && !enemyAI.isActiveAndEnabled) { ResetStuck(); return; }
        if (Time.timeScale <= 0f) { ResetStuck(); return; }

        // If we're already near the player, don't transport.
        Vector3 p = transform.position;
        Vector3 pl = player.position;
        pl.z = p.z;
        float distToPlayer = Vector3.Distance(p, pl);
        if (distToPlayer <= minPlayerDistance) { ResetStuck(); return; }

        float moved = Vector3.Distance(p, lastPos);
        float speed = moved / Mathf.Max(0.0001f, Time.deltaTime);
        lastPos = p;

        bool shouldMove = true;
        if (aiLerp == null) aiLerp = GetComponent<AILerp>();
        if (aiLerp != null && (aiLerp.isStopped || !aiLerp.canMove || !aiLerp.canSearch))
            shouldMove = false;

        if (!shouldMove) { ResetStuck(); return; }

        if (speed < minMoveSpeed) stuckTimer += Time.deltaTime;
        else stuckTimer = 0f;

        if (stuckTimer >= stuckSeconds)
        {
            TransportNearPlayer();
            ResetStuck();
        }
    }

    void ResetStuck()
    {
        stuckTimer = 0f;
        lastPos = transform.position;
    }

    void TransportNearPlayer()
    {
        if (player == null) return;

        Vector3 p = transform.position;
        Vector3 pl = player.position;
        pl.z = p.z;

        Vector3 dir = (p - pl);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.right;
        dir.z = 0f;
        dir.Normalize();

        Vector3 dest = pl + dir * teleportOffset;

        // Snap to nearest walkable node.
        if (AstarPath.active != null)
        {
            var c = NNConstraint.Default;
            c.constrainWalkability = true;
            c.walkable = true;
            var nn = AstarPath.active.GetNearest(dest, c);
            if (nn.node != null) dest = (Vector3)nn.position;
        }

        transform.position = new Vector3(dest.x, dest.y, p.z);

        // Force new path calculation after teleport.
        if (aiLerp == null) aiLerp = GetComponent<AILerp>();
        if (aiLerp != null)
            aiLerp.SearchPath();
    }
}

