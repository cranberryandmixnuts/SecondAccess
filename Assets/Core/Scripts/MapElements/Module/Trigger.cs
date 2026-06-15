using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public enum TriggerType
{
    Single,
    Hold
}

public sealed class Trigger : MonoBehaviour
{
    [SerializeField]
    private TriggerType triggerType = TriggerType.Single;

    [SerializeField]
    private List<TriggerTarget> targets = new();

    public TriggerType TriggerType => triggerType;
    public IReadOnlyList<TriggerTarget> Targets => targets;

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
}