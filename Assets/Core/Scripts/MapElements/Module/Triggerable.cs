using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public sealed class Triggerable : MonoBehaviour
{
    private readonly struct SustainSourceId : IEquatable<SustainSourceId>
    {
        public SustainSourceId(int ownerId, string key)
        {
            OwnerId = ownerId;
            Key = key;
        }

        public int OwnerId { get; }
        public string Key { get; }

        public bool Equals(SustainSourceId other) => OwnerId == other.OwnerId && Key == other.Key;

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
    public bool IsTriggered => initialTriggered ^ LatchedTriggered ^ (sustainSources.Count > 0);

    public event Action<Triggerable, bool, bool> TriggerStateChanged;

    private readonly HashSet<SustainSourceId> sustainSources = new();

    public void ApplySingleTrigger()
    {
        bool previous = IsTriggered;
        LatchedTriggered = !LatchedTriggered;
        RaiseIfChanged(previous);
    }

    public void BeginSustain(Component source) => BeginSustain(source, string.Empty);

    public void BeginSustain(Component source, string sourceKey)
    {
        bool previous = IsTriggered;
        sustainSources.Add(CreateSourceId(source, sourceKey));
        RaiseIfChanged(previous);
    }

    public void EndSustain(Component source) => EndSustain(source, string.Empty);

    public void EndSustain(Component source, string sourceKey)
    {
        bool previous = IsTriggered;
        sustainSources.Remove(CreateSourceId(source, sourceKey));
        RaiseIfChanged(previous);
    }

    public void ResetTriggerState()
    {
        bool previous = IsTriggered;
        LatchedTriggered = false;
        sustainSources.Clear();
        RaiseIfChanged(previous);
    }

    private void RaiseIfChanged(bool previous)
    {
        bool current = IsTriggered;

        if (previous == current)
            return;

        TriggerStateChanged?.Invoke(this, previous, current);
    }

    private SustainSourceId CreateSourceId(Component source, string sourceKey)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        return new SustainSourceId(source.GetInstanceID(), sourceKey ?? string.Empty);
    }
}
