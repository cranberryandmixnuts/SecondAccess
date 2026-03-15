using UnityEngine;
using Sirenix.OdinInspector;

public sealed class FollowObject : MonoBehaviour
{
    [SerializeField] private Transform target;

    private void Update()
    {
        if (target != null) transform.position = target.position;
    }
}
