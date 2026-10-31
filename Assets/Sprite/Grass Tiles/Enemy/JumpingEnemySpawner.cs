using System.Collections;
using UnityEngine;
using Pathfinding;

[DisallowMultipleComponent]
public class JumpingEnemySpawner : MonoBehaviour
{
    [Header("Spawn Target")]
    [Tooltip("Scene object to enable after the delay (NO prefab required).")]
    public GameObject sceneEnemy;

    [Tooltip("Prefab to spawn (optional). If set, a new instance will be created after the delay.")]
    public GameObject enemyPrefab;

    [Tooltip("Seconds after play starts to spawn the jumping enemy.")]
    public float spawnDelay = 15f;

    [Tooltip("Where to spawn. If null, spawns at this object's position.")]
    public Transform spawnPoint;

    [Tooltip("If true, adds/ensures JumpingEnemy component on the spawned enemy.")]
    public bool ensureJumpingEnemyComponent = true;

    [Header("Visibility")]
    [Tooltip("If true, hides the scene enemy object until spawnDelay.")]
    public bool hideSceneObjectUntilDelay = true;

    GameObject sceneObjectToEnable;

    void Awake()
    {
        // Safety: never disable the object that owns this spawner,
        // otherwise the coroutine will never run and nothing will spawn/enable.
        if (sceneEnemy != null && sceneEnemy == gameObject)
        {
            Debug.LogError("JumpingEnemySpawner: 'sceneEnemy' is set to THIS GameObject. " +
                           "That would disable the spawner and prevent spawning. Clearing sceneEnemy.");
            sceneEnemy = null;
        }

        if (enemyPrefab != null && enemyPrefab.scene.IsValid() && enemyPrefab == gameObject)
        {
            Debug.LogError("JumpingEnemySpawner: 'enemyPrefab' references THIS scene GameObject. " +
                           "That would disable the spawner and prevent spawning. Clearing enemyPrefab.");
            enemyPrefab = null;
        }

        // Prefer explicit scene enemy if provided.
        if (hideSceneObjectUntilDelay && sceneEnemy != null)
        {
            sceneObjectToEnable = sceneEnemy;
            sceneObjectToEnable.SetActive(false);
            return;
        }

        // Backward compatible: if the user dragged a Hierarchy object into enemyPrefab.
        if (hideSceneObjectUntilDelay && enemyPrefab != null && enemyPrefab.scene.IsValid())
        {
            sceneObjectToEnable = enemyPrefab;
            sceneObjectToEnable.SetActive(false);
        }
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(spawnDelay);

        if (sceneObjectToEnable != null)
        {
            Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
            if (AstarPath.active != null)
            {
                var constraint = NNConstraint.Default;
                constraint.constrainWalkability = true;
                constraint.walkable = true;
                var nn = AstarPath.active.GetNearest(pos, constraint);
                if (nn.node != null) pos = nn.position;
            }

            sceneObjectToEnable.transform.SetPositionAndRotation(pos, Quaternion.identity);
            sceneObjectToEnable.SetActive(true);
            if (ensureJumpingEnemyComponent && sceneObjectToEnable.GetComponent<JumpingEnemy>() == null)
                sceneObjectToEnable.AddComponent<JumpingEnemy>();
            yield break;
        }

        if (enemyPrefab == null)
        {
            Debug.LogWarning("JumpingEnemySpawner: Assign sceneEnemy (recommended) or enemyPrefab.");
            yield break;
        }

        Vector3 spawnPos = spawnPoint != null ? spawnPoint.position : transform.position;

        // Snap spawn position to the nearest walkable node to avoid "Path Failed (Searched Nodes 0)".
        if (AstarPath.active != null)
        {
            var constraint = NNConstraint.Default;
            constraint.constrainWalkability = true;
            constraint.walkable = true;
            var nn = AstarPath.active.GetNearest(spawnPos, constraint);
            if (nn.node != null)
                spawnPos = nn.position;
        }
        var go = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);

        if (ensureJumpingEnemyComponent && go.GetComponent<JumpingEnemy>() == null)
            go.AddComponent<JumpingEnemy>();
    }
}

