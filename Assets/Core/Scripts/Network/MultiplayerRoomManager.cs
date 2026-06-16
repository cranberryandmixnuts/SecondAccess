using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Sirenix.OdinInspector;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkObject))]
public sealed class MultiplayerRoomManager : NetworkSingleton<MultiplayerRoomManager, SceneScope>
{
    [TabGroup("MultiplayerRoomManager", "Network"), SerializeField]
    private ushort port = 7777;

    [TabGroup("MultiplayerRoomManager", "Network"), SerializeField]
    private string hostListenAddress = "0.0.0.0";

    [TabGroup("MultiplayerRoomManager", "Network"), SerializeField]
    private string localFallbackAddress = "127.0.0.1";

    [TabGroup("MultiplayerRoomManager", "Network"), SerializeField]
    private int maxPlayers = 2;

    [TabGroup("MultiplayerRoomManager", "Spawn"), Required, SerializeField]
    private NetworkObject playerPrefab;

    [TabGroup("MultiplayerRoomManager", "Spawn"), Required, SerializeField]
    private Transform player1SpawnPoint;

    [TabGroup("MultiplayerRoomManager", "Spawn"), Required, SerializeField]
    private Transform player2SpawnPoint;

    [TabGroup("MultiplayerRoomManager", "Scenes"), SerializeField]
    private string failSceneName = "Fail";

    [TabGroup("MultiplayerRoomManager", "Scenes"), SerializeField]
    private string clearSceneName = "Clear";

    private readonly List<ulong> joinedClientIds = new();
    private readonly Dictionary<ulong, NetworkObject> playerObjectsByClientId = new();

    private bool callbacksBound;
    private bool gameStarted;
    private bool endingGame;
    private bool suppressDisconnectMenu;

    public bool IsGameStarted => gameStarted;
    public int ConnectedPlayerCount => joinedClientIds.Count;
    public string LocalRoomAddress => GetLocalIPv4Address();
    public string FailSceneName => failSceneName;
    public string ClearSceneName => clearSceneName;

    public event Action<string> StatusChanged;
    public event Action GameStarted;

    public bool StartHostRoom()
    {
        if (!CanStartNetwork())
            return false;

        ConfigureTransport(localFallbackAddress, hostListenAddress);
        BindNetworkCallbacks();
        ResetRoomState();
        endingGame = false;
        suppressDisconnectMenu = false;

        bool started = NetworkManager.Singleton.StartHost();

        if (!started)
        {
            StatusChanged?.Invoke("방 생성 실패");
            UnbindNetworkCallbacks();
            return false;
        }

        StatusChanged?.Invoke($"방 생성됨: {GetLocalIPv4Address()}");
        return true;
    }

    public bool StartClientRoom(string address)
    {
        if (!CanStartNetwork())
            return false;

        string targetAddress = string.IsNullOrWhiteSpace(address) ? localFallbackAddress : address.Trim();

        ConfigureTransport(targetAddress, null);
        BindNetworkCallbacks();
        ResetRoomState();
        endingGame = false;
        suppressDisconnectMenu = false;

        bool started = NetworkManager.Singleton.StartClient();

        if (!started)
        {
            StatusChanged?.Invoke("참가 실패");
            UnbindNetworkCallbacks();
            return false;
        }

        StatusChanged?.Invoke($"접속 시도 중: {targetAddress}");
        return true;
    }

    public void ShutdownRoom()
    {
        if (NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        ResetRoomState();
        endingGame = false;
        suppressDisconnectMenu = false;
        StatusChanged?.Invoke("네트워크 종료");
    }

    public bool TryEndGameWithFailScene() => TryEndGameToScene(failSceneName);

    public bool TryEndGameWithClearScene() => TryEndGameToScene(clearSceneName);

    public bool TryEndGameToScene(string sceneName)
    {
        if (!NetworkManager.Singleton.IsServer)
            return false;

        if (endingGame)
            return false;

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("[SecondAccess] End game scene name is empty.", this);
            return false;
        }

        endingGame = true;
        suppressDisconnectMenu = true;

        EndGameClientRpc(sceneName);
        StartCoroutine(EndGameHostRoutine(sceneName));
        return true;
    }

    protected override void NetworkSingletonOnDestroy()
    {
        UnbindNetworkCallbacks();
    }

    private bool CanStartNetwork()
    {
        if (!NetworkManager.Singleton.IsListening)
            return true;

        StatusChanged?.Invoke("이미 네트워크가 실행 중입니다.");
        return false;
    }

    private void ConfigureTransport(string address, string listenAddress)
    {
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetConnectionData(address, port, listenAddress);
    }

    private void BindNetworkCallbacks()
    {
        if (callbacksBound)
            return;

        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        NetworkManager.Singleton.ConnectionApprovalCallback = ApproveConnection;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        callbacksBound = true;
    }

    private void UnbindNetworkCallbacks()
    {
        if (!callbacksBound)
            return;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback = null;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        callbacksBound = false;
    }

    private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        bool approved = !gameStarted && NetworkManager.Singleton.ConnectedClientsIds.Count < maxPlayers;

        response.Approved = approved;
        response.CreatePlayerObject = false;
        response.Pending = false;
        response.Reason = approved ? string.Empty : "Room is full or already started.";
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            StatusChanged?.Invoke("서버 접속 완료");
            return;
        }

        if (!joinedClientIds.Contains(clientId))
            joinedClientIds.Add(clientId);

        StatusChanged?.Invoke($"플레이어 대기 중: {joinedClientIds.Count}/{maxPlayers}");
        TryStartGame();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton.IsServer)
        {
            joinedClientIds.Remove(clientId);
            RemoveSpawnedPlayer(clientId);

            if (!gameStarted)
                StatusChanged?.Invoke($"플레이어 대기 중: {joinedClientIds.Count}/{maxPlayers}");

            return;
        }

        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            if (suppressDisconnectMenu)
                return;

            StatusChanged?.Invoke("접속이 끊겼습니다.");
            MainNetworkMenuUI.Instance.ShowAfterDisconnect("접속이 끊겼습니다.");
        }
    }

    private void TryStartGame()
    {
        if (gameStarted)
            return;

        if (joinedClientIds.Count < maxPlayers)
            return;

        gameStarted = true;

        SpawnPlayers();
        StartGameClientRpc();

        StatusChanged?.Invoke("게임 시작");
    }

    private void SpawnPlayers()
    {
        Transform[] spawnPoints =
        {
            player1SpawnPoint,
            player2SpawnPoint
        };

        for (int i = 0; i < maxPlayers; i++)
        {
            ulong clientId = joinedClientIds[i];

            if (playerObjectsByClientId.ContainsKey(clientId))
                continue;

            NetworkObject playerObject = Instantiate(playerPrefab, spawnPoints[i].position, spawnPoints[i].rotation);
            playerObject.SpawnAsPlayerObject(clientId, true);

            playerObjectsByClientId.Add(clientId, playerObject);
        }
    }

    private void RemoveSpawnedPlayer(ulong clientId)
    {
        if (!playerObjectsByClientId.TryGetValue(clientId, out NetworkObject playerObject))
            return;

        if (playerObject.IsSpawned)
            playerObject.Despawn(true);

        playerObjectsByClientId.Remove(clientId);
    }

    private void ResetRoomState()
    {
        joinedClientIds.Clear();
        playerObjectsByClientId.Clear();
        gameStarted = false;
    }

    private string GetLocalIPv4Address()
    {
        IPHostEntry hostEntry = Dns.GetHostEntry(Dns.GetHostName());

        foreach (IPAddress address in hostEntry.AddressList)
        {
            if (address.AddressFamily != AddressFamily.InterNetwork)
                continue;

            if (IPAddress.IsLoopback(address))
                continue;

            return address.ToString();
        }

        return localFallbackAddress;
    }

    [ClientRpc]
    private void StartGameClientRpc()
    {
        gameStarted = true;

        MainNetworkMenuUI.Instance.HideForGameStart();
        GameStarted?.Invoke();
    }

    private IEnumerator EndGameHostRoutine(string sceneName)
    {
        yield return null;

        ShutdownAndLoadScene(sceneName);
    }

    private void ShutdownAndLoadScene(string sceneName)
    {
        if (NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        ResetRoomState();
        SceneManager.LoadScene(sceneName);
    }

    [Rpc(SendTo.NotServer)]
    private void EndGameClientRpc(string sceneName)
    {
        endingGame = true;
        suppressDisconnectMenu = true;

        ShutdownAndLoadScene(sceneName);
    }

}