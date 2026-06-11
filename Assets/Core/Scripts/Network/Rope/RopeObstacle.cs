using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class RopeObstacle : MonoBehaviour
{
    private static readonly List<RopeObstacle> registered = new();

    [SerializeField, TitleGroup("Rope Obstacle")]
    private bool includeChildColliders = true;

    [SerializeField, TitleGroup("Rope Obstacle")]
    private List<Collider> colliders = new();

    public static IReadOnlyList<RopeObstacle> Registered => registered;
    public IReadOnlyList<Collider> Colliders => colliders;
    public bool AffectsRope => isActiveAndEnabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => registered.Clear();

    private void Reset() => CacheColliders();

    private void Awake()
    {
        if (colliders.Count == 0)
            CacheColliders();
    }

    private void OnEnable()
    {
        if (!registered.Contains(this))
            registered.Add(this);
    }

    private void OnDisable() => registered.Remove(this);

    [Button]
    private void CacheColliders()
    {
        colliders.Clear();

        if (includeChildColliders)
            GetComponentsInChildren(false, colliders);
        else if (TryGetComponent(out Collider ownCollider))
            colliders.Add(ownCollider);

        for (int i = colliders.Count - 1; i >= 0; i--)
        {
            if (colliders[i] == null)
                colliders.RemoveAt(i);
        }
    }
}
