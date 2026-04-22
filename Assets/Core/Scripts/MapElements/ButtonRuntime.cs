using System.Collections.Generic;
using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;

[RequireComponent(typeof(Interactable))]
[RequireComponent(typeof(Trigger))]
public sealed class ButtonRuntime : MonoBehaviour
{
    private Interactable interactable;
    private Trigger trigger;

    [SerializeField, TitleGroup("Visual")]
    private Transform visualRoot;

    [SerializeField, TitleGroup("Visual")]
    private Vector3 pressedLocalOffset = new(0f, -0.05f, 0f);

    [SerializeField, TitleGroup("Visual"), MinValue(0f)]
    private float pressTweenDuration = 0.08f;

    [SerializeField, TitleGroup("Visual")]
    private Ease pressEase = Ease.OutQuad;

    public bool IsPressed => activeSources.Count > 0;

    public int ActiveSourceCount => activeSources.Count;

    private readonly HashSet<InteractionSource> activeSources = new();

    private Tween pressTween;
    private Vector3 releasedLocalPosition;
    private string sustainSourceKey;

    private void Awake()
    {
        interactable = GetComponent<Interactable>();
        trigger = GetComponent<Trigger>();

        releasedLocalPosition = visualRoot.localPosition;
        sustainSourceKey = GetInstanceID().ToString();

        interactable.InteractionStarted += OnInteractionStarted;
        interactable.InteractionPerformed += OnInteractionPerformed;
        interactable.InteractionEnded += OnInteractionEnded;
        interactable.InteractionEnabledChanged += OnInteractionEnabledChanged;
    }

    private void Start() => UpdateVisualState(true);

    private void OnDisable()
    {
        ReleaseAllSources();
        KillPressTween();

        if (visualRoot != null)
            visualRoot.localPosition = releasedLocalPosition;
    }

    private void OnDestroy()
    {
        interactable.InteractionStarted -= OnInteractionStarted;
        interactable.InteractionPerformed -= OnInteractionPerformed;
        interactable.InteractionEnded -= OnInteractionEnded;
        interactable.InteractionEnabledChanged -= OnInteractionEnabledChanged;
    }

    private void OnInteractionStarted(InteractionSource source)
    {
        if (interactable.ExecutionType != InteractionExecutionType.Hold)
            return;

        if (!activeSources.Add(source))
            return;

        trigger.BeginSustain(source, sustainSourceKey);
        UpdateVisualState(false);
    }

    private void OnInteractionPerformed(InteractionSource source)
    {
        if (interactable.ExecutionType != InteractionExecutionType.Instant)
            return;

        trigger.TriggerOnce();
        PlayInstantPressPulse();
    }

    private void OnInteractionEnded(InteractionSource source)
    {
        if (interactable.ExecutionType != InteractionExecutionType.Hold)
            return;

        if (!activeSources.Remove(source))
            return;

        trigger.EndSustain(source, sustainSourceKey);
        UpdateVisualState(false);
    }

    private void OnInteractionEnabledChanged(bool enabled)
    {
        if (enabled)
            return;

        ReleaseAllSources();
        UpdateVisualState(false);
    }

    private void ReleaseAllSources()
    {
        if (activeSources.Count == 0)
            return;

        foreach (InteractionSource source in activeSources)
            trigger.EndSustain(source, sustainSourceKey);

        activeSources.Clear();
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