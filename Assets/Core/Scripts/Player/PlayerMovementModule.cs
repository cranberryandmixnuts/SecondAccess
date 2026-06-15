using Sirenix.OdinInspector;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class PlayerMovementModule : PlayerModule
{
    [SerializeField, MinValue(0f), TitleGroup("Movement")]
    private float moveSpeed = 10f;

    [SerializeField, MinValue(0f), TitleGroup("Rotation")]
    private float rotationSpeed = 1080f;

    public Vector2 Input { get; private set; }
    public Vector3 MoveDirection { get; private set; }
    public Vector3 DesiredVelocity { get; private set; }
    public Vector3 PlanarVelocity => new(Player.Body.linearVelocity.x, 0f, Player.Body.linearVelocity.z);
    public bool SimulationEnabled { get; private set; } = true;

    private NetworkPlayer networkPlayer;

    private bool ShouldSimulate => !Player.IsSpawned || Player.IsServer;

    private void FixedUpdate()
    {
        if (!SimulationEnabled)
            return;

        if (!ShouldSimulate)
            return;

        MoveDirection = ResolveMoveDirection(Input);
        DesiredVelocity = MoveDirection * moveSpeed;

        Vector3 planarVelocity = ResolveLinkedPlanarVelocity(DesiredVelocity);

        Player.Body.linearVelocity = new Vector3(planarVelocity.x, Player.Body.linearVelocity.y, planarVelocity.z);

        EvaluateEnergyDistance();

        if (MoveDirection.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(MoveDirection, Vector3.up);
        Quaternion nextRotation = Quaternion.RotateTowards(Player.Body.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);

        Player.Body.MoveRotation(nextRotation);
    }

    public void SetInput(Vector2 input) => Player.SetMovementInput(input);

    internal void ApplyInputFromNetwork(Vector2 input) => Input = Vector2.ClampMagnitude(input, 1f);

    public void ApplyNetworkState()
    {
        Player.Body.isKinematic = Player.IsSpawned && !Player.IsServer;
    }

    public void ApplyLocalState()
    {
        Player.Body.isKinematic = false;
    }

    public void SetSimulationEnabled(bool enabled)
    {
        if (SimulationEnabled == enabled)
            return;

        SimulationEnabled = enabled;

        if (SimulationEnabled)
            return;

        Input = Vector2.zero;
        MoveDirection = Vector3.zero;
        DesiredVelocity = Vector3.zero;

        if (Player.IsServer || !Player.IsSpawned)
            Player.Body.linearVelocity = new Vector3(0f, Player.Body.linearVelocity.y, 0f);
    }

    private Vector3 ResolveLinkedPlanarVelocity(Vector3 baseVelocity)
    {
        if (!TryGetLinkContext(out LinkManager linkManager, out NetworkPlayer self, out NetworkPlayer first, out NetworkPlayer second))
            return baseVelocity;

        if (linkManager.Mode.Value != LinkMode.Rope)
            return baseVelocity;

        return ResolveRopeVelocity(baseVelocity, linkManager, self, first, second);
    }

    private Vector3 ResolveRopeVelocity(Vector3 baseVelocity, LinkManager linkManager, NetworkPlayer self, NetworkPlayer first, NetworkPlayer second)
    {
        Rigidbody firstBody = first.Body;
        Rigidbody secondBody = second.Body;

        Vector3 delta = secondBody.position - firstBody.position;
        delta.y = 0f;

        float distance = delta.magnitude;

        if (distance <= 0.0001f)
            return baseVelocity;

        Vector3 direction = delta / distance;
        bool isFirst = self.NetworkObjectId == first.NetworkObjectId;
        NetworkPlayer other = isFirst ? second : first;

        Vector3 otherBaseVelocity = other.Movement.CalculateDesiredVelocity();
        Vector3 inwardDirection = isFirst ? direction : -direction;
        Vector3 outwardDirection = -inwardDirection;
        Vector3 otherOutwardDirection = isFirst ? direction : -direction;

        float slowdownStartDistance = Mathf.Min(linkManager.RopeSoftDistance, linkManager.RopeMaxDistance);
        float stretchRatio = GetSoftStretchRatio(distance, slowdownStartDistance, linkManager.RopeMaxDistance);
        float overstretchRatio = GetOverstretchRatio(distance, linkManager.RopeMaxDistance, linkManager.RopeOverstretchDistance);

        Vector3 nextVelocity = baseVelocity;

        if (stretchRatio > 0f || overstretchRatio > 0f)
            nextVelocity = ApplyOutwardMovementResistance(nextVelocity, otherBaseVelocity, outwardDirection, otherOutwardDirection, inwardDirection, Mathf.Max(stretchRatio, overstretchRatio), linkManager);

        if (stretchRatio > 0f)
            nextVelocity += inwardDirection * GetRopePullSpeed(stretchRatio, linkManager.RopePullAcceleration);

        if (overstretchRatio > 0f)
            nextVelocity += inwardDirection * GetRopePullSpeed(overstretchRatio, linkManager.RopeOverstretchPullAcceleration);

        if (TryApplyOwnHardLimitCorrection(linkManager, direction, distance, isFirst))
            nextVelocity = RemoveOwnSeparatingVelocity(nextVelocity, otherBaseVelocity, direction, inwardDirection, isFirst);

        return nextVelocity;
    }

    private Vector3 ApplyOutwardMovementResistance(Vector3 selfVelocity, Vector3 otherVelocity, Vector3 selfOutwardDirection, Vector3 otherOutwardDirection, Vector3 inwardDirection, float stretchRatio, LinkManager linkManager)
    {
        Vector3 nextVelocity = selfVelocity;

        float elasticRatio = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(stretchRatio));
        float speedRatio = Mathf.Lerp(1f, linkManager.MinimumOutwardSpeedRatio, elasticRatio);
        float removedRatio = 1f - speedRatio;

        float selfOutwardSpeed = Mathf.Max(0f, Vector3.Dot(selfVelocity, selfOutwardDirection));
        float otherOutwardSpeed = Mathf.Max(0f, Vector3.Dot(otherVelocity, otherOutwardDirection));

        if (selfOutwardSpeed > 0f)
        {
            float removedSpeed = selfOutwardSpeed * removedRatio;
            nextVelocity -= selfOutwardDirection * removedSpeed;
        }

        float sharedOutwardSpeed = Mathf.Min(selfOutwardSpeed, otherOutwardSpeed);
        float otherExclusiveOutwardSpeed = Mathf.Max(0f, otherOutwardSpeed - sharedOutwardSpeed);

        if (otherExclusiveOutwardSpeed > 0f)
        {
            float transferSpeed = otherExclusiveOutwardSpeed * removedRatio * linkManager.RopeDragTransferRatio;
            nextVelocity += inwardDirection * transferSpeed;
        }

        return nextVelocity;
    }

    private bool TryApplyOwnHardLimitCorrection(LinkManager linkManager, Vector3 direction, float distance, bool isFirst)
    {
        float hardLimitDistance = linkManager.RopeMaxDistance + Mathf.Max(0f, linkManager.RopeOverstretchDistance);

        if (distance <= hardLimitDistance)
            return false;

        float excess = distance - hardLimitDistance;
        float correction = Mathf.Min(excess * 0.25f, linkManager.RopeHardCorrectionSpeed * Time.fixedDeltaTime);
        Vector3 inwardDirection = isFirst ? direction : -direction;

        Player.Body.MovePosition(Player.Body.position + inwardDirection * correction);
        return true;
    }

    private Vector3 RemoveOwnSeparatingVelocity(Vector3 selfVelocity, Vector3 otherVelocity, Vector3 direction, Vector3 inwardDirection, bool isFirst)
    {
        Vector3 firstVelocity = isFirst ? selfVelocity : otherVelocity;
        Vector3 secondVelocity = isFirst ? otherVelocity : selfVelocity;
        Vector3 relativeVelocity = secondVelocity - firstVelocity;

        float separatingSpeed = Vector3.Dot(relativeVelocity, direction);

        if (separatingSpeed <= 0f)
            return selfVelocity;

        return selfVelocity + inwardDirection * (separatingSpeed * 0.5f);
    }

    private void EvaluateEnergyDistance()
    {
        if (!TryGetLinkContext(out LinkManager linkManager, out NetworkPlayer self, out NetworkPlayer first, out NetworkPlayer second))
            return;

        if (linkManager.Mode.Value != LinkMode.Energy)
            return;

        linkManager.EvaluateEnergyDistance();
    }

    private bool TryGetLinkContext(out LinkManager linkManager, out NetworkPlayer self, out NetworkPlayer first, out NetworkPlayer second)
    {
        linkManager = LinkManager.Instance;
        self = GetNetworkPlayer();
        first = null;
        second = null;

        if (linkManager == null)
            return false;

        if (self == null)
            return false;

        if (!linkManager.TryGetRegisteredPlayers(out first, out second))
            return false;

        return linkManager.IsRegisteredPlayer(self);
    }

    private NetworkPlayer GetNetworkPlayer()
    {
        if (networkPlayer != null)
            return networkPlayer;

        TryGetComponent(out networkPlayer);
        return networkPlayer;
    }

    private Vector3 CalculateDesiredVelocity()
    {
        Vector3 direction = ResolveMoveDirection(Input);
        return direction * moveSpeed;
    }

    private float GetSoftStretchRatio(float distance, float slowdownStartDistance, float maxDistance)
    {
        if (maxDistance <= slowdownStartDistance)
            return distance >= maxDistance ? 1f : 0f;

        return Mathf.InverseLerp(slowdownStartDistance, maxDistance, Mathf.Min(distance, maxDistance));
    }

    private float GetOverstretchRatio(float distance, float maxDistance, float overstretchDistance)
    {
        if (distance <= maxDistance)
            return 0f;

        if (overstretchDistance <= 0f)
            return 1f;

        return Mathf.Clamp01((distance - maxDistance) / overstretchDistance);
    }

    private float GetRopePullSpeed(float ratio, float acceleration)
    {
        float pullRatio = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ratio));
        return acceleration * pullRatio * Time.fixedDeltaTime;
    }

    private Vector3 ResolveMoveDirection(Vector2 input)
    {
        Vector3 direction = new(input.x, 0f, input.y);
        direction.Normalize();

        return direction;
    }
}