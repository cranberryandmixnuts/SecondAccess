using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(-30000)]
[RequireComponent(typeof(NetworkObject))]
public abstract class NetworkSingleton<T, TScope> : NetworkBehaviour where T : NetworkBehaviour where TScope : struct, ISingletonScope
{
    public static T Instance { get; private set; }

    public static bool HasInstance => Instance != null;

    private static readonly TScope scope = new();

    static NetworkSingleton()
    {
        SingletonRuntimeBridge.SubsystemRegistration += ResetStatics;
    }

    private static void ResetStatics() => Instance = null;

    protected void Awake()
    {
        T self = (T)(object)this;

        if (Instance != null && Instance != self)
        {
            Debug.LogWarning($"[NetworkSingleton] Duplicate destroyed. Type={typeof(T).Name}, Destroyed={name}, Kept={((NetworkBehaviour)Instance).name}", this);
            Destroy(gameObject);
            return;
        }

        Instance = self;

        if (scope.IsGlobal) DontDestroyOnLoad(gameObject);

        NetworkSingletonAwake();
    }

    public sealed override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        NetworkSingletonOnNetworkSpawn();
    }

    public sealed override void OnNetworkDespawn()
    {
        NetworkSingletonOnNetworkDespawn();

        base.OnNetworkDespawn();
    }

    protected new void OnDestroy()
    {
        base.OnDestroy();

        if (Instance == (T)(object)this) Instance = null;

        NetworkSingletonOnDestroy();
    }

    protected virtual void NetworkSingletonAwake() { }

    protected virtual void NetworkSingletonOnNetworkSpawn() { }

    protected virtual void NetworkSingletonOnNetworkDespawn() { }

    protected virtual void NetworkSingletonOnDestroy() { }
}