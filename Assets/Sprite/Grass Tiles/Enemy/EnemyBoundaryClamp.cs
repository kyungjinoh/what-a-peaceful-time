using UnityEngine;

[DisallowMultipleComponent]
public class EnemyBoundaryClamp : MonoBehaviour
{
    // Hard boundary requested by user.
    public const float BoundaryRadius = 13f;

    [Tooltip("Center of the allowed area. If null, uses initial position as center.")]
    public Transform center;

    Vector3 fallbackCenter;

    void Awake()
    {
        fallbackCenter = transform.position;
    }

    void LateUpdate()
    {
        Vector3 c = center != null ? center.position : fallbackCenter;
        Vector3 p = transform.position;

        Vector3 d = p - c;
        d.z = 0f;
        float m = d.magnitude;
        if (m <= BoundaryRadius) return;

        Vector3 clamped = c + d / Mathf.Max(0.0001f, m) * BoundaryRadius;
        clamped.z = p.z;
        transform.position = clamped;
    }
}

