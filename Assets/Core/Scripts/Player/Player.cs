using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
[DefaultExecutionOrder(-20000)]

[RequireComponent(typeof(PlayerMovementModule))]
[RequireComponent(typeof(PlayerInteractionModule))]
public sealed class Player : MonoBehaviour
{
    [HideInInspector]
    public PlayerMovementModule Movement { get; private set; }

    [HideInInspector]
    public PlayerInteractionModule Interaction { get; private set; }

    public IReadOnlyList<PlayerModule> Modules => modules;

    private readonly List<PlayerModule> modules = new();

    private void Reset() => CacheModules();

    private void Awake()
    {
        CacheModules();
        BindModules();
    }

    public T GetModule<T>() where T : PlayerModule => GetComponent<T>();

    private void CacheModules()
    {
        modules.Clear();
        GetComponents(modules);
        Movement = GetComponent<PlayerMovementModule>();
        Interaction = GetComponent<PlayerInteractionModule>();
    }

    private void BindModules()
    {
        for (int i = 0; i < modules.Count; i++)
            modules[i].Initialize(this);
    }
}
