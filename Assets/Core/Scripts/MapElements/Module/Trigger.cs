using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public enum TriggerType
{
    Single,
    Hold
}

[RequireComponent(typeof(TriggerSignalEffect))]
public sealed class Trigger : MonoBehaviour
{
    private readonly struct SustainEffectSourceId : IEquatable<SustainEffectSourceId>
    {
        public SustainEffectSourceId(EntityId sourceEntityId, string key)
        {
            SourceEntityId = sourceEntityId;
            Key = key;
        }

        public EntityId SourceEntityId { get; }
        public string Key { get; }

        public bool Equals(SustainEffectSourceId other) => SourceEntityId == other.SourceEntityId && Key == other.Key;

        public override bool Equals(object obj) => obj is SustainEffectSourceId other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(SourceEntityId, Key);
    }

    [SerializeField]
    private TriggerType triggerType = TriggerType.Single;

    [SerializeField]
    private List<TriggerTarget> targets = new();

    [SerializeField, Required]
    private TriggerSignalEffect signalEffect;

    private readonly HashSet<SustainEffectSourceId> sustainEffectSources = new();

    public TriggerType TriggerType => triggerType;
    public IReadOnlyList<TriggerTarget> Targets => targets;

    private void Awake() => EnsureSignalEffect();

    private void OnValidate()
    {
        if (signalEffect == null)
            signalEffect = GetComponent<TriggerSignalEffect>();
    }

    public void BeginTrigger(Component source) => BeginTrigger(source, string.Empty);

    public void BeginTrigger(Component source, string sourceKey)
    {
        switch (triggerType)
        {
            case TriggerType.Single:
                TriggerOnce();
                break;

            case TriggerType.Hold:
                BeginSustain(source, sourceKey);
                break;
        }
    }

    public void EndTrigger(Component source) => EndTrigger(source, string.Empty);

    public void EndTrigger(Component source, string sourceKey)
    {
        if (triggerType != TriggerType.Hold)
            return;

        EndSustain(source, sourceKey);
    }

    public void TriggerOnce()
    {
        PlaySignalEffect();

        for (int i = 0; i < targets.Count; i++)
        {
            TriggerTarget target = targets[i];

            if (target == null)
                continue;

            target.ApplySingleTrigger();
        }
    }

    public void BeginSustain(Component source) => BeginSustain(source, string.Empty);

    public void BeginSustain(Component source, string sourceKey)
    {
        if (sustainEffectSources.Add(CreateSustainEffectSourceId(source, sourceKey)))
            PlaySignalEffect();

        for (int i = 0; i < targets.Count; i++)
        {
            TriggerTarget target = targets[i];

            if (target == null)
                continue;

            target.BeginSustain(source, sourceKey);
        }
    }

    public void EndSustain(Component source) => EndSustain(source, string.Empty);

    public void EndSustain(Component source, string sourceKey)
    {
        if (sustainEffectSources.Remove(CreateSustainEffectSourceId(source, sourceKey)))
            PlaySignalEffect();

        for (int i = 0; i < targets.Count; i++)
        {
            TriggerTarget target = targets[i];

            if (target == null)
                continue;

            target.EndSustain(source, sourceKey);
        }
    }

    public void SetSustain(Component source, bool active) => SetSustain(source, string.Empty, active);

    public void SetSustain(Component source, string sourceKey, bool active)
    {
        if (active)
            BeginSustain(source, sourceKey);
        else
            EndSustain(source, sourceKey);
    }

    private void PlaySignalEffect() => EnsureSignalEffect().Play(transform, targets);

    private TriggerSignalEffect EnsureSignalEffect()
    {
        if (signalEffect != null)
            return signalEffect;

        if (!TryGetComponent(out signalEffect))
            signalEffect = gameObject.AddComponent<TriggerSignalEffect>();

        return signalEffect;
    }

    private SustainEffectSourceId CreateSustainEffectSourceId(Component source, string sourceKey) =>
        new(source.GetEntityId(), sourceKey ?? string.Empty);
}