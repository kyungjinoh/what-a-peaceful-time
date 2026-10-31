using System.Collections;
using UnityEngine;
using Pathfinding;

[DisallowMultipleComponent]
public class EnemyTeleportAheadOfPlayer : MonoBehaviour
{
    [Tooltip("Seconds between teleports (realtime, unaffected by pause).")]
    public float cooldownSeconds = 12f;

    [Tooltip("How far ahead of the player's heading direction to teleport.")]
    public float distance = 4f;

    [Tooltip("Minimum distance from player required to teleport (prevents jitter).")]
    public float minDistance = 2f;

    Coroutine loop;

    void OnEnable()
    {
        if (loop == null) loop = StartCoroutine(Loop());
    }

    void OnDisable()
    {
        if (loop != null) { StopCoroutine(loop); loop = null; }
    }

    IEnumerator Loop()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, cooldownSeconds));
            if (!GameStartReplayUI.IsRunning) continue;
            Teleport();
        }
    }

    void Teleport()
    {
        var player = GameObject.FindWithTag("Player");
        if (player == null) return;

        Vector3 pp = player.transform.position;
        Vector3 ep = transform.position;
        pp.z = ep.z;

        if (Vector3.Distance(pp, ep) < minDistance) return;

        // Determine heading: prefer rigidbody velocity, fallback to facing direction.
        Vector2 heading = Vector2.right;
        var prb = player.GetComponent<Rigidbody2D>();
        if (prb != null && prb.linearVelocity.sqrMagnitude > 0.0004f)
            heading = prb.linearVelocity.normalized;
        else
        {
            var pm = player.GetComponent<PlayerMovement>();
            if (pm != null) heading = pm.facingdirection >= 0 ? Vector2.right : Vector2.left;
        }

        Vector3 dest = pp + (Vector3)(heading * Mathf.Max(0.1f, distance));

        // Snap to walkable node so AI doesn't break.
        if (AstarPath.active != null)
        {
            var c = NNConstraint.Default;
            c.constrainWalkability = true;
            c.walkable = true;
            var nn = AstarPath.active.GetNearest(dest, c);
            if (nn.node != null) dest = (Vector3)nn.position;
        }

        transform.position = new Vector3(dest.x, dest.y, ep.z);

        // Force replanning.
        var ai = GetComponent<AILerp>();
        if (ai != null) ai.SearchPath();
    }
}

