using UnityEngine;

[DisallowMultipleComponent]
public class EnemyMeleeAttack : MonoBehaviour
{
    [Tooltip("How far the player must be to get hit.")]
    public float range = 1.0f;

    [Tooltip("Seconds between successful hits.")]
    public float cooldownSeconds = 1.0f;

    [Tooltip("Damage applied to PlayerHealth (negative number recommended).")]
    public int damage = -3;
    [Tooltip("Grace period after spawn where enemy cannot attack.")]
    public float attackStartDelay = 1.0f;

    float nextAttackTime;
    Transform player;
    float spawnTime;

    void Awake()
    {
        player = GameObject.FindWithTag("Player")?.transform;
        spawnTime = Time.time;
    }

    public bool InRange()
    {
        if (player == null) player = GameObject.FindWithTag("Player")?.transform;
        if (player == null) return false;
        Vector3 p = transform.position;
        Vector3 t = player.position;
        t.z = p.z;
        return Vector3.Distance(p, t) <= range;
    }

    public bool CanAttackNow() => Time.time >= nextAttackTime;

    public bool TryAttackNow()
    {
        if (Time.time - spawnTime < attackStartDelay) return false;
        if (!InRange()) return false;
        if (!CanAttackNow()) return false;

        var ph = player != null ? player.GetComponent<PlayerHealth>() : null;
        if (ph != null) ph.ChangeHealth(damage);

        nextAttackTime = Time.time + Mathf.Max(0.05f, cooldownSeconds);
        return true;
    }
}

