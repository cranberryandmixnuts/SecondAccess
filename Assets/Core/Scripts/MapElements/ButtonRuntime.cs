using System.Collections.Generic;
using DG.Tweening;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Interactable))]
[RequireComponent(typeof(Trigger))]
public sealed class ButtonRuntime : NetworkBehaviour
{
    [SerializeField, Required, TitleGroup("References")]
    private Interactable interactable;

    [SerializeField, Required, TitleGroup("References")]
    private Trigger trigger;

    [SerializeField, TitleGroup("Visual")]
    private Transform visualRoot;

    [SerializeField, TitleGroup("Visual")]
    private Vector3 pressedLocalOffset = new(0f, -0.05f, 0f);

    [SerializeField, TitleGroup("Visual"), MinValue(0f)]
    private float pressTweenDuration = 0.08f;

    [SerializeField, TitleGroup("Visual")]
    private Ease pressEase = Ease.OutQuad;

    public bool IsPressed => pressedSourceCount.Value > 0;

    public int ActiveSourceCount => pressedSourceCount.Value;

    private readonly HashSet<ulong> activeClientIds = new();

    private readonly NetworkVariable<int> pressedSourceCount = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Tween pressTween;
    private Vector3 releasedLocalPosition;

    private void Awake()
    {
        interactable.InteractionStarted += OnInteractionStarted;
        interactable.InteractionPerformed += OnInteractionPerformed;
        interactable.InteractionEnded += OnInteractionEnded;
        interactable.InteractionEnabledChanged += OnInteractionEnabledChanged;
    }

    private void Start() => UpdateVisualState(true);

    public override void OnNetworkSpawn()
    {
        pressedSourceCount.OnValueChanged += OnPressedSourceCountChanged;
        UpdateVisualState(true);
    }

    public override void OnNetworkDespawn() => pressedSourceCount.OnValueChanged -= OnPressedSourceCountChanged;

    private void OnDisable()
    {
        KillPressTween();

        if (visualRoot != null)
            visualRoot.localPosition = releasedLocalPosition;
    }

    private new void OnDestroy()
    {
        base.OnDestroy();

        interactable.InteractionStarted -= OnInteractionStarted;
        interactable.InteractionPerformed -= OnInteractionPerformed;
        interactable.InteractionEnded -= OnInteractionEnded;
        interactable.InteractionEnabledChanged -= OnInteractionEnabledChanged;
    }

    private void OnInteractionStarted(InteractionSource source)
    {
        if (interactable.ExecutionType != InteractionExecutionType.Hold)
            return;

        RequestBeginHoldRpc();
    }

    private void OnInteractionPerformed(InteractionSource source)
    {
        if (interactable.ExecutionType != InteractionExecutionType.Instant)
            return;

        RequestInstantPressRpc();
    }

    private void OnInteractionEnded(InteractionSource source)
    {
        if (interactable.ExecutionType != InteractionExecutionType.Hold)
            return;

        RequestEndHoldRpc();
    }

    private void OnInteractionEnabledChanged(bool enabled)
    {
        if (enabled)
            return;

        if (IsSpawned)
            RequestEndHoldRpc();
    }

    private void OnPressedSourceCountChanged(int previous, int current)
    {
        UpdateVisualState(false);
    }

    [Rpc(SendTo.Server)]
    private void RequestInstantPressRpc()
    {
        trigger.TriggerOnce();
        PlayInstantPressPulseRpc();
    }

    [Rpc(SendTo.Server)]
    private void RequestBeginHoldRpc(RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        if (!activeClientIds.Add(clientId))
            return;

        pressedSourceCount.Value = activeClientIds.Count;
        trigger.BeginSustain(this, clientId.ToString());
    }

    [Rpc(SendTo.Server)]
    private void RequestEndHoldRpc(RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        if (!activeClientIds.Remove(clientId))
            return;

        pressedSourceCount.Value = activeClientIds.Count;
        trigger.EndSustain(this, clientId.ToString());
    }

    [Rpc(SendTo.Everyone)]
    private void PlayInstantPressPulseRpc()
    {
        PlayInstantPressPulse();
    }

    private void UpdateVisualState(bool immediate)
    {
        if (visualRoot == null)
            return;

        Vector3 targetPosition = IsPressed ? releasedLocalPosition + pressedLocalOffset : releasedLocalPosition;

        KillPressTween();

        if (immediate || pressTweenDuration <= 0f)
        {
            visualRoot.localPosition = targetPosition;
            return;
        }

        pressTween = visualRoot
            .DOLocalMove(targetPosition, pressTweenDuration)
            .SetEase(pressEase)
            .SetLink(gameObject);
    }

    private void PlayInstantPressPulse()
    {
        if (visualRoot == null)
            return;

        KillPressTween();

        Vector3 pressedPosition = releasedLocalPosition + pressedLocalOffset;

        if (pressTweenDuration <= 0f)
        {
            visualRoot.localPosition = releasedLocalPosition;
            return;
        }

        Sequence sequence = DOTween.Sequence().SetLink(gameObject);
        sequence.Append(visualRoot.DOLocalMove(pressedPosition, pressTweenDuration).SetEase(pressEase));
        sequence.Append(visualRoot.DOLocalMove(releasedLocalPosition, pressTweenDuration).SetEase(pressEase));
        pressTween = sequence;
    }

    private void KillPressTween()
    {
        if (pressTween == null)
            return;

        if (pressTween.IsActive())
            pressTween.Kill(false);

        pressTween = null;
    }
}