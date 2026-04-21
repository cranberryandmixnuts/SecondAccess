using Sirenix.OdinInspector;
using UnityEngine;

public sealed class PlayerInteractionModule : PlayerModule
{
    [SerializeField, Required, TitleGroup("References")]
    private InteractionSource interactionSource;

    [SerializeField, Required, TitleGroup("References")]
    private Camera interactionCamera;

    [SerializeField,TitleGroup("Raycast")]
    private LayerMask interactionMask = ~0;

    [SerializeField, MinValue(0f), TitleGroup("Raycast")]
    private float interactionDistance = 100f;

    public Interactable HoveredInteractable => interactionSource.HoveredInteractable;
    public Interactable ActiveInteractable => interactionSource.ActiveInteractable;

    private void Reset()
    {
        interactionSource = GetComponentInChildren<InteractionSource>();
        interactionCamera = Camera.main;
    }

    private void Update()
    {
        interactionSource.RefreshHovered(interactionCamera, interactionMask, interactionDistance);

        if (!InputManager.Instance.InterActionHeld)
            interactionSource.EndActiveInteraction();

        if (InputManager.Instance.InterActionDown)
            interactionSource.TryStartCurrentInteraction();
    }

    public void SetInteractionCamera(Camera targetCamera) => interactionCamera = targetCamera;

    public void ForceEndInteraction() => interactionSource.EndActiveInteraction();
}
