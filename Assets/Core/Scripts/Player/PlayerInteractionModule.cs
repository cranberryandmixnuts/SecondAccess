using Sirenix.OdinInspector;
using UnityEngine;

public sealed class PlayerInteractionModule : PlayerModule
{
    [TitleGroup("References")]
    [SerializeField, Required]
    private InteractionSource interactionSource;

    [TitleGroup("References")]
    [SerializeField]
    private Camera interactionCamera;

    [TitleGroup("Raycast")]
    [SerializeField]
    private LayerMask interactionMask = ~0;

    [TitleGroup("Raycast")]
    [SerializeField, MinValue(0f)]
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
