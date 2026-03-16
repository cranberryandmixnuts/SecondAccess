using System;
using UnityEngine;

public interface ISingletonScope { public bool IsGlobal { get; } }

public readonly struct GlobalScope : ISingletonScope { public bool IsGlobal => true; }
public readonly struct SceneScope : ISingletonScope { public bool IsGlobal => false; }

public static class SingletonRuntimeBridge
{
    public static event Action SubsystemRegistration;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void OnSubsystemRegistration() => SubsystemRegistration?.Invoke();
}

[DefaultExecutionOrder(-30000)]
public abstract class Singleton<T, TScope> : MonoBehaviour where T : MonoBehaviour where TScope : struct, ISingletonScope
{
    public static T Instance { get; private set; }

    private static readonly TScope scope = new();

    static Singleton() { SingletonRuntimeBridge.SubsystemRegistration += ResetStatics; }

    private static void ResetStatics() => Instance = null;

    protected void Awake()
    {
        T self = (T)(object)this;

        if (Instance != null && Instance != self)
        {
            Debug.LogWarning($"[Singleton] Duplicate destroyed. Type={typeof(T).Name}, Destroyed={name}, Kept={((MonoBehaviour)Instance).name}", this);
            Destroy(gameObject);
            return;
        }

        Instance = self;

        if (scope.IsGlobal) DontDestroyOnLoad(gameObject);

        SingletonAwake();
    }

    protected void OnDestroy()
    {
        if (Instance == (T)(object)this) Instance = null;

        SingletonOnDestroy();
    }

    protected virtual void SingletonAwake() { }

    protected virtual void SingletonOnDestroy() { }
}