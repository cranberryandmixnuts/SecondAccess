using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sirenix.OdinInspector;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

[DefaultExecutionOrder(-20000)]
public sealed class MultiplayerLobbyManager : Singleton<MultiplayerLobbyManager, GlobalScope>
{
    [Header("Match")]
    [SerializeField, MinValue(2)] private int maxPlayers = 2;
    [SerializeField] private SceneType gameScene = SceneType.None;

    [Header("Relay")]
    [SerializeField] private string relayConnectionType = "dtls";

    private readonly HashSet<ulong> synchronizedClientIds = new();

    private bool callbacksRegistered;
    private bool isGameStarting;
    private Coroutine beginGameRoutine;

    public string JoinCode { get; private set; }
    public string CurrentStatus { get; private set; } = "대기 중";
    public bool IsBusy { get; private set; }
    public int MaxPlayers => maxPlayers;
    public int ConnectedPlayerCount => NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsIds.Count : 0;
    public bool IsConnected => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    public event Action<string> JoinCodeChanged;
    public event Action<int, int> PlayerCountChanged;
    public event Action<string> StatusChanged;
    public event Action StateChanged;

    protected override void SingletonOnDestroy()
    {
        UnregisterNetworkCallbacks();
    }

    public async void CreateRoom() => await CreateRoomAsync();

    public async void JoinRoom(string joinCode) => await JoinRoomAsync(joinCode);

    public async Task<bool> CreateRoomAsync()
    {
        if (!CanStartNewConnection())
            return false;

        SetBusy(true);
        SetStatus("서비스 로그인 중...");

        try
        {
            await EnsureUnityServicesAsync();

            NetworkManager networkManager = NetworkManager.Singleton;
            UnityTransport transport = networkManager.GetComponent<UnityTransport>();

            PrepareNetworkManager(networkManager);

            SetStatus("Relay 방 생성 중...");

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            JoinCode = (await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId)).ToUpperInvariant();
            JoinCodeChanged?.Invoke(JoinCode);

            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, relayConnectionType));

            if (!networkManager.StartHost())
            {
                JoinCode = null;
                JoinCodeChanged?.Invoke(JoinCode);
                SetStatus("호스트 시작 실패");
                return false;
            }

            synchronizedClientIds.Add(networkManager.ServerClientId);
            RaisePlayerCountChanged();
            SetStatus($"방 생성 완료. 참가 코드: {JoinCode}");
            TryScheduleBeginGame();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            Shutdown();
            SetStatus($"방 생성 실패: {exception.Message}");
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    public async Task<bool> JoinRoomAsync(string joinCode)
    {
        string normalizedJoinCode = NormalizeJoinCode(joinCode);

        if (string.IsNullOrWhiteSpace(normalizedJoinCode))
        {
            SetStatus("참가 코드를 입력해야 합니다.");
            return false;
        }

        if (!CanStartNewConnection())
            return false;

        SetBusy(true);
        SetStatus("서비스 로그인 중...");

        try
        {
            await EnsureUnityServicesAsync();

            NetworkManager networkManager = NetworkManager.Singleton;
            UnityTransport transport = networkManager.GetComponent<UnityTransport>();

            PrepareNetworkManager(networkManager);

            SetStatus("Relay 방 참가 중...");

            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(normalizedJoinCode);
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, relayConnectionType));

            if (!networkManager.StartClient())
            {
                SetStatus("클라이언트 시작 실패");
                return false;
            }

            SetStatus("방 접속 요청 중...");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            Shutdown();
            SetStatus($"방 참가 실패: {exception.Message}");
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    public void Shutdown()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (beginGameRoutine != null)
        {
            StopCoroutine(beginGameRoutine);
            beginGameRoutine = null;
        }

        if (networkManager != null && networkManager.IsListening)
            networkManager.Shutdown();

        JoinCode = null;
        isGameStarting = false;
        synchronizedClientIds.Clear();

        JoinCodeChanged?.Invoke(JoinCode);
        RaisePlayerCountChanged();
        StateChanged?.Invoke();
    }

    private async Task EnsureUnityServicesAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private void PrepareNetworkManager(NetworkManager networkManager)
    {
        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.ConnectionApprovalCallback -= ApprovalCheck;
        networkManager.ConnectionApprovalCallback += ApprovalCheck;
        RegisterNetworkCallbacks(networkManager);
    }

    private void RegisterNetworkCallbacks(NetworkManager networkManager)
    {
        if (callbacksRegistered)
            return;

        networkManager.OnClientConnectedCallback += OnClientConnected;
        networkManager.OnClientDisconnectCallback += OnClientDisconnected;

        if (networkManager.SceneManager != null)
            networkManager.SceneManager.OnSceneEvent += OnSceneEvent;

        callbacksRegistered = true;
    }

    private void UnregisterNetworkCallbacks()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null || !callbacksRegistered)
            return;

        networkManager.OnClientConnectedCallback -= OnClientConnected;
        networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        networkManager.ConnectionApprovalCallback -= ApprovalCheck;

        if (networkManager.SceneManager != null)
            networkManager.SceneManager.OnSceneEvent -= OnSceneEvent;

        callbacksRegistered = false;
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        bool isFull = networkManager.ConnectedClientsIds.Count >= maxPlayers;
        bool canJoin = !isFull && !isGameStarting;

        response.Approved = canJoin;
        response.CreatePlayerObject = false;
        response.PlayerPrefabHash = null;
        response.Position = Vector3.zero;
        response.Rotation = Quaternion.identity;
        response.Reason = GetDenyReason(isFull);
        response.Pending = false;
    }

    private string GetDenyReason(bool isFull)
    {
        if (isFull)
            return "방이 가득 찼습니다.";

        if (isGameStarting)
            return "이미 게임이 시작됐습니다.";

        return string.Empty;
    }

    private void OnClientConnected(ulong clientId)
    {
        RaisePlayerCountChanged();

        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager.IsServer)
        {
            if (clientId == networkManager.ServerClientId)
                synchronizedClientIds.Add(clientId);

            SetStatus($"플레이어 대기 중... {networkManager.ConnectedClientsIds.Count}/{maxPlayers}");
            TryScheduleBeginGame();
            return;
        }

        if (clientId == networkManager.LocalClientId)
            SetStatus("방 접속 완료. 씬 동기화 중...");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        synchronizedClientIds.Remove(clientId);
        RaisePlayerCountChanged();

        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
            return;

        if (beginGameRoutine != null && networkManager.IsServer && !isGameStarting)
        {
            StopCoroutine(beginGameRoutine);
            beginGameRoutine = null;
        }

        if (networkManager.IsServer)
        {
            SetStatus($"플레이어 대기 중... {networkManager.ConnectedClientsIds.Count}/{maxPlayers}");
            return;
        }

        if (clientId != networkManager.LocalClientId)
            return;

        string reason = networkManager.DisconnectReason;

        if (string.IsNullOrWhiteSpace(reason))
            SetStatus("서버와의 연결이 끊겼습니다.");
        else
            SetStatus($"서버와의 연결이 끊겼습니다: {reason}");
    }

    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null || !networkManager.IsServer)
            return;

        if (sceneEvent.SceneEventType != SceneEventType.SynchronizeComplete)
            return;

        if (!IsClientConnected(networkManager, sceneEvent.ClientId))
            return;

        synchronizedClientIds.Add(sceneEvent.ClientId);
        SetStatus($"플레이어 동기화 완료... {GetReadyPlayerCount()}/{maxPlayers}");
        TryScheduleBeginGame();
    }

    private void TryScheduleBeginGame()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null || !networkManager.IsServer)
            return;

        if (isGameStarting || beginGameRoutine != null)
            return;

        if (!CanBeginGame())
            return;

        beginGameRoutine = StartCoroutine(BeginGameAfterSceneEvent());
    }

    private IEnumerator BeginGameAfterSceneEvent()
    {
        yield return null;

        beginGameRoutine = null;

        if (!CanBeginGame())
            yield break;

        if (gameScene == SceneType.None)
        {
            SetStatus("게임 씬이 설정되지 않았습니다.");
            Debug.LogError("MultiplayerLobbyManager의 Game Scene을 설정해야 합니다.", this);
            yield break;
        }

        if (SceneLoader.Instance == null)
        {
            SetStatus("SceneLoader가 없습니다.");
            Debug.LogError("현재 씬에 SceneLoader가 필요합니다.", this);
            yield break;
        }

        isGameStarting = true;
        SetStatus("2명 동기화 완료. 게임을 시작합니다.");
        SceneLoader.Instance.LoadScene(gameScene);
    }

    private bool CanBeginGame()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
            return false;

        if (!networkManager.IsServer)
            return false;

        if (networkManager.ConnectedClientsIds.Count < maxPlayers)
            return false;

        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId == networkManager.ServerClientId)
                continue;

            if (!synchronizedClientIds.Contains(clientId))
                return false;
        }

        return true;
    }

    private int GetReadyPlayerCount()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
            return 0;

        int count = 0;

        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId == networkManager.ServerClientId || synchronizedClientIds.Contains(clientId))
                count++;
        }

        return count;
    }

    private bool IsClientConnected(NetworkManager networkManager, ulong clientId)
    {
        foreach (ulong connectedClientId in networkManager.ConnectedClientsIds)
        {
            if (connectedClientId == clientId)
                return true;
        }

        return false;
    }

    private bool CanStartNewConnection()
    {
        if (IsBusy)
        {
            SetStatus("이미 처리 중입니다.");
            return false;
        }

        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
        {
            SetStatus("NetworkManager가 없습니다.");
            return false;
        }

        if (networkManager.IsListening)
        {
            SetStatus("이미 네트워크 연결 중입니다.");
            return false;
        }

        if (networkManager.GetComponent<UnityTransport>() != null)
            return true;

        SetStatus("NetworkManager에 UnityTransport가 없습니다.");
        return false;
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        StateChanged?.Invoke();
    }

    private void SetStatus(string message)
    {
        CurrentStatus = message;
        StatusChanged?.Invoke(CurrentStatus);
        StateChanged?.Invoke();
    }

    private void RaisePlayerCountChanged()
    {
        PlayerCountChanged?.Invoke(ConnectedPlayerCount, maxPlayers);
        StateChanged?.Invoke();
    }

    private string NormalizeJoinCode(string joinCode) => string.IsNullOrWhiteSpace(joinCode) ? string.Empty : joinCode.Trim().ToUpperInvariant();
}