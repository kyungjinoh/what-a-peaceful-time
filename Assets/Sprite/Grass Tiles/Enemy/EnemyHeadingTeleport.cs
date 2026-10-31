using UnityEngine;
using Pathfinding;

[DisallowMultipleComponent]
public class EnemyHeadingTeleport : MonoBehaviour
{
    [Tooltip("Seconds between teleports.")]
    public float cooldownSeconds = 12f;

    [Tooltip("Teleport distance (world units) in the player's heading direction.")]
    public float teleportDistance = 4f;

    float nextTime;
    Rigidbody2D playerRb;
    Vector2 lastHeading = Vector2.right;
    PlayerMovement playerMovement;

    void Update()
    {
        if (!GameStartReplayUI.IsRunning) return;
        if (Time.unscaledTime < nextTime) return;

        var player = GameObject.FindWithTag("Player");
        if (player == null) return;

        if (playerRb == null) playerRb = player.GetComponent<Rigidbody2D>();
        if (playerMovement == null) playerMovement = player.GetComponent<PlayerMovement>();
        if (playerRb != null)
        {
            // PlayerMovement in this project writes rb.linearVelocity.
            Vector2 v = playerRb.linearVelocity;
            if (v.sqrMagnitude > 0.0004f) lastHeading = v.normalized;
        }
        // Fallback to facing direction if standing still.
        if (playerMovement != null && playerRb != null && playerRb.linearVelocity.sqrMagnitude <= 0.0004f)
        {
            lastHeading = playerMovement.facingdirection >= 0 ? Vector2.right : Vector2.left;
        }

        Vector3 dest = transform.position + (Vector3)(lastHeading * Mathf.Max(0.1f, teleportDistance));

        // Snap to nearest walkable node to keep pathfinding stable.
        if (AstarPath.active != null)
        {
            var constraint = NNConstraint.Default;
            constraint.constrainWalkability = true;
            constraint.walkable = true;
            var nn = AstarPath.active.GetNearest(dest, constraint);
            if (nn.node != null) dest = (Vector3)nn.position;
        }

        transform.position = new Vector3(dest.x, dest.y, transform.position.z);

        // Respect boundary clamp if present (it will clamp in LateUpdate).
        nextTime = Time.unscaledTime + Mathf.Max(0.1f, cooldownSeconds);
    }
}

