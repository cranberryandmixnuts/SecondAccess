using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Trigger))]
public sealed class LaserInverseReceiverRuntime : MonoBehaviour, ILaserReceiver
{
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

        public override int GetHashCode() => HashCode.Combine(Source != null ? Source.GetInstanceID() : 0, Key);
    }

    private const string InverseSourceKey = "InverseLaserAbsence";

    [SerializeField, Required, TitleGroup("References")]
    private Trigger trigger;

    [SerializeField, TitleGroup("Laser")]
    private bool blocksLaser = true;

    [ShowInInspector, ReadOnly]
    public bool IsLasered => activeInputs.Count > 0;

    public bool BlocksLaser => blocksLaser;

    private readonly HashSet<LaserInputSource> activeInputs = new();
    private bool inverseSustainActive;

    private void Reset()
    {
        trigger = GetComponent<Trigger>();
    }

    private void Awake()
    {
        trigger = GetComponent<Trigger>();
        LaserSystemRuntime.EnsureExists();
    }

    private void OnEnable()
    {
        RefreshInverseSustain();
    }

    private void Update()
    {
        RefreshInverseSustain();
    }

    private void OnDisable()
    {
        if (!LaserSystemRuntime.CanWriteGameplay)
            return;

        foreach (LaserInputSource input in activeInputs)
        {
            if (input.Source != null)
                trigger.EndSustain(input.Source, input.Key);
        }

        activeInputs.Clear();
        SetInverseSustain(false);
    }

    public void SetLaserInput(Component source, string sourceKey, bool active)
    {
        if (!LaserSystemRuntime.CanWriteGameplay)
            return;

        LaserInputSource input = new(source, sourceKey);

        if (active)
            activeInputs.Add(input);
        else
            activeInputs.Remove(input);

        RefreshInverseSustain();
    }

    private void RefreshInverseSustain()
    {
        if (!LaserSystemRuntime.CanWriteGameplay)
            return;

        SetInverseSustain(activeInputs.Count == 0);
    }

    private void SetInverseSustain(bool active)
    {
        if (inverseSustainActive == active)
            return;

        inverseSustainActive = active;

        if (inverseSustainActive)
            trigger.BeginSustain(this, InverseSourceKey);
        else
            trigger.EndSustain(this, InverseSourceKey);
    }
}
