using System.Collections.Generic;
using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;

[RequireComponent(typeof(Interactable))]
[RequireComponent(typeof(Trigger))]
public sealed class ButtonRuntime : MonoBehaviour
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

    public bool IsPressed => activeSources.Count > 0;

    public int ActiveSourceCount => activeSources.Count;

    private readonly HashSet<InteractionSource> activeSources = new();

    private Tween pressTween;
    private Vector3 releasedLocalPosition;
    private string sustainSourceKey;

    private void Reset()
    {
        interactable = GetComponent<Interactable>();
        trigger = GetComponent<Trigger>();
        visualRoot = transform;
    }

    private void Awake()
    {
        if (visualRoot == null)
            visualRoot = transform;

        releasedLocalPosition = visualRoot.localPosition;
        sustainSourceKey = GetInstanceID().ToString();

        interactable.InteractionStarted += OnInteractionStarted;
        interactable.InteractionEnded += OnInteractionEnded;
        interactable.InteractionEnabledChanged += OnInteractionEnabledChanged;
    }

    private void Start()
    {
        if (interactable.ExecutionType != InteractionExecutionType.Hold)
            Debug.LogWarning($"{name}의 Interactable.ExecutionType은 Hold여야 합니다.", this);

        UpdateVisualState(true);
    }

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
        interactable.InteractionEnded -= OnInteractionEnded;
        interactable.InteractionEnabledChanged -= OnInteractionEnabledChanged;
    }

    private void OnInteractionStarted(InteractionSource source)
    {
        if (!activeSources.Add(source))
            return;

        trigger.BeginSustain(source, sustainSourceKey);
        UpdateVisualState(false);
    }

    private void OnInteractionEnded(InteractionSource source)
    {
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

    private void KillPressTween()
    {
        if (pressTween == null)
            return;

        if (pressTween.IsActive())
            pressTween.Kill(false);

        pressTween = null;
    }
}