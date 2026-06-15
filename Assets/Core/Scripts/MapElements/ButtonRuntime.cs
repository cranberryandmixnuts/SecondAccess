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

    private readonly HashSet<string> activeSourceKeys = new();

    private readonly NetworkVariable<int> pressedSourceCount = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Tween pressTween;
    private Vector3 releasedLocalPosition;

    private void Reset()
    {
        interactable = GetComponent<Interactable>();
        trigger = GetComponent<Trigger>();
        visualRoot = transform;
    }

    private void OnValidate()
    {
        if (interactable == null)
            interactable = GetComponent<Interactable>();

        if (trigger == null)
            trigger = GetComponent<Trigger>();
    }

    private void Awake()
    {
        if (interactable == null)
            interactable = GetComponent<Interactable>();

        if (trigger == null)
            trigger = GetComponent<Trigger>();

        if (visualRoot != null)
            releasedLocalPosition = visualRoot.localPosition;

        interactable.InteractionStarted += OnInteractionStarted;
        interactable.InteractionEnded += OnInteractionEnded;
        interactable.InteractionEnabledChanged += OnInteractionEnabledChanged;
    }

    private void Start() => UpdateVisualState(true);

    public override void OnNetworkSpawn()
    {
        pressedSourceCount.OnValueChanged += OnPressedSourceCountChanged;
        UpdateVisualState(true);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
            ClearServerSustain();

        pressedSourceCount.OnValueChanged -= OnPressedSourceCountChanged;
    }

    private void OnDisable()
    {
        KillPressTween();

        if (visualRoot != null)
            visualRoot.localPosition = releasedLocalPosition;

        if (IsServer)
            ClearServerSustain();
    }

    private new void OnDestroy()
    {
        base.OnDestroy();

        if (interactable == null)
            return;

        interactable.InteractionStarted -= OnInteractionStarted;
        interactable.InteractionEnded -= OnInteractionEnded;
        interactable.InteractionEnabledChanged -= OnInteractionEnabledChanged;
    }

    private void OnInteractionStarted(InteractionSource source)
    {
        if (!IsSpawned)
            return;

        if (trigger.TriggerType == TriggerType.Single)
        {
            RequestSinglePressRpc();
            return;
        }

        RequestBeginSustainRpc(source.SourceKey);
    }

    private void OnInteractionEnded(InteractionSource source)
    {
        if (!IsSpawned)
            return;

        if (trigger.TriggerType != TriggerType.Hold)
            return;

        RequestEndSustainRpc(source.SourceKey);
    }

    private void OnInteractionEnabledChanged(bool enabled)
    {
        if (enabled)
            return;

        if (IsServer)
            ClearServerSustain();
    }

    private void OnPressedSourceCountChanged(int previous, int current)
    {
        UpdateVisualState(false);
    }

    [Rpc(SendTo.Server)]
    private void RequestSinglePressRpc()
    {
        if (trigger.TriggerType != TriggerType.Single)
            return;

        trigger.TriggerOnce();
        PlayInstantPressPulseRpc();
    }

    [Rpc(SendTo.Server)]
    private void RequestBeginSustainRpc(string sourceKey, RpcParams rpcParams = default)
    {
        if (trigger.TriggerType != TriggerType.Hold)
            return;

        string networkSourceKey = GetNetworkSourceKey(rpcParams.Receive.SenderClientId, sourceKey);

        if (!activeSourceKeys.Add(networkSourceKey))
            return;

        pressedSourceCount.Value = activeSourceKeys.Count;
        trigger.BeginSustain(this, networkSourceKey);
    }

    [Rpc(SendTo.Server)]
    private void RequestEndSustainRpc(string sourceKey, RpcParams rpcParams = default)
    {
        if (trigger.TriggerType != TriggerType.Hold)
            return;

        string networkSourceKey = GetNetworkSourceKey(rpcParams.Receive.SenderClientId, sourceKey);

        if (!activeSourceKeys.Remove(networkSourceKey))
            return;

        pressedSourceCount.Value = activeSourceKeys.Count;
        trigger.EndSustain(this, networkSourceKey);
    }

    [Rpc(SendTo.Everyone)]
    private void PlayInstantPressPulseRpc()
    {
        PlayInstantPressPulse();
    }

    private void ClearServerSustain()
    {
        if (activeSourceKeys.Count == 0)
            return;

        List<string> sourceKeys = new(activeSourceKeys);
        activeSourceKeys.Clear();

        for (int i = 0; i < sourceKeys.Count; i++)
            trigger.EndSustain(this, sourceKeys[i]);

        if (IsSpawned)
            pressedSourceCount.Value = 0;
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

    private string GetNetworkSourceKey(ulong clientId, string sourceKey) => clientId + ":" + sourceKey;
}