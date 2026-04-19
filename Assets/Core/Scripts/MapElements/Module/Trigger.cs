using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public sealed class Trigger : MonoBehaviour
{
    [SerializeField, ListDrawerSettings(Expanded = true)]
    private List<Triggerable> targets = new();

    public IReadOnlyList<Triggerable> Targets => targets;

    public void TriggerOnce()
    {
        for (int i = 0; i < targets.Count; i++)
        {
            Triggerable target = targets[i];

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
            Triggerable target = targets[i];

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
            Triggerable target = targets[i];

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
