using Sirenix.OdinInspector;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public sealed class SecondAccessNetworkBootstrap : MonoBehaviour
{
    [SerializeField, TitleGroup("Connection")]
    private string address = "127.0.0.1";

    [SerializeField, TitleGroup("Connection")]
    private string listenAddress = "0.0.0.0";

    [SerializeField, TitleGroup("Connection")]
    private ushort port = 7777;

    public string Address => address;
    public ushort Port => port;

    public void SetAddress(string value) => address = value;

    public void SetPort(ushort value) => port = value;

    [Button]
    public void StartHost()
    {
        ApplyConnectionData();
        NetworkManager.Singleton.StartHost();
    }

    [Button]
    public void StartClient()
    {
        ApplyConnectionData();
        NetworkManager.Singleton.StartClient();
    }

    [Button]
    public void StartServer()
    {
        ApplyConnectionData();
        NetworkManager.Singleton.StartServer();
    }

    [Button]
    public void Shutdown()
    {
        if (NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
    }

    private void ApplyConnectionData()
    {
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetConnectionData(address, port, listenAddress);
    }
}
