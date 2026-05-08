using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
[RequireComponent(typeof(NetworkObject))]
public sealed class SecondAccessLinkManager : NetworkBehaviour
{
    public static SecondAccessLinkManager Instance { get; private set; }

    private const ulong MissingNetworkObjectId = ulong.MaxValue;

    [SerializeField, TitleGroup("Initial State")]
    private SecondAccessLinkMode initialMode = SecondAccessLinkMode.Rope;

    [SerializeField, MinValue(0.1f), TitleGroup("Rope")]
    private float ropeMaxDistance = 12f;

    [SerializeField, MinValue(0f), TitleGroup("Rope")]
    private float ropeSoftDistance = 8f;

    [SerializeField, MinValue(0f), TitleGroup("Rope")]
    private float ropePullAcceleration = 40f;

    [SerializeField, MinValue(0.1f), TitleGroup("Energy")]
    private float energyMaxDistance = 8f;

    [SerializeField, TitleGroup("Debug")]
    private bool logRejectedConversion = true;

    public NetworkVariable<SecondAccessLinkMode> Mode { get; } = new(SecondAccessLinkMode.Rope, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> FirstPlayerObjectId { get; } = new(MissingNetworkObjectId, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> SecondPlayerObjectId { get; } = new(MissingNetworkObjectId, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> RelayObjectId { get; } = new(MissingNetworkObjectId, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public float RopeMaxDistance => ropeMaxDistance;
    public float EnergyMaxDistance => energyMaxDistance;
    public bool HasRelayConnection => RelayObjectId.Value != MissingNetworkObjectId;
    public bool HasTwoPlayers => FirstPlayerObjectId.Value != MissingNetworkObjectId && SecondPlayerObjectId.Value != MissingNetworkObjectId;

    private bool energyGameOverLogged;

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        Mode.Value = initialMode;
        energyGameOverLogged = false;
    }

    private void FixedUpdate()
    {
        if (!IsServer)
            return;

        if (!TryGetPlayerObjects(out SecondAccessNetworkPlayer first, out SecondAccessNetworkPlayer second))
            return;

        if (Mode.Value == SecondAccessLinkMode.Rope)
        {
            energyGameOverLogged = false;
            ApplyRopeConstraint(first, second);
            return;
        }

        EvaluateEnergyDistance(first, second);
    }

    public void RegisterPlayer(SecondAccessNetworkPlayer player)
    {
        if (!IsServer)
            return;

        if (FirstPlayerObjectId.Value == player.NetworkObjectId || SecondPlayerObjectId.Value == player.NetworkObjectId)
            return;

        if (FirstPlayerObjectId.Value == MissingNetworkObjectId)
        {
            FirstPlayerObjectId.Value = player.NetworkObjectId;
            player.SetPlayerSlot(0);
            return;
        }

        if (SecondPlayerObjectId.Value == MissingNetworkObjectId)
        {
            SecondPlayerObjectId.Value = player.NetworkObjectId;
            player.SetPlayerSlot(1);
            return;
        }

        Debug.LogWarning($"[SecondAccess] Player limit exceeded. Disconnecting client {player.OwnerClientId}.");
        NetworkManager.Singleton.DisconnectClient(player.OwnerClientId);
    }

    public void UnregisterPlayer(SecondAccessNetworkPlayer player)
    {
        if (!IsServer)
            return;

        if (FirstPlayerObjectId.Value == player.NetworkObjectId)
            FirstPlayerObjectId.Value = MissingNetworkObjectId;

        if (SecondPlayerObjectId.Value == player.NetworkObjectId)
            SecondPlayerObjectId.Value = MissingNetworkObjectId;
    }

    public bool CanConvertTo(SecondAccessLinkMode targetMode)
    {
        if (!HasTwoPlayers)
            return false;

        if (Mode.Value == targetMode)
            return false;

        switch (targetMode)
        {
            case SecondAccessLinkMode.Rope:
                return !HasRelayConnection;

            case SecondAccessLinkMode.Energy:
                return GetDirectDistance() <= energyMaxDistance;

            default:
                return false;
        }
    }

    public bool TrySetMode(SecondAccessLinkMode targetMode)
    {
        if (!IsServer)
            return false;

        if (!CanConvertTo(targetMode))
        {
            if (logRejectedConversion)
                Debug.Log($"[SecondAccess] Link mode conversion rejected. Current={Mode.Value}, Target={targetMode}, Distance={GetDirectDistance():0.00}, Relay={HasRelayConnection}");

            return false;
        }

        Mode.Value = targetMode;
        energyGameOverLogged = false;
        Debug.Log($"[SecondAccess] Link mode changed: {targetMode}");
        return true;
    }

    public bool TryGetDirectLine(out Vector3 start, out Vector3 end)
    {
        start = Vector3.zero;
        end = Vector3.zero;

        if (!TryGetPlayerObjects(out SecondAccessNetworkPlayer first, out SecondAccessNetworkPlayer second))
            return false;

        start = first.transform.position;
        end = second.transform.position;
        return true;
    }

    public float GetDirectDistance()
    {
        if (!TryGetDirectLine(out Vector3 start, out Vector3 end))
            return float.PositiveInfinity;

        return Vector3.Distance(start, end);
    }

    private bool TryGetPlayerObjects(out SecondAccessNetworkPlayer first, out SecondAccessNetworkPlayer second)
    {
        bool hasFirst = TryGetNetworkPlayer(FirstPlayerObjectId.Value, out first);
        bool hasSecond = TryGetNetworkPlayer(SecondPlayerObjectId.Value, out second);
        return hasFirst && hasSecond;
    }

    private bool TryGetNetworkPlayer(ulong objectId, out SecondAccessNetworkPlayer player)
    {
        player = null;

        if (objectId == MissingNetworkObjectId)
            return false;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(objectId, out NetworkObject networkObject))
            return false;

        return networkObject.TryGetComponent(out player);
    }

    private void ApplyRopeConstraint(SecondAccessNetworkPlayer first, SecondAccessNetworkPlayer second)
    {
        Rigidbody firstBody = first.Body;
        Rigidbody secondBody = second.Body;

        Vector3 delta = secondBody.position - firstBody.position;
        delta.y = 0f;

        float distance = delta.magnitude;

        if (distance <= 0.0001f)
            return;

        Vector3 direction = delta / distance;
        float softDistance = Mathf.Min(ropeSoftDistance, ropeMaxDistance);

        if (distance > softDistance)
            ApplyRopePull(firstBody, secondBody, direction, distance, softDistance);

        if (distance <= ropeMaxDistance)
            return;

        float excess = distance - ropeMaxDistance;
        Vector3 correction = direction * (excess * 0.5f);

        firstBody.MovePosition(firstBody.position + correction);
        secondBody.MovePosition(secondBody.position - correction);

        RemoveSeparatingVelocity(firstBody, secondBody, direction);
    }

    private void ApplyRopePull(Rigidbody firstBody, Rigidbody secondBody, Vector3 direction, float distance, float softDistance)
    {
        float t = Mathf.InverseLerp(softDistance, ropeMaxDistance, Mathf.Min(distance, ropeMaxDistance));
        Vector3 pullVelocity = direction * ropePullAcceleration * t * Time.fixedDeltaTime;

        firstBody.linearVelocity = AddPlanarVelocity(firstBody.linearVelocity, pullVelocity);
        secondBody.linearVelocity = AddPlanarVelocity(secondBody.linearVelocity, -pullVelocity);
    }

    private void RemoveSeparatingVelocity(Rigidbody firstBody, Rigidbody secondBody, Vector3 direction)
    {
        Vector3 relativeVelocity = secondBody.linearVelocity - firstBody.linearVelocity;
        relativeVelocity.y = 0f;

        float separatingSpeed = Vector3.Dot(relativeVelocity, direction);

        if (separatingSpeed <= 0f)
            return;

        Vector3 cancellation = direction * (separatingSpeed * 0.5f);

        firstBody.linearVelocity = AddPlanarVelocity(firstBody.linearVelocity, cancellation);
        secondBody.linearVelocity = AddPlanarVelocity(secondBody.linearVelocity, -cancellation);
    }

    private Vector3 AddPlanarVelocity(Vector3 velocity, Vector3 delta) => new(velocity.x + delta.x, velocity.y, velocity.z + delta.z);

    private void EvaluateEnergyDistance(SecondAccessNetworkPlayer first, SecondAccessNetworkPlayer second)
    {
        float distance = Vector3.Distance(first.transform.position, second.transform.position);

        if (distance <= energyMaxDistance)
            return;

        if (energyGameOverLogged)
            return;

        energyGameOverLogged = true;

        Debug.Log($"[SecondAccess] Energy link exceeded max distance. GameOver placeholder. Distance={distance:0.00}, Max={energyMaxDistance:0.00}");
        ReportEnergyGameOverRpc(distance, energyMaxDistance);
    }

    [Rpc(SendTo.NotServer)]
    private void ReportEnergyGameOverRpc(float distance, float maxDistance)
    {
        Debug.Log($"[SecondAccess] Energy link exceeded max distance. GameOver placeholder. Distance={distance:0.00}, Max={maxDistance:0.00}");
    }
}
