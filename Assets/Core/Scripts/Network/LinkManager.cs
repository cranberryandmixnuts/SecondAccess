using System;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

public enum LinkMode : byte
{
    Rope,
    Energy
}

[RequireComponent(typeof(NetworkObject))]
public sealed class LinkManager : NetworkSingleton<LinkManager, SceneScope>
{
    private const ulong MissingNetworkObjectId = ulong.MaxValue;

    [SerializeField] private LinkMode initialMode = LinkMode.Rope;

    [Header("Rope Link Settings")]
    [SerializeField, MinValue(0.1f)] private float ropeMaxDistance = 16f;
    [SerializeField, MinValue(0f)] private float ropeSoftDistance = 8f;
    [SerializeField, Range(0f, 1f)] private float minimumOutwardSpeedRatio = 0.3f;
    [SerializeField, Range(0f, 1f)] private float ropeDragTransferRatio = 0.6f;
    [SerializeField, MinValue(0f)] private float ropePullAcceleration = 100f;
    [SerializeField, MinValue(0f)] private float ropeOverstretchDistance = 3f;
    [SerializeField, MinValue(0f)] private float ropeOverstretchPullAcceleration = 160f;
    [SerializeField, MinValue(0f)] private float ropeHardCorrectionSpeed = 10f;

    [Header("Energy Link Settings")]
    [SerializeField, MinValue(0.1f)] private float energyMaxDistance = 10f;

    public NetworkVariable<LinkMode> Mode { get; } = new(LinkMode.Rope, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> FirstPlayerObjectId { get; } = new(MissingNetworkObjectId, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> SecondPlayerObjectId { get; } = new(MissingNetworkObjectId, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> RelayObjectId { get; } = new(MissingNetworkObjectId, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> EnergyLinkLaserized { get; } = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public float RopeMaxDistance => ropeMaxDistance;
    public float RopeSoftDistance => ropeSoftDistance;
    public float MinimumOutwardSpeedRatio => minimumOutwardSpeedRatio;
    public float RopeDragTransferRatio => ropeDragTransferRatio;
    public float RopePullAcceleration => ropePullAcceleration;
    public float RopeOverstretchDistance => ropeOverstretchDistance;
    public float RopeOverstretchPullAcceleration => ropeOverstretchPullAcceleration;
    public float RopeHardCorrectionSpeed => ropeHardCorrectionSpeed;
    public float EnergyMaxDistance => energyMaxDistance;
    public bool HasRelayConnection => RelayObjectId.Value != MissingNetworkObjectId;
    public bool IsEnergyLinkLaserized => EnergyLinkLaserized.Value;
    public bool HasTwoPlayers => FirstPlayerObjectId.Value != MissingNetworkObjectId && SecondPlayerObjectId.Value != MissingNetworkObjectId;
    public int RegisteredPlayerCount => GetRegisteredPlayerCount();

    private bool energyGameOverLogged;

    protected override void NetworkSingletonOnNetworkSpawn()
    {
        if (!IsServer)
            return;

        Mode.Value = initialMode;
        RelayObjectId.Value = MissingNetworkObjectId;
        EnergyLinkLaserized.Value = false;
        energyGameOverLogged = false;
        RegisterExistingPlayers();
    }

    private void FixedUpdate()
    {
        if (!IsServer)
            return;

        RegisterExistingPlayers();
        ValidateRegisteredObjects();

        if (Mode.Value == LinkMode.Energy && HasTwoPlayers)
            EvaluateEnergyDistance();
    }

    public void RegisterPlayer(NetworkPlayer player)
    {
        if (!IsServer)
            return;

        ValidateRegisteredObjects();

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
            Debug.Log($"[SecondAccess] Link mode conversion rejected. Current={Mode.Value}, Target={targetMode}, Distance={GetDirectDistance():0.00}, Relay={HasRelayConnection}");
            return false;
        }

        Mode.Value = targetMode;
        EnergyLinkLaserized.Value = false;
        energyGameOverLogged = false;
        Debug.Log($"[SecondAccess] Link mode changed: {targetMode}");
        return true;
    }

    public bool CanToggleRelay(NetworkObject relayObject)
    {
        if (!CanUseRelay(relayObject))
            return false;

        if (IsRelayConnectedTo(relayObject))
            return true;

        return AreRelaySegmentsWithinMax(relayObject.transform.position);
    }

    public bool TryToggleRelay(NetworkObject relayObject)
    {
        if (!IsServer)
            return false;

        if (!CanUseRelay(relayObject))
            return false;

        if (IsRelayConnectedTo(relayObject))
            return TryDisconnectRelay(relayObject);

        return TryConnectRelay(relayObject);
    }

    public bool TryConnectRelay(NetworkObject relayObject)
    {
        if (!IsServer)
            return false;

        if (!CanUseRelay(relayObject))
            return false;

        if (!AreRelaySegmentsWithinMax(relayObject.transform.position))
        {
            GetRelaySegmentDistances(relayObject.transform.position, out float firstDistance, out float secondDistance);
            Debug.Log($"[SecondAccess] Relay connection rejected. Relay={relayObject.NetworkObjectId}, First={firstDistance:0.00}, Second={secondDistance:0.00}, Max={energyMaxDistance:0.00}", relayObject);
            return false;
        }

        RelayObjectId.Value = relayObject.NetworkObjectId;
        energyGameOverLogged = false;
        Debug.Log($"[SecondAccess] Relay connected. Relay={relayObject.NetworkObjectId}", relayObject);
        return true;
    }

    public void SetEnergyLinkLaserized(bool laserized)
    {
        if (!IsServer)
            return;

        bool nextLaserized = laserized && Mode.Value == LinkMode.Energy && HasTwoPlayers;

        if (EnergyLinkLaserized.Value == nextLaserized)
            return;

        EnergyLinkLaserized.Value = nextLaserized;
    }

    public bool TryDisconnectRelay(NetworkObject relayObject)
    {
        if (!IsServer)
            return false;

        if (!IsRelayConnectedTo(relayObject))
            return false;

        RelayObjectId.Value = MissingNetworkObjectId;
        energyGameOverLogged = false;
        Debug.Log($"[SecondAccess] Relay disconnected. Relay={relayObject.NetworkObjectId}", relayObject);
        return true;
    }

    public bool IsRelayConnectedTo(NetworkObject relayObject)
    {
        if (relayObject == null)
            return false;

        return RelayObjectId.Value == relayObject.NetworkObjectId;
    }

    public bool TryGetRelay(out NetworkObject relayObject)
    {
        relayObject = null;

        if (!HasRelayConnection)
            return false;

        if (NetworkManager.Singleton == null)
            return false;

        return NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(RelayObjectId.Value, out relayObject);
    }

    public bool TryGetLinkPath(out Vector3 firstPosition, out Vector3 relayPosition, out Vector3 secondPosition, out bool usesRelay)
    {
        firstPosition = Vector3.zero;
        relayPosition = Vector3.zero;
        secondPosition = Vector3.zero;
        usesRelay = false;

        if (TryGetRegisteredPlayers(out NetworkPlayer first, out NetworkPlayer second))
        {
            firstPosition = first.transform.position;
            secondPosition = second.transform.position;

            if (Mode.Value == LinkMode.Energy && TryGetRelay(out NetworkObject relayObject))
            {
                relayPosition = relayObject.transform.position;
                usesRelay = true;
            }

            return true;
        }

        if (TryGetScenePlayerObjects(out first, out second))
        {
            firstPosition = first.transform.position;
            secondPosition = second.transform.position;
            return true;
        }

        return false;
    }

    public bool TryGetDirectLine(out Vector3 start, out Vector3 end)
    {
        start = Vector3.zero;
        end = Vector3.zero;

        if (TryGetRegisteredPlayers(out NetworkPlayer first, out NetworkPlayer second))
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

    public bool TryGetRegisteredPlayers(out NetworkPlayer first, out NetworkPlayer second)
    {
        bool hasFirst = TryGetNetworkPlayer(FirstPlayerObjectId.Value, out first);
        bool hasSecond = TryGetNetworkPlayer(SecondPlayerObjectId.Value, out second);
        return hasFirst && hasSecond;
    }

    public bool TryGetLinkedPlayer(NetworkPlayer player, out NetworkPlayer linkedPlayer)
    {
        linkedPlayer = null;

        if (!TryGetRegisteredPlayers(out NetworkPlayer first, out NetworkPlayer second))
            return false;

        if (player.NetworkObjectId == first.NetworkObjectId)
        {
            linkedPlayer = second;
            return true;
        }

        if (player.NetworkObjectId == second.NetworkObjectId)
        {
            linkedPlayer = first;
            return true;
        }

        return false;
    }

    public bool IsRegisteredPlayer(NetworkPlayer player)
    {
        if (player.NetworkObjectId == FirstPlayerObjectId.Value)
            return true;

        if (player.NetworkObjectId == SecondPlayerObjectId.Value)
            return true;

        return false;
    }

    public bool IsFirstRegisteredPlayer(NetworkPlayer player) => player.NetworkObjectId == FirstPlayerObjectId.Value;

    public void EvaluateEnergyDistance()
    {
        if (!IsServer)
            return;

        if (!TryGetRegisteredPlayers(out NetworkPlayer first, out NetworkPlayer second))
            return;

        EvaluateEnergyDistance(first, second);
    }

    private bool CanUseRelay(NetworkObject relayObject)
    {
        if (!HasTwoPlayers)
            return false;

        if (Mode.Value != LinkMode.Energy)
            return false;

        if (relayObject == null)
            return false;

        return relayObject.IsSpawned;
    }

    private bool AreRelaySegmentsWithinMax(Vector3 relayPosition)
    {
        GetRelaySegmentDistances(relayPosition, out float firstDistance, out float secondDistance);
        return firstDistance <= energyMaxDistance && secondDistance <= energyMaxDistance;
    }

    private void GetRelaySegmentDistances(Vector3 relayPosition, out float firstDistance, out float secondDistance)
    {
        firstDistance = float.PositiveInfinity;
        secondDistance = float.PositiveInfinity;

        if (!TryGetRegisteredPlayers(out NetworkPlayer first, out NetworkPlayer second))
            return;

        firstDistance = Vector3.Distance(first.transform.position, relayPosition);
        secondDistance = Vector3.Distance(relayPosition, second.transform.position);
    }

    private void RegisterExistingPlayers()
    {
        ValidateRegisteredObjects();

        if (HasTwoPlayers)
            return;

        NetworkPlayer[] players = FindSpawnedPlayers();

        for (int i = 0; i < players.Length; i++)
        {
            RegisterPlayer(players[i]);

            if (HasTwoPlayers)
                return;
        }
    }

    private void ValidateRegisteredObjects()
    {
        if (!IsRegisteredObjectAlive(FirstPlayerObjectId.Value))
            FirstPlayerObjectId.Value = MissingNetworkObjectId;

        if (!IsRegisteredObjectAlive(SecondPlayerObjectId.Value))
            SecondPlayerObjectId.Value = MissingNetworkObjectId;

        if (HasRelayConnection && !IsRegisteredObjectAlive(RelayObjectId.Value))
            RelayObjectId.Value = MissingNetworkObjectId;

        if (Mode.Value != LinkMode.Energy || !HasTwoPlayers)
            EnergyLinkLaserized.Value = false;
    }

    private bool IsRegisteredObjectAlive(ulong objectId)
    {
        if (objectId == MissingNetworkObjectId)
            return false;

        if (NetworkManager.Singleton == null)
            return false;

        return NetworkManager.Singleton.SpawnManager.SpawnedObjects.ContainsKey(objectId);
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

        NetworkPlayer[] players = FindSpawnedPlayers();

        if (players.Length < 2)
            return false;

        first = players[0];
        second = players[1];
        return true;
    }

    private NetworkPlayer[] FindSpawnedPlayers()
    {
        NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsInactive.Exclude);
        int spawnedCount = 0;

        for (int i = 0; i < players.Length; i++)
        {
            if (!players[i].IsSpawned)
                continue;

            players[spawnedCount] = players[i];
            spawnedCount++;
        }

        if (spawnedCount != players.Length)
            Array.Resize(ref players, spawnedCount);

        Array.Sort(players, CompareNetworkPlayersByNetworkObjectId);
        return players;
    }

    private int CompareNetworkPlayersByNetworkObjectId(NetworkPlayer x, NetworkPlayer y) => x.NetworkObjectId.CompareTo(y.NetworkObjectId);

    private int GetRegisteredPlayerCount()
    {
        int count = 0;

        if (FirstPlayerObjectId.Value != MissingNetworkObjectId)
            count++;

        if (SecondPlayerObjectId.Value != MissingNetworkObjectId)
            count++;

        return count;
    }

    private void EvaluateEnergyDistance(NetworkPlayer first, NetworkPlayer second)
    {
        if (HasRelayConnection && TryGetRelay(out NetworkObject relayObject))
        {
            EvaluateRelayEnergyDistance(first, second, relayObject.transform.position);
            return;
        }

        float distance = Vector3.Distance(first.transform.position, second.transform.position);

        if (distance <= energyMaxDistance)
            return;

        if (energyGameOverLogged)
            return;

        energyGameOverLogged = true;

        Debug.Log($"Energy link exceeded max distance. Distance={distance:0.00}, Max={energyMaxDistance:0.00}");
        RequestFailScene();
    }

    private void EvaluateRelayEnergyDistance(NetworkPlayer first, NetworkPlayer second, Vector3 relayPosition)
    {
        float firstDistance = Vector3.Distance(first.transform.position, relayPosition);
        float secondDistance = Vector3.Distance(relayPosition, second.transform.position);

        if (firstDistance <= energyMaxDistance && secondDistance <= energyMaxDistance)
            return;

        if (energyGameOverLogged)
            return;

        energyGameOverLogged = true;

        Debug.Log($"Energy relay link exceeded max segment distance. First={firstDistance:0.00}, Second={secondDistance:0.00}, Max={energyMaxDistance:0.00}");
        RequestFailScene();
    }

    private void RequestFailScene()
    {
        if (!MultiplayerRoomManager.HasInstance)
        {
            Debug.LogError("[SecondAccess] Energy link failed, but MultiplayerRoomManager does not exist.", this);
            return;
        }

        MultiplayerRoomManager.Instance.TryEndGameWithFailScene();
    }
}
