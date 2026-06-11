using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(TriggerTarget))]
[RequireComponent(typeof(LineRenderer))]
public sealed class LaserEmitterRuntime : MonoBehaviour
{
    private const float MinimumSegmentLength = 0.001f;

    [SerializeField, Required, TitleGroup("References")]
    private TriggerTarget triggerTarget;

    [SerializeField, Required, TitleGroup("References")]
    private LineRenderer lineRenderer;

    [SerializeField, TitleGroup("Laser")]
    private Transform laserOrigin;

    [SerializeField, TitleGroup("Laser"), MinValue(0.1f)]
    private float maxDistance = 40f;

    [SerializeField, TitleGroup("Laser"), MinValue(0)]
    private int maxReflections = 8;

    [SerializeField, TitleGroup("Laser")]
    private LayerMask collisionMask = ~0;

    [SerializeField, TitleGroup("Laser"), MinValue(0.001f)]
    private float raySkin = 0.03f;

    [SerializeField, TitleGroup("Link Interaction"), MinValue(0.001f)]
    private float linkInteractionRadius = 0.12f;

    [SerializeField, TitleGroup("Link Interaction"), MinValue(0f)]
    private float linkHeightTolerance = 1f;

    [SerializeField, TitleGroup("Visual")]
    private Color laserColor = Color.red;

    [SerializeField, TitleGroup("Visual"), MinValue(0f)]
    private float width = 0.08f;

    [SerializeField, TitleGroup("Visual"), MinValue(0)]
    private int capVertices = 4;

    public LayerMask CollisionMask => collisionMask;
    public float EnergyLinkInputRadius => linkInteractionRadius;

    private readonly List<Vector3> points = new();
    private readonly HashSet<Collider> ignoredColliders = new();
    private readonly RaycastHit[] physicsHitBuffer = new RaycastHit[32];

    private MaterialPropertyBlock propertyBlock;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        ConfigureRenderer();
    }

    private void OnEnable() => LaserSystemManager.RegisterEmitter(this);

    private void OnDisable()
    {
        LaserSystemManager.UnregisterEmitter(this);
        ClearVisual();
    }

    internal bool Simulate(HashSet<LaserSystemManager.ReceiverInput> receiverInputs)
    {
        points.Clear();
        ignoredColliders.Clear();

        Vector3 origin = laserOrigin.position;
        Vector3 direction = ResolvePlanarDirection(laserOrigin.forward);
        float remainingDistance = maxDistance;
        bool energyLinkHit = false;

        points.Add(origin);

        for (int reflectionIndex = 0; reflectionIndex <= maxReflections; reflectionIndex++)
        {
            if (remainingDistance <= MinimumSegmentLength) break;

            bool hasPhysicsHit = TryGetNearestPhysicsHit(origin, direction, remainingDistance, out RaycastHit physicsHit);
            float physicsDistance = hasPhysicsHit ? physicsHit.distance : remainingDistance;

            if (TryHitEnergyLink(origin, direction, physicsDistance)) energyLinkHit = true;

            if (TryHitRopeLink(origin, direction, physicsDistance, out Vector3 linkHitPoint, out Vector3 linkReflectedDirection))
            {
                AddPoint(linkHitPoint);
                remainingDistance -= Vector3.Distance(origin, linkHitPoint);
                origin = linkHitPoint + linkReflectedDirection * raySkin;
                direction = linkReflectedDirection;
                continue;
            }

            if (!hasPhysicsHit)
            {
                AddPoint(origin + direction * remainingDistance);
                break;
            }

            AddPoint(physicsHit.point);
            HandlePhysicsHit(physicsHit, receiverInputs, ref origin, ref direction, ref remainingDistance, out bool continueTrace);

            if (!continueTrace) break;
        }

        DrawVisual();
        return energyLinkHit;
    }

    internal void ClearVisual()
    {
        if (lineRenderer == null) return;

        lineRenderer.enabled = false;
        lineRenderer.positionCount = 0;
    }

    private void ConfigureRenderer()
    {
        lineRenderer.enabled = false;
        lineRenderer.useWorldSpace = true;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.numCapVertices = capVertices;
        lineRenderer.numCornerVertices = capVertices;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
    }

    private void DrawVisual()
    {
        if (points.Count < 2)
        {
            ClearVisual();
            return;
        }

        lineRenderer.enabled = true;
        lineRenderer.positionCount = points.Count;
        lineRenderer.SetPositions(points.ToArray());
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.startColor = laserColor;
        lineRenderer.endColor = laserColor;
        lineRenderer.numCapVertices = capVertices;
        lineRenderer.numCornerVertices = capVertices;

        propertyBlock.Clear();
        propertyBlock.SetColor("_BaseColor", laserColor);
        propertyBlock.SetColor("_Color", laserColor);
        lineRenderer.SetPropertyBlock(propertyBlock);
    }

    private void HandlePhysicsHit(RaycastHit hit, HashSet<LaserSystemManager.ReceiverInput> receiverInputs, ref Vector3 origin, ref Vector3 direction, ref float remainingDistance, out bool continueTrace)
    {
        remainingDistance -= hit.distance;
        continueTrace = false;

        if (LaserSystemManager.TryResolveReceiver(hit.collider, out ILaserReceiver receiver))
        {
            LaserSystemManager.AddDirectInput(receiverInputs, receiver, this);

            if (receiver.BlocksLaser) return;

            ignoredColliders.Add(hit.collider);
            origin = hit.point + direction * raySkin;
            continueTrace = true;
            return;
        }

        if (!LaserSystemManager.TryResolveMirror(hit.collider, out LaserMirror mirror)) return;

        Vector3 reflectedDirection = mirror.GetReflectedDirection(direction, hit.normal);
        origin = hit.point + reflectedDirection * raySkin;
        direction = reflectedDirection;
        continueTrace = true;
    }

    private bool TryGetNearestPhysicsHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearestHit)
    {
        int hitCount = Physics.RaycastNonAlloc(origin, direction, physicsHitBuffer, distance, collisionMask, QueryTriggerInteraction.Collide);

        nearestHit = default;
        float nearestDistance = float.PositiveInfinity;
        bool hasHit = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = physicsHitBuffer[i];

            if (hit.distance <= raySkin) continue;
            if (ignoredColliders.Contains(hit.collider)) continue;
            if (!IsMeaningfulHit(hit.collider)) continue;
            if (hit.distance >= nearestDistance) continue;

            nearestHit = hit;
            nearestDistance = hit.distance;
            hasHit = true;
        }

        return hasHit;
    }

    private bool IsMeaningfulHit(Collider targetCollider)
    {
        if (targetCollider == null) return false;

        if (LaserSystemManager.TryResolveReceiver(targetCollider, out _)) return true;

        if (LaserSystemManager.TryResolveMirror(targetCollider, out _)) return true;

        return !targetCollider.isTrigger;
    }

    private bool TryHitRopeLink(Vector3 origin, Vector3 direction, float maxSegmentDistance, out Vector3 hitPoint, out Vector3 reflectedDirection)
    {
        hitPoint = Vector3.zero;
        reflectedDirection = Vector3.zero;

        LinkManager manager = LinkManager.Instance;

        if (manager == null || manager.Mode.Value != LinkMode.Rope) return false;

        if (!manager.TryGetLinkPath(out Vector3 firstPosition, out Vector3 relayPosition, out Vector3 secondPosition, out bool usesRelay)) return false;

        if (!TryGetNearestLinkIntersection(origin, direction, firstPosition, usesRelay ? relayPosition : secondPosition, maxSegmentDistance, out LinkIntersection nearest)) return false;

        if (usesRelay && TryGetNearestLinkIntersection(origin, direction, relayPosition, secondPosition, maxSegmentDistance, out LinkIntersection secondIntersection) && secondIntersection.Distance < nearest.Distance)
            nearest = secondIntersection;

        hitPoint = nearest.Point;
        reflectedDirection = ReflectByLinkSegment(direction, nearest.SegmentStart, nearest.SegmentEnd);
        return true;
    }

    private bool TryHitEnergyLink(Vector3 origin, Vector3 direction, float maxSegmentDistance)
    {
        LinkManager manager = LinkManager.Instance;

        if (manager == null || manager.Mode.Value != LinkMode.Energy) return false;

        if (!manager.TryGetLinkPath(out Vector3 firstPosition, out Vector3 relayPosition, out Vector3 secondPosition, out bool usesRelay)) return false;

        if (TryGetNearestLinkIntersection(origin, direction, firstPosition, usesRelay ? relayPosition : secondPosition, maxSegmentDistance, out _)) return true;

        return usesRelay && TryGetNearestLinkIntersection(origin, direction, relayPosition, secondPosition, maxSegmentDistance, out _);
    }

    private bool TryGetNearestLinkIntersection(Vector3 origin, Vector3 direction, Vector3 segmentStart, Vector3 segmentEnd, float maxDistance, out LinkIntersection intersection)
    {
        intersection = default;

        Vector2 rayOrigin = new(origin.x, origin.z);
        Vector2 rayDirection = new(direction.x, direction.z);
        Vector2 start = new(segmentStart.x, segmentStart.z);
        Vector2 end = new(segmentEnd.x, segmentEnd.z);
        Vector2 segment = end - start;

        float denominator = Cross(rayDirection, segment);

        if (Mathf.Abs(denominator) <= 0.0001f) return false;

        Vector2 delta = start - rayOrigin;
        float rayDistance = Cross(delta, segment) / denominator;
        float segmentT = Cross(delta, rayDirection) / denominator;

        if (rayDistance <= raySkin || rayDistance > maxDistance) return false;

        if (segmentT < 0f || segmentT > 1f) return false;

        Vector3 point = origin + direction * rayDistance;
        Vector3 linkPoint = Vector3.Lerp(segmentStart, segmentEnd, segmentT);

        if (Mathf.Abs(point.y - linkPoint.y) > linkHeightTolerance) return false;

        intersection = new LinkIntersection(point, segmentStart, segmentEnd, rayDistance);
        return true;
    }

    private Vector3 ReflectByLinkSegment(Vector3 direction, Vector3 segmentStart, Vector3 segmentEnd)
    {
        Vector3 segmentDirection = segmentEnd - segmentStart;
        segmentDirection.y = 0f;

        if (segmentDirection.sqrMagnitude <= 0.0001f) return -direction;

        segmentDirection.Normalize();
        Vector3 normal = new(-segmentDirection.z, 0f, segmentDirection.x);
        Vector3 reflected = Vector3.Reflect(direction, normal);
        reflected.y = 0f;

        if (reflected.sqrMagnitude <= 0.0001f) return -direction;

        return reflected.normalized;
    }

    private void AddPoint(Vector3 point)
    {
        if (points.Count > 0 && Vector3.Distance(points[^1], point) <= MinimumSegmentLength) return;

        points.Add(point);
    }

    private Vector3 ResolvePlanarDirection(Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f) return Vector3.forward;

        return direction.normalized;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private readonly struct LinkIntersection
    {
        public Vector3 Point { get; }
        public Vector3 SegmentStart { get; }
        public Vector3 SegmentEnd { get; }
        public float Distance { get; }

        public LinkIntersection(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd, float distance)
        {
            Point = point;
            SegmentStart = segmentStart;
            SegmentEnd = segmentEnd;
            Distance = distance;
        }
    }
}