using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public interface ILaserReceiver
{
    public bool BlocksLaser { get; }

    public void SetLaserInput(Component source, string sourceKey, bool active);
}

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Trigger))]
public sealed class LaserReceiverRuntime : MonoBehaviour, ILaserReceiver
{
    private const string TriggerSourceKey = "LaserReceiver";

    private readonly struct LaserInputSource : IEquatable<LaserInputSource>
    {
        public Component Source { get; }
        public string Key { get; }

        public LaserInputSource(Component source, string key)
        {
            Source = source;
            Key = key ?? string.Empty;
        }

        public bool Equals(LaserInputSource other) => Source == other.Source && Key == other.Key;

        public override bool Equals(object obj) => obj is LaserInputSource other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Source != null ? Source.GetEntityId() : default, Key);
    }

    [SerializeField, Required, TitleGroup("References")]
    private Trigger trigger;

    [SerializeField, TitleGroup("Laser")]
    private bool blocksLaser = true;

    [ShowInInspector, ReadOnly]
    public bool IsLasered => activeInputs.Count > 0;

    public bool BlocksLaser => blocksLaser;

    private readonly HashSet<LaserInputSource> activeInputs = new();

    private void OnDisable()
    {
        if (!LaserSystemManager.CanWriteGameplay)
            return;

        if (IsLasered)
            trigger.EndTrigger(this, TriggerSourceKey);

        activeInputs.Clear();
    }

    public void SetLaserInput(Component source, string sourceKey, bool active)
    {
        if (!LaserSystemManager.CanWriteGameplay)
            return;

        LaserInputSource input = new(source, sourceKey);

        if (active)
        {
            bool wasLasered = IsLasered;

            if (!activeInputs.Add(input))
                return;

            if (!wasLasered)
                trigger.BeginTrigger(this, TriggerSourceKey);

            return;
        }

        if (!activeInputs.Remove(input))
            return;

        if (!IsLasered)
            trigger.EndTrigger(this, TriggerSourceKey);
    }
}