using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
[RequireComponent(typeof(UnityTransport))]
public sealed class ListenServerLauncher : MonoBehaviour
{
    [SerializeField] private string defaultAddress = "127.0.0.1";
    [SerializeField] private ushort defaultPort = 7777;
    [SerializeField] private bool showGui = true;

    private NetworkManager networkManager;
    private UnityTransport unityTransport;
    private string address;
    private string portText;
    private string status;

    private void Awake()
    {
        Application.runInBackground = true;
        networkManager = GetComponent<NetworkManager>();
        unityTransport = GetComponent<UnityTransport>();
        address = defaultAddress;
        portText = defaultPort.ToString();
        status = "Idle";
    }

    private void OnEnable()
    {
        networkManager.OnServerStarted += HandleServerStarted;
        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    private void OnDisable()
    {
        networkManager.OnServerStarted -= HandleServerStarted;
        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    public void StartHostSession()
    {
        if (networkManager.IsListening)
            return;

        ushort port = GetPort();
        unityTransport.SetConnectionData("127.0.0.1", port, "0.0.0.0");

        if (!networkManager.StartHost())
            status = "Host start failed";
    }

    public void StartClientSession()
    {
        if (networkManager.IsListening)
            return;

        ushort port = GetPort();
        unityTransport.SetConnectionData(address, port);

        if (!networkManager.StartClient())
            status = "Client start failed";
        else
            status = $"Connecting to {address}:{port}";
    }

    public void ShutdownSession()
    {
        if (!networkManager.IsListening)
            return;

        networkManager.Shutdown();
        status = "Shutdown complete";
    }

    private ushort GetPort()
    {
        if (ushort.TryParse(portText, out ushort port))
            return port;

        portText = defaultPort.ToString();
        return defaultPort;
    }

    private void HandleServerStarted()
    {
        status = networkManager.IsHost ? "Host started" : "Server started";
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (networkManager.IsHost && clientId == NetworkManager.ServerClientId)
            status = $"Host connected ({clientId})";
        else if (networkManager.IsServer)
            status = $"Client connected ({clientId})";
        else
            status = $"Connected to host ({clientId})";
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!networkManager.IsListening)
        {
            status = $"Disconnected ({clientId})";
            return;
        }

        status = networkManager.IsServer
            ? $"Client disconnected ({clientId})"
            : $"Disconnected from host ({clientId})";
    }

    private void OnGUI()
    {
        if (!showGui)
            return;

        GUILayout.BeginArea(new Rect(16f, 16f, 280f, 230f), GUI.skin.box);
        GUILayout.Label("Listen Server Launcher");
        GUILayout.Space(8f);
        GUILayout.Label($"Status: {status}");
        GUILayout.Space(8f);
        GUILayout.Label("Address");
        address = GUILayout.TextField(address);
        GUILayout.Label("Port");
        portText = GUILayout.TextField(portText);
        GUILayout.Space(8f);

        GUI.enabled = !networkManager.IsListening;
        if (GUILayout.Button("Start Host", GUILayout.Height(32f)))
            StartHostSession();
        if (GUILayout.Button("Start Client", GUILayout.Height(32f)))
            StartClientSession();

        GUI.enabled = networkManager.IsListening;
        if (GUILayout.Button("Shutdown", GUILayout.Height(32f)))
            ShutdownSession();

        GUI.enabled = true;
        GUILayout.EndArea();
    }
}