using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public enum InteractionExecutionType
{
    Instant,
    Hold
}

public sealed class Interactable : MonoBehaviour
{
    [SerializeField]
    private InteractionExecutionType executionType = InteractionExecutionType.Instant;

    [SerializeField]
    private bool interactionEnabled = true;

    [SerializeField]
    private bool allowMultipleInteractors;

    public InteractionExecutionType ExecutionType => executionType;
    public bool InteractionEnabled => interactionEnabled;

    [ShowInInspector, ReadOnly]
    public int HoveredSourceCount => hoveredSources.Count;

    [ShowInInspector, ReadOnly]
    public int InteractingSourceCount => interactingSources.Count;

    public bool IsHovered => hoveredSources.Count > 0;
    public bool IsBeingInteractedWith => interactingSources.Count > 0;

    public event Action<InteractionSource> HoverEntered;
    public event Action<InteractionSource> HoverExited;
    public event Action<InteractionSource> InteractionStarted;
    public event Action<InteractionSource> InteractionPerformed;
    public event Action<InteractionSource> InteractionEnded;
    public event Action<bool> InteractionEnabledChanged;

    private readonly HashSet<InteractionSource> hoveredSources = new();
    private readonly HashSet<InteractionSource> interactingSources = new();

    private void OnDisable()
    {
        hoveredSources.Clear();
        interactingSources.Clear();
    }

    public bool CanHover(InteractionSource source) => interactionEnabled && isActiveAndEnabled;

    public bool CanInteract(InteractionSource source)
    {
        if (!CanHover(source))
            return false;

        if (allowMultipleInteractors)
            return true;

        return interactingSources.Count == 0 || interactingSources.Contains(source);
    }

    public bool TryPerformInteraction(InteractionSource source)
    {
        if (executionType != InteractionExecutionType.Instant)
            return false;

        if (!CanInteract(source))
            return false;

        InteractionPerformed?.Invoke(source);
        return true;
    }

    public bool TryBeginInteraction(InteractionSource source)
    {
        if (executionType != InteractionExecutionType.Hold)
            return false;

        if (!CanInteract(source))
            return false;

        if (!interactingSources.Add(source))
            return false;

        InteractionStarted?.Invoke(source);
        return true;
    }

    public bool TryEndInteraction(InteractionSource source)
    {
        if (!interactingSources.Remove(source))
            return false;

        InteractionEnded?.Invoke(source);
        return true;
    }

    public void SetInteractionEnabled(bool value)
    {
        if (interactionEnabled == value)
            return;

        interactionEnabled = value;
        InteractionEnabledChanged?.Invoke(interactionEnabled);
    }

    public void NotifyHoverEnter(InteractionSource source)
    {
        if (!hoveredSources.Add(source))
            return;

        HoverEntered?.Invoke(source);
    }

    public void NotifyHoverExit(InteractionSource source)
    {
        if (!hoveredSources.Remove(source))
            return;

        HoverExited?.Invoke(source);
    }
}
