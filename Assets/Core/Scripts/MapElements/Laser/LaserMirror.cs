using Sirenix.OdinInspector;
using UnityEngine;

public enum LaserMirrorNormalSource
{
    HitNormal,
    TransformForward,
    TransformRight
}

public sealed class LaserMirror : MonoBehaviour
{
    [SerializeField, TitleGroup("Reflection")]
    private bool reflectionEnabled = true;

    [SerializeField, TitleGroup("Reflection")]
    private LaserMirrorNormalSource normalSource = LaserMirrorNormalSource.HitNormal;

    public bool ReflectionEnabled => reflectionEnabled;

    public Vector3 GetReflectedDirection(Vector3 incomingDirection, Vector3 hitNormal)
    {
        Vector3 normal = ResolveNormal(hitNormal);
        Vector3 reflected = Vector3.Reflect(incomingDirection, normal);
        reflected.y = 0f;

        if (reflected.sqrMagnitude <= 0.0001f)
            return -incomingDirection;

        return reflected.normalized;
    }

    private Vector3 ResolveNormal(Vector3 hitNormal)
    {
        Vector3 normal = normalSource switch
        {
            LaserMirrorNormalSource.TransformForward => transform.forward,
            LaserMirrorNormalSource.TransformRight => transform.right,
            _ => hitNormal,
        };

        normal.y = 0f;

        if (normal.sqrMagnitude <= 0.0001f)
            normal = hitNormal;

        normal.y = 0f;
        return normal.normalized;
    }
}
