using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Interactable))]
public sealed class LinkModeConverterRuntime : NetworkBehaviour
{
    [SerializeField, Required]
    private Interactable interactable;

    [SerializeField, TitleGroup("Converter")]
    private LinkMode targetMode = LinkMode.Rope;

    [SerializeField, TitleGroup("Availability")]
    private bool updateInteractableAvailability = true;

    private bool availabilityInitialized;
    private bool lastAvailability;

    private void Reset()
    {
        interactable = GetComponent<Interactable>();
    }

    private void OnValidate()
    {
        if (interactable == null)
            interactable = GetComponent<Interactable>();
    }

    private void Awake()
    {
        if (interactable == null)
            interactable = GetComponent<Interactable>();
    }

    private void OnEnable()
    {
        interactable.InteractionStarted += OnInteractionStarted;
        availabilityInitialized = false;
    }

    private void OnDisable()
    {
        interactable.InteractionStarted -= OnInteractionStarted;
    }

    private void Update()
    {
        if (!updateInteractableAvailability)
            return;

        RefreshAvailability();
    }

    private void OnInteractionStarted(InteractionSource source)
    {
        if (!IsSpawned)
            return;

        RequestConvertRpc();
    }

    private void RefreshAvailability()
    {
        bool available = LinkManager.Instance != null && LinkManager.Instance.CanConvertTo(targetMode);

        if (availabilityInitialized && lastAvailability == available)
            return;

        availabilityInitialized = true;
        lastAvailability = available;
        interactable.SetInteractionEnabled(available);
    }

    [Rpc(SendTo.Server)]
    private void RequestConvertRpc()
    {
        LinkManager.Instance.TrySetMode(targetMode);
    }
}