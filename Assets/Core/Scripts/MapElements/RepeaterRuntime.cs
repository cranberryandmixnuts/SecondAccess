using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Interactable))]
public sealed class RepeaterRuntime : NetworkBehaviour
{
    [SerializeField, Required]
    private Interactable interactable;

    [SerializeField, Required]
    private NetworkObject repeaterObject;

    [ShowInInspector, ReadOnly]
    public bool IsConnected => LinkManager.Instance != null && LinkManager.Instance.IsRelayConnectedTo(repeaterObject);

    [ShowInInspector, ReadOnly]
    public bool IsInteractableAvailable => LinkManager.Instance != null && LinkManager.Instance.CanToggleRelay(repeaterObject);

    private bool availabilityInitialized;
    private bool lastAvailability;

    private void Reset()
    {
        interactable = GetComponent<Interactable>();
        repeaterObject = GetComponent<NetworkObject>();
    }

    private void OnValidate()
    {
        if (interactable == null)
            interactable = GetComponent<Interactable>();

        if (repeaterObject == null)
            repeaterObject = GetComponent<NetworkObject>();
    }

    private void Awake()
    {
        if (interactable == null)
            interactable = GetComponent<Interactable>();

        if (repeaterObject == null)
            repeaterObject = GetComponent<NetworkObject>();
    }

    private void OnEnable()
    {
        interactable.InteractionStarted += OnInteractionStarted;
        availabilityInitialized = false;
    }

    private void Update() => RefreshAvailability();

    private void OnDisable()
    {
        interactable.InteractionStarted -= OnInteractionStarted;
    }

    private new void OnDestroy()
    {
        base.OnDestroy();

        if (interactable == null)
            return;

        interactable.InteractionStarted -= OnInteractionStarted;
    }

    private void OnInteractionStarted(InteractionSource source)
    {
        if (!IsSpawned)
            return;

        RequestToggleRelayRpc();
    }

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