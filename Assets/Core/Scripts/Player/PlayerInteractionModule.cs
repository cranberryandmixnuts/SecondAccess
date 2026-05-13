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

    private bool inputEnabled = true;

    public Interactable HoveredInteractable => interactionSource.HoveredInteractable;
    public Interactable ActiveInteractable => interactionSource.ActiveInteractable;

    private void Update()
    {
        if (!inputEnabled)
            return;

        interactionSource.RefreshHovered(Player.Camera, interactionMask, interactionDistance);

        if (!InputManager.Instance.InterActionHeld)
            interactionSource.EndActiveInteraction();

        if (InputManager.Instance.InterActionDown)
            interactionSource.TryStartCurrentInteraction();
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;

        if (inputEnabled)
            return;

        interactionSource.ClearHover();
        interactionSource.EndActiveInteraction();
    }

    public void ForceEndInteraction() => interactionSource.EndActiveInteraction();
}