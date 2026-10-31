using UnityEngine;

public class EnemyCombat : MonoBehaviour
{
    public int damage = -3;
    public bool IsTouchingPlayer { get; private set; }
    public System.Action OnAttackHit;
    PlayerHealth currentPlayer;
    Collider2D currentPlayerCollider;
    Collider2D selfCollider;

    [Header("Debug")]
    [Tooltip("Logs why damage did not apply (useful when setting up colliders/layers).")]
    public bool logWhenDamageSkipped = false;

    /// <summary>
    /// Call this from an Animation Event on the Attack animation at the exact hit frame.
    /// The player will only take damage if they are currently touching the enemy.
    /// </summary>
    public void DealDamage()
    {
        if (selfCollider == null) selfCollider = GetComponent<Collider2D>();

        // If we have collider references, verify we are STILL overlapping right now.
        // (Sometimes OnTriggerExit/OnCollisionExit doesn't fire due to transform-driven movement.)
        if (IsTouchingPlayer && selfCollider != null && currentPlayerCollider != null)
        {
            var d = Physics2D.Distance(selfCollider, currentPlayerCollider);
            if (!d.isOverlapped)
            {
                SetTouching(null, null);
            }
        }

        if (!IsTouchingPlayer || currentPlayer == null)
        {
            if (logWhenDamageSkipped)
                Debug.Log($"EnemyCombat: DealDamage skipped (IsTouchingPlayer={IsTouchingPlayer}, currentPlayer={(currentPlayer != null)})", this);
            return;
        }
        currentPlayer.ChangeHealth(damage);
        OnAttackHit?.Invoke();
    }

    public void DealDamageInRange(float range)
    {
        var playerGo = GameObject.FindWithTag("Player");
        if (playerGo == null) return;
        var player = playerGo.GetComponent<PlayerHealth>();
        if (player == null) return;

        Vector3 p = transform.position;
        Vector3 t = player.transform.position;
        t.z = p.z;
        if (Vector3.Distance(p, t) > Mathf.Max(0.01f, range))
            return;

        player.ChangeHealth(damage);
        OnAttackHit?.Invoke();
    }

    static PlayerHealth FindPlayerHealth(Component hit)
    {
        if (hit == null) return null;
        var go = hit.gameObject;
        var ph = go.GetComponent<PlayerHealth>();
        if (ph != null) return ph;
        ph = go.GetComponentInParent<PlayerHealth>();
        if (ph != null) return ph;
        return go.GetComponentInChildren<PlayerHealth>();
    }

    void SetTouching(PlayerHealth player, Collider2D playerCol)
    {
        IsTouchingPlayer = player != null;
        currentPlayer = player;
        currentPlayerCollider = playerCol;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        var player = FindPlayerHealth(collision.collider);
        if (player == null) return;
        SetTouching(player, collision.collider);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        var player = FindPlayerHealth(collision.collider);
        if (player == null) return;
        SetTouching(player, collision.collider);
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        // Only clear if we exited the same player collider we latched onto.
        if (currentPlayerCollider != null && collision.collider != currentPlayerCollider) return;
        if (FindPlayerHealth(collision.collider) == null) return;
        SetTouching(null, null);
    }

    // Triggers are more reliable when enemies are moved by pathfinding/Transform updates.
    private void OnTriggerEnter2D(Collider2D other)
    {
        var player = FindPlayerHealth(other);
        if (player == null) return;
        SetTouching(player, other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        var player = FindPlayerHealth(other);
        if (player == null) return;
        SetTouching(player, other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (currentPlayerCollider != null && other != currentPlayerCollider) return;
        if (FindPlayerHealth(other) == null) return;
        SetTouching(null, null);
    }
}