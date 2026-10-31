using UnityEngine;

/// <summary>
/// Unity AnimationEvents call methods on components on the same GameObject as the Animator.
/// This receiver forwards DealDamage() to an EnemyCombat found on self/parent/children.
/// </summary>
[DisallowMultipleComponent]
public class EnemyDamageEventReceiver : MonoBehaviour
{
    public void DealDamage()
    {
        GameAudioManager.Instance.PlayEnemyAttack();

        var combat = GetComponent<EnemyCombat>();
        if (combat == null) combat = GetComponentInParent<EnemyCombat>();
        if (combat == null) combat = GetComponentInChildren<EnemyCombat>();
        if (combat == null) return;

        var ai = GetComponent<EnemyAI>();
        if (ai == null) ai = GetComponentInParent<EnemyAI>();
        if (ai == null) ai = GetComponentInChildren<EnemyAI>();

        if (ai != null) combat.DealDamageInRange(ai.attackRange);
        else combat.DealDamage();
    }
}

