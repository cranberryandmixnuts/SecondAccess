using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public sealed class PlayerInteractionModule : PlayerModule
{
    [SerializeField, Required, TitleGroup("References")]
    private InteractionSource interactionSource;

    [SerializeField, TitleGroup("Raycast")]
    private LayerMask interactionMask = ~0;

    [SerializeField, MinValue(0f), TitleGroup("Raycast")]
    private float interactionDistance = 100f;

    private readonly List<InteractionSource> extraInteractionSources = new();

    private bool inputEnabled = true;

    public Interactable HoveredInteractable => interactionSource.HoveredInteractable;
    public Interactable ActiveInteractable => interactionSource.ActiveInteractable;

    private void Update()
    {
        if (!inputEnabled)
            return;

        RefreshInteractionSources();

        if (!InputManager.Instance.InterActionHeld)
            EndActiveInteractions();

        if (InputManager.Instance.InterActionDown)
            TryStartCurrentInteraction();
    }

    public void RegisterInteractionSource(InteractionSource source)
    {
        if (source == interactionSource)
            return;

        if (extraInteractionSources.Contains(source))
            return;

        extraInteractionSources.Add(source);
    }

    public void UnregisterInteractionSource(InteractionSource source)
    {
        if (!extraInteractionSources.Remove(source))
            return;

        source.ClearHover();
        source.EndActiveInteraction();
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;

        if (inputEnabled)
            return;

        ClearAllHover();
        EndActiveInteractions();
    }

    public void ForceEndInteraction() => EndActiveInteractions();

    private void RefreshInteractionSources()
    {
        interactionSource.RefreshHovered(Player.Camera, interactionMask, interactionDistance);

        for (int i = extraInteractionSources.Count - 1; i >= 0; i--)
        {
            InteractionSource source = extraInteractionSources[i];

            if (source == null)
            {
                extraInteractionSources.RemoveAt(i);
                continue;
            }

            if (!source.isActiveAndEnabled)
                continue;

            source.RefreshHovered(Player.Camera, interactionMask, interactionDistance);
        }
    }

    private bool TryStartCurrentInteraction()
    {
        if (interactionSource.TryStartCurrentInteraction())
            return true;

        for (int i = 0; i < extraInteractionSources.Count; i++)
        {
            InteractionSource source = extraInteractionSources[i];

            if (source == null)
                continue;

            if (!source.isActiveAndEnabled)
                continue;

            if (source.TryStartCurrentInteraction())
                return true;
        }

        return false;
    }

    private void EndActiveInteractions()
    {
        interactionSource.EndActiveInteraction();

        for (int i = 0; i < extraInteractionSources.Count; i++)
        {
            InteractionSource source = extraInteractionSources[i];

            if (source == null)
                continue;

            source.EndActiveInteraction();
        }
    }

    private void ClearAllHover()
    {
        interactionSource.ClearHover();

        for (int i = 0; i < extraInteractionSources.Count; i++)
        {
            InteractionSource source = extraInteractionSources[i];

            if (source == null)
                continue;

            source.ClearHover();
        }
    }
}