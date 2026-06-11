using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Interactable))]
public sealed class RepeaterRuntime : NetworkBehaviour
{
    [SerializeField] private Interactable interactable;
    [SerializeField] private NetworkObject repeaterObject;

    [ShowInInspector, ReadOnly]
    public bool IsConnected => LinkManager.Instance.IsRelayConnectedTo(repeaterObject);

    [ShowInInspector, ReadOnly]
    public bool IsInteractableAvailable => LinkManager.Instance.CanToggleRelay(repeaterObject);

    private bool availabilityInitialized;
    private bool lastAvailability;

    private void OnEnable()
    {
        interactable.InteractionPerformed += OnInteractionPerformed;
        availabilityInitialized = false;
    }

    private void Update() => RefreshAvailability();

    private void OnDisable() => interactable.InteractionPerformed -= OnInteractionPerformed;

    private new void OnDestroy()
    {
        base.OnDestroy();
        interactable.InteractionPerformed -= OnInteractionPerformed;
    }

    private void OnInteractionPerformed(InteractionSource source) => RequestToggleRelayRpc();

    private void RefreshAvailability()
    {
        bool available = IsInteractableAvailable;

        if (availabilityInitialized && lastAvailability == available)
            return;

        availabilityInitialized = true;
        lastAvailability = available;
        interactable.SetInteractionEnabled(available);
    }

    [Rpc(SendTo.Server)]
    private void RequestToggleRelayRpc()
    {
        LinkManager.Instance.TryToggleRelay(repeaterObject);
    }
}