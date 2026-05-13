using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public sealed class TriggerTarget : NetworkBehaviour
{
    private readonly struct SustainSourceId : IEquatable<SustainSourceId>
    {
        public SustainSourceId(EntityId ownerId, string key)
        {
            OwnerId = ownerId;
            Key = key;
        }

        public EntityId OwnerId { get; }
        public string Key { get; }

        public bool Equals(SustainSourceId other) => OwnerId.Equals(other.OwnerId) && Key == other.Key;

        public override bool Equals(object obj) => obj is SustainSourceId other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(OwnerId, Key);
    }

    [SerializeField]
    private bool initialTriggered;

    [ShowInInspector, ReadOnly]
    public bool LatchedTriggered { get; private set; }

    [ShowInInspector, ReadOnly]
    public int SustainSourceCount => sustainSources.Count;

    [ShowInInspector, ReadOnly]
    public bool IsTriggered => IsSpawned ? networkTriggered.Value : ComputeLocalTriggered();

    public event Action<TriggerTarget, bool, bool> TriggerStateChanged;

    private readonly HashSet<SustainSourceId> sustainSources = new();

    private readonly NetworkVariable<bool> networkTriggered = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        networkTriggered.OnValueChanged += OnNetworkTriggeredChanged;

        if (IsServer)
            networkTriggered.Value = ComputeLocalTriggered();

        TriggerStateChanged?.Invoke(this, !networkTriggered.Value, networkTriggered.Value);
    }

    public override void OnNetworkDespawn()
    {
        networkTriggered.OnValueChanged -= OnNetworkTriggeredChanged;
    }

    public void ApplySingleTrigger()
    {
        if (IsSpawned && !IsServer)
            return;

        bool previous = ComputeLocalTriggered();
        LatchedTriggered = !LatchedTriggered;
        RaiseIfChanged(previous);
    }

    public void BeginSustain(Component source) => BeginSustain(source, string.Empty);

    public void BeginSustain(Component source, string sourceKey)
    {
        if (IsSpawned && !IsServer)
            return;

        bool previous = ComputeLocalTriggered();
        sustainSources.Add(CreateSourceId(source, sourceKey));
        RaiseIfChanged(previous);
    }

    public void EndSustain(Component source) => EndSustain(source, string.Empty);

    public void EndSustain(Component source, string sourceKey)
    {
        if (IsSpawned && !IsServer)
            return;

        bool previous = ComputeLocalTriggered();
        sustainSources.Remove(CreateSourceId(source, sourceKey));
        RaiseIfChanged(previous);
    }

    public void ResetTriggerState()
    {
        if (IsSpawned && !IsServer)
            return;

        bool previous = ComputeLocalTriggered();
        LatchedTriggered = false;
        sustainSources.Clear();
        RaiseIfChanged(previous);
    }

    private bool ComputeLocalTriggered()
    {
        if (sustainSources.Count > 0)
            return !initialTriggered;

        return initialTriggered ^ LatchedTriggered;
    }

    private void RaiseIfChanged(bool previous)
    {
        bool current = ComputeLocalTriggered();

        if (previous == current)
            return;

        if (IsSpawned)
        {
            networkTriggered.Value = current;
            return;
        }

        TriggerStateChanged?.Invoke(this, previous, current);
    }

    private void OnNetworkTriggeredChanged(bool previous, bool current)
    {
        TriggerStateChanged?.Invoke(this, previous, current);
    }

    private SustainSourceId CreateSourceId(Component source, string sourceKey)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        return new SustainSourceId(source.GetEntityId(), sourceKey ?? string.Empty);
    }
}