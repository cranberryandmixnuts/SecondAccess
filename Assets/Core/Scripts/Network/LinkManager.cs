using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(10000)]
[RequireComponent(typeof(NetworkObject))]
public sealed class LinkManager : NetworkBehaviour
{
    public static LinkManager Instance { get; private set; }

    private const ulong MissingNetworkObjectId = ulong.MaxValue;

    [SerializeField, TitleGroup("Initial State")]
    private LinkMode initialMode = LinkMode.Rope;

    [SerializeField, MinValue(0.1f), TitleGroup("Rope")]
    private float ropeMaxDistance = 12f;

    [SerializeField, MinValue(0f), TitleGroup("Rope")]
    private float ropeSoftDistance = 8f;

    [SerializeField, Range(0f, 1f), TitleGroup("Rope")]
    private float minimumOutwardSpeedRatio = 0.2f;

    [SerializeField, Range(0f, 1f), TitleGroup("Rope")]
    private float ropeDragTransferRatio = 0.65f;

    [SerializeField, MinValue(0f), TitleGroup("Rope")]
    private float ropePullAcceleration = 20f;

    [SerializeField, MinValue(0f), TitleGroup("Rope")]
    private float ropeOverstretchDistance = 2f;

    [SerializeField, MinValue(0f), TitleGroup("Rope")]
    private float ropeOverstretchPullAcceleration = 80f;

    [SerializeField, MinValue(0f), TitleGroup("Rope")]
    private float ropeHardCorrectionSpeed = 8f;

    [SerializeField, MinValue(0.1f), TitleGroup("Energy")]
    private float energyMaxDistance = 8f;

    [SerializeField, TitleGroup("Debug")]
    private bool logRejectedConversion = true;

    public NetworkVariable<LinkMode> Mode { get; } = new(LinkMode.Rope, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> FirstPlayerObjectId { get; } = new(MissingNetworkObjectId, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> SecondPlayerObjectId { get; } = new(MissingNetworkObjectId, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> RelayObjectId { get; } = new(MissingNetworkObjectId, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public float RopeMaxDistance => ropeMaxDistance;
    public float EnergyMaxDistance => energyMaxDistance;
    public bool HasRelayConnection => RelayObjectId.Value != MissingNetworkObjectId;
    public bool HasTwoPlayers => FirstPlayerObjectId.Value != MissingNetworkObjectId && SecondPlayerObjectId.Value != MissingNetworkObjectId;
    public int RegisteredPlayerCount => GetRegisteredPlayerCount();

    private bool energyGameOverLogged;

    private void Awake() => Instance = this;

    private new void OnDestroy()
    {
        base.OnDestroy();

        if (Instance == this)
            Instance = null;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        Mode.Value = initialMode;
        energyGameOverLogged = false;
        RegisterExistingPlayers();
    }

    private void FixedUpdate()
    {
        if (!IsServer)
            return;

        RegisterExistingPlayers();

        if (!TryGetPlayerObjects(out NetworkPlayer first, out NetworkPlayer second))
            return;

        if (Mode.Value == LinkMode.Rope)
        {
            energyGameOverLogged = false;
            ApplyRopeConstraint(first, second);
            return;
        }

        EvaluateEnergyDistance(first, second);
    }

    public void RegisterPlayer(NetworkPlayer player)
    {
        if (!IsServer)
            return;

        ValidateRegisteredPlayers();

        if (FirstPlayerObjectId.Value == player.NetworkObjectId || SecondPlayerObjectId.Value == player.NetworkObjectId)
            return;

        if (FirstPlayerObjectId.Value == MissingNetworkObjectId)
        {
            FirstPlayerObjectId.Value = player.NetworkObjectId;
            player.SetPlayerSlot(0);
            Debug.Log($"[SecondAccess] First player registered. ObjectId={player.NetworkObjectId}", this);
            return;
        }

        if (SecondPlayerObjectId.Value == MissingNetworkObjectId)
        {
            SecondPlayerObjectId.Value = player.NetworkObjectId;
            player.SetPlayerSlot(1);
            Debug.Log($"[SecondAccess] Second player registered. ObjectId={player.NetworkObjectId}", this);
            return;
        }

        Debug.LogWarning($"[SecondAccess] Player limit exceeded. Disconnecting client {player.OwnerClientId}.");
        NetworkManager.Singleton.DisconnectClient(player.OwnerClientId);
    }

    public void UnregisterPlayer(NetworkPlayer player)
    {
        if (!IsServer)
            return;

        if (FirstPlayerObjectId.Value == player.NetworkObjectId)
            FirstPlayerObjectId.Value = MissingNetworkObjectId;

        if (SecondPlayerObjectId.Value == player.NetworkObjectId)
            SecondPlayerObjectId.Value = MissingNetworkObjectId;
    }

    public bool CanConvertTo(LinkMode targetMode)
    {
        if (!HasTwoPlayers)
            return false;

        if (Mode.Value == targetMode)
            return false;

        return targetMode switch
        {
            LinkMode.Rope => !HasRelayConnection,
            LinkMode.Energy => GetDirectDistance() <= energyMaxDistance,
            _ => false,
        };
    }

    public bool TrySetMode(LinkMode targetMode)
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

        if (TryGetPlayerObjects(out NetworkPlayer first, out NetworkPlayer second))
        {
            start = first.transform.position;
            end = second.transform.position;
            return true;
        }

        if (TryGetScenePlayerObjects(out first, out second))
        {
            start = first.transform.position;
            end = second.transform.position;
            return true;
        }

        return false;
    }

    public float GetDirectDistance()
    {
        if (!TryGetDirectLine(out Vector3 start, out Vector3 end))
            return float.PositiveInfinity;

        return Vector3.Distance(start, end);
    }

    private void RegisterExistingPlayers()
    {
        ValidateRegisteredPlayers();

        if (HasTwoPlayers)
            return;

        NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID);

        for (int i = 0; i < players.Length; i++)
        {
            if (!players[i].IsSpawned)
                continue;

            RegisterPlayer(players[i]);

            if (HasTwoPlayers)
                return;
        }
    }

    private void ValidateRegisteredPlayers()
    {
        if (!IsRegisteredObjectAlive(FirstPlayerObjectId.Value))
            FirstPlayerObjectId.Value = MissingNetworkObjectId;

        if (!IsRegisteredObjectAlive(SecondPlayerObjectId.Value))
            SecondPlayerObjectId.Value = MissingNetworkObjectId;
    }

    private bool IsRegisteredObjectAlive(ulong objectId)
    {
        if (objectId == MissingNetworkObjectId)
            return false;

        if (NetworkManager.Singleton == null)
            return false;

        return NetworkManager.Singleton.SpawnManager.SpawnedObjects.ContainsKey(objectId);
    }

    private bool TryGetPlayerObjects(out NetworkPlayer first, out NetworkPlayer second)
    {
        bool hasFirst = TryGetNetworkPlayer(FirstPlayerObjectId.Value, out first);
        bool hasSecond = TryGetNetworkPlayer(SecondPlayerObjectId.Value, out second);
        return hasFirst && hasSecond;
    }

    private bool TryGetNetworkPlayer(ulong objectId, out NetworkPlayer player)
    {
        player = null;

        if (objectId == MissingNetworkObjectId)
            return false;

        if (NetworkManager.Singleton == null)
            return false;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(objectId, out NetworkObject networkObject))
            return false;

        return networkObject.TryGetComponent(out player);
    }

    private bool TryGetScenePlayerObjects(out NetworkPlayer first, out NetworkPlayer second)
    {
        first = null;
        second = null;

        NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID);

        for (int i = 0; i < players.Length; i++)
        {
            if (!players[i].IsSpawned)
                continue;

            if (first == null)
            {
                first = players[i];
                continue;
            }

            second = players[i];
            return true;
        }

        return false;
    }

    private int GetRegisteredPlayerCount()
    {
        int count = 0;

        if (FirstPlayerObjectId.Value != MissingNetworkObjectId)
            count++;

        if (SecondPlayerObjectId.Value != MissingNetworkObjectId)
            count++;

        return count;
    }

    private void ApplyRopeConstraint(NetworkPlayer first, NetworkPlayer second)
    {
        Rigidbody firstBody = first.Body;
        Rigidbody secondBody = second.Body;

        Vector3 delta = secondBody.position - firstBody.position;
        delta.y = 0f;

        float distance = delta.magnitude;

        if (distance <= 0.0001f)
            return;

        Vector3 direction = delta / distance;
        float slowdownStartDistance = Mathf.Min(ropeSoftDistance, ropeMaxDistance);
        float stretchRatio = GetStretchRatio(distance, slowdownStartDistance);

        if (stretchRatio > 0f)
            ApplyOutwardMovementResistance(firstBody, secondBody, direction, stretchRatio);

        if (distance > slowdownStartDistance)
            ApplyRopePull(firstBody, secondBody, direction, distance, slowdownStartDistance);

        if (distance > ropeMaxDistance)
            ApplyOverstretchRecovery(firstBody, secondBody, direction, distance);

        ApplyHardLimitCorrection(firstBody, secondBody, direction, distance);
    }

    private float GetStretchRatio(float distance, float slowdownStartDistance)
    {
        if (ropeMaxDistance <= slowdownStartDistance)
            return distance >= ropeMaxDistance ? 1f : 0f;

        return Mathf.InverseLerp(slowdownStartDistance, ropeMaxDistance, Mathf.Min(distance, ropeMaxDistance));
    }

    private void ApplyOutwardMovementResistance(Rigidbody firstBody, Rigidbody secondBody, Vector3 direction, float stretchRatio)
    {
        Vector3 firstVelocity = GetPlanarVelocity(firstBody);
        Vector3 secondVelocity = GetPlanarVelocity(secondBody);
        Vector3 nextFirstVelocity = firstVelocity;
        Vector3 nextSecondVelocity = secondVelocity;

        float speedRatio = Mathf.Lerp(1f, minimumOutwardSpeedRatio, stretchRatio);

        float firstOutwardSpeed = Vector3.Dot(firstVelocity, -direction);

        if (firstOutwardSpeed > 0f)
        {
            float removedSpeed = firstOutwardSpeed * (1f - speedRatio);
            Vector3 removedVelocity = -direction * removedSpeed;

            nextFirstVelocity -= removedVelocity;
            nextSecondVelocity += removedVelocity * ropeDragTransferRatio;
        }

        float secondOutwardSpeed = Vector3.Dot(secondVelocity, direction);

        if (secondOutwardSpeed > 0f)
        {
            float removedSpeed = secondOutwardSpeed * (1f - speedRatio);
            Vector3 removedVelocity = direction * removedSpeed;

            nextSecondVelocity -= removedVelocity;
            nextFirstVelocity += removedVelocity * ropeDragTransferRatio;
        }

        SetPlanarVelocity(firstBody, nextFirstVelocity);
        SetPlanarVelocity(secondBody, nextSecondVelocity);
    }

    private void ApplyRopePull(Rigidbody firstBody, Rigidbody secondBody, Vector3 direction, float distance, float slowdownStartDistance)
    {
        float pullRatio = GetStretchRatio(distance, slowdownStartDistance);
        float pullSpeed = ropePullAcceleration * pullRatio * Time.fixedDeltaTime;
        Vector3 pullVelocity = direction * pullSpeed;

        firstBody.linearVelocity = AddPlanarVelocity(firstBody.linearVelocity, pullVelocity);
        secondBody.linearVelocity = AddPlanarVelocity(secondBody.linearVelocity, -pullVelocity);
    }

    private void ApplyOverstretchRecovery(Rigidbody firstBody, Rigidbody secondBody, Vector3 direction, float distance)
    {
        if (ropeOverstretchDistance <= 0f)
            return;

        float overstretch = distance - ropeMaxDistance;
        float recoveryRatio = Mathf.Clamp01(overstretch / ropeOverstretchDistance);
        float recoverySpeed = ropeOverstretchPullAcceleration * recoveryRatio * Time.fixedDeltaTime;
        Vector3 recoveryVelocity = direction * recoverySpeed;

        firstBody.linearVelocity = AddPlanarVelocity(firstBody.linearVelocity, recoveryVelocity);
        secondBody.linearVelocity = AddPlanarVelocity(secondBody.linearVelocity, -recoveryVelocity);
    }

    private void ApplyHardLimitCorrection(Rigidbody firstBody, Rigidbody secondBody, Vector3 direction, float distance)
    {
        if (ropeOverstretchDistance <= 0f)
            return;

        float hardLimitDistance = ropeMaxDistance + ropeOverstretchDistance;

        if (distance <= hardLimitDistance)
            return;

        float excess = distance - hardLimitDistance;
        float correction = Mathf.Min(excess * 0.5f, ropeHardCorrectionSpeed * Time.fixedDeltaTime);
        Vector3 correctionOffset = direction * correction;

        firstBody.MovePosition(firstBody.position + correctionOffset);
        secondBody.MovePosition(secondBody.position - correctionOffset);

        RemoveSeparatingVelocity(firstBody, secondBody, direction);
    }

    private void RemoveSeparatingVelocity(Rigidbody firstBody, Rigidbody secondBody, Vector3 direction)
    {
        Vector3 firstVelocity = GetPlanarVelocity(firstBody);
        Vector3 secondVelocity = GetPlanarVelocity(secondBody);
        Vector3 relativeVelocity = secondVelocity - firstVelocity;

        float separatingSpeed = Vector3.Dot(relativeVelocity, direction);

        if (separatingSpeed <= 0f)
            return;

        Vector3 cancellation = direction * (separatingSpeed * 0.5f);

        SetPlanarVelocity(firstBody, firstVelocity + cancellation);
        SetPlanarVelocity(secondBody, secondVelocity - cancellation);
    }

    private Vector3 GetPlanarVelocity(Rigidbody body) => new(body.linearVelocity.x, 0f, body.linearVelocity.z);

    private void SetPlanarVelocity(Rigidbody body, Vector3 velocity) => body.linearVelocity = new Vector3(velocity.x, body.linearVelocity.y, velocity.z);

    private Vector3 AddPlanarVelocity(Vector3 velocity, Vector3 delta) => new(velocity.x + delta.x, velocity.y, velocity.z + delta.z);

    private void EvaluateEnergyDistance(NetworkPlayer first, NetworkPlayer second)
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