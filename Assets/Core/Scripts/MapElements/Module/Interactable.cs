using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class Interactable : MonoBehaviour
{
    [SerializeField]
    private bool interactionEnabled = true;

    [Title("Outline")]
    [SerializeField]
    private bool useHoverOutline = true;

    [SerializeField]
    private string outlineLayerName = "Outline Target";

    [SerializeField]
    private bool includeOutlineTargetChildren = true;

    [SerializeField]
    private List<GameObject> outlineTargets = new();

    public bool InteractionEnabled => interactionEnabled;
    public int HoveredSourceCount => hoveredSources.Count;
    public int InteractingSourceCount => interactingSources.Count;
    public bool IsHovered => hoveredSources.Count > 0;
    public bool IsBeingInteractedWith => interactingSources.Count > 0;

    public event Action<InteractionSource> HoverEntered;
    public event Action<InteractionSource> HoverExited;
    public event Action<InteractionSource> InteractionStarted;
    public event Action<InteractionSource> InteractionEnded;
    public event Action<bool> InteractionEnabledChanged;

    private readonly HashSet<InteractionSource> hoveredSources = new();
    private readonly HashSet<InteractionSource> interactingSources = new();
    private readonly Dictionary<Transform, int> outlinedOriginalLayers = new();

    private void OnDisable()
    {
        EndAllInteractions();

        SetOutlineActive(false);
        hoveredSources.Clear();
    }

    public bool CanHover() => interactionEnabled && isActiveAndEnabled;

    public bool CanInteract() => CanHover();

    public bool TryBeginInteraction(InteractionSource source)
    {
        if (!CanInteract())
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

        if (!interactionEnabled)
            EndAllInteractions();

        SetOutlineActive(interactionEnabled && hoveredSources.Count > 0);

        InteractionEnabledChanged?.Invoke(interactionEnabled);
    }

    public void NotifyHoverEnter(InteractionSource source)
    {
        if (!CanHover())
            return;

        if (!hoveredSources.Add(source))
            return;

        if (hoveredSources.Count == 1)
            SetOutlineActive(true);

        HoverEntered?.Invoke(source);
    }

    public void NotifyHoverExit(InteractionSource source)
    {
        if (!hoveredSources.Remove(source))
            return;

        if (hoveredSources.Count == 0)
            SetOutlineActive(false);

        HoverExited?.Invoke(source);
    }

    private void EndAllInteractions()
    {
        if (interactingSources.Count == 0)
            return;

        List<InteractionSource> sources = new(interactingSources);
        interactingSources.Clear();

        for (int i = 0; i < sources.Count; i++)
            InteractionEnded?.Invoke(sources[i]);
    }

    private void SetOutlineActive(bool active)
    {
        if (!useHoverOutline)
            return;

        if (active)
        {
            int outlineLayer = LayerMask.NameToLayer(outlineLayerName);

            if (outlineLayer < 0)
                return;

            ApplyOutlineLayer(outlineLayer);
            return;
        }

        RestoreOriginalLayers();
    }

    private void ApplyOutlineLayer(int outlineLayer)
    {
        if (outlinedOriginalLayers.Count > 0)
            return;

        for (int i = 0; i < outlineTargets.Count; i++)
            ApplyOutlineLayerRecursive(outlineTargets[i].transform, outlineLayer);
    }

    private void ApplyOutlineLayerRecursive(Transform target, int outlineLayer)
    {
        if (!outlinedOriginalLayers.ContainsKey(target))
            outlinedOriginalLayers.Add(target, target.gameObject.layer);

        target.gameObject.layer = outlineLayer;

        if (!includeOutlineTargetChildren)
            return;

        for (int i = 0; i < target.childCount; i++)
            ApplyOutlineLayerRecursive(target.GetChild(i), outlineLayer);
    }

    private void RestoreOriginalLayers()
    {
        if (outlinedOriginalLayers.Count == 0)
            return;

        foreach (KeyValuePair<Transform, int> pair in outlinedOriginalLayers)
            pair.Key.gameObject.layer = pair.Value;

        outlinedOriginalLayers.Clear();
    }
}