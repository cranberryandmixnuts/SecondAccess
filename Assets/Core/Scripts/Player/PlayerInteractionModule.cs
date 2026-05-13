using Sirenix.OdinInspector;
using UnityEngine;

public sealed class PlayerInteractionModule : PlayerModule
{
    [SerializeField, Required, TitleGroup("References")]
    private InteractionSource interactionSource;

    [SerializeField,TitleGroup("Raycast")]
    private LayerMask interactionMask = ~0;

    [SerializeField, MinValue(0f), TitleGroup("Raycast")]
    private float interactionDistance = 100f;

    public Interactable HoveredInteractable => interactionSource.HoveredInteractable;
    public Interactable ActiveInteractable => interactionSource.ActiveInteractable;

    private void Update()
    {
        interactionSource.RefreshHovered(Player.Camera, interactionMask, interactionDistance);

        if (!InputManager.Instance.InterActionHeld)
            interactionSource.EndActiveInteraction();

        if (InputManager.Instance.InterActionDown)
            interactionSource.TryStartCurrentInteraction();
    }

    public void ForceEndInteraction() => interactionSource.EndActiveInteraction();
}
