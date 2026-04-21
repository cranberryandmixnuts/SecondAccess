using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider))]
public sealed class InteractionSource : MonoBehaviour
{
    public Interactable HoveredInteractable { get; private set; }
    public Interactable ActiveInteractable { get; private set; }

    private readonly Dictionary<Interactable, int> overlapCounts = new();

    private void OnDisable()
    {
        ClearHover();
        EndActiveInteraction();
        overlapCounts.Clear();
    }

    public void RefreshHovered(Camera targetCamera, LayerMask interactionMask, float distance)
    {
        Interactable nextHovered = ResolveHoveredInteractable(targetCamera, interactionMask, distance);

        if (HoveredInteractable == nextHovered)
            return;

        if (HoveredInteractable != null)
            HoveredInteractable.NotifyHoverExit(this);

        HoveredInteractable = nextHovered;

        if (HoveredInteractable != null)
            HoveredInteractable.NotifyHoverEnter(this);
    }

    public bool TryStartCurrentInteraction()
    {
        if (HoveredInteractable == null)
            return false;

        if (HoveredInteractable.ExecutionType == InteractionExecutionType.Instant)
            return HoveredInteractable.TryPerformInteraction(this);

        if (!HoveredInteractable.TryBeginInteraction(this))
            return false;

        ActiveInteractable = HoveredInteractable;
        return true;
    }

    public void EndActiveInteraction()
    {
        if (ActiveInteractable == null)
            return;

        ActiveInteractable.TryEndInteraction(this);
        ActiveInteractable = null;
    }

    public void ClearHover()
    {
        if (HoveredInteractable == null)
            return;

        HoveredInteractable.NotifyHoverExit(this);
        HoveredInteractable = null;
    }

    private void OnTriggerEnter(Collider other)
    {
        Interactable interactable = ResolveInteractable(other);

        if (interactable == null)
            return;

        overlapCounts.TryGetValue(interactable, out int count);
        overlapCounts[interactable] = count + 1;
    }

    private void OnTriggerExit(Collider other)
    {
        Interactable interactable = ResolveInteractable(other);

        if (interactable == null)
            return;

        if (!overlapCounts.TryGetValue(interactable, out int count))
            return;

        if (count <= 1)
            overlapCounts.Remove(interactable);
        else
            overlapCounts[interactable] = count - 1;

        if (HoveredInteractable == interactable && !IsAvailableCandidate(interactable))
            ClearHover();

        if (ActiveInteractable == interactable && !IsAvailableCandidate(interactable))
            EndActiveInteraction();
    }

    private Interactable ResolveHoveredInteractable(Camera targetCamera, LayerMask interactionMask, float distance)
    {
        if (targetCamera == null)
            return null;

        if (Mouse.current == null)
            return null;

        Ray ray = targetCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (!Physics.Raycast(ray, out RaycastHit hit, distance, interactionMask, QueryTriggerInteraction.Collide))
            return null;

        Interactable interactable = ResolveInteractable(hit.collider);

        if (!IsAvailableCandidate(interactable))
            return null;

        return interactable;
    }

    private Interactable ResolveInteractable(Collider targetCollider)
    {
        
        if (targetCollider.TryGetComponent<Interactable>(out var interactable))
            return interactable;

        return targetCollider.GetComponentInParent<Interactable>();
    }

    private bool IsAvailableCandidate(Interactable interactable)
    {
        if (interactable == null)
            return false;

        if (!overlapCounts.ContainsKey(interactable))
            return false;

        return interactable.CanHover(this);
    }
}
