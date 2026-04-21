using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Triggerable))]
public sealed class DoorRuntime : MonoBehaviour
{
    [SerializeField, Required, TitleGroup("References")]
    private Triggerable triggerable;

    [SerializeField, Required, TitleGroup("References")]
    private Transform visualRoot;

    [SerializeField, Required, TitleGroup("References")]
    private List<Collider> blockingColliders = new();

    [SerializeField, TitleGroup("State")]
    private bool openWhenTriggered = true;

    [SerializeField, TitleGroup("State")]
    private bool disableCollidersWhenOpen = true;

    [SerializeField, TitleGroup("Motion")]
    private Vector3 openLocalPositionOffset = new(0f, 2f, 0f);

    [SerializeField, TitleGroup("Motion")]
    private Vector3 openLocalEulerOffset = Vector3.zero;

    [SerializeField, TitleGroup("Motion"), MinValue(0f)]
    private float transitionDuration = 0.25f;

    [SerializeField, TitleGroup("Motion")]
    private Ease transitionEase = Ease.OutCubic;

    public bool IsOpen { get; private set; }

    private Sequence transitionSequence;
    private Vector3 closedLocalPosition;
    private Quaternion closedLocalRotation;

    private void Reset()
    {
        triggerable = GetComponent<Triggerable>();
        visualRoot = transform;
        CacheBlockingColliders();
    }

    private void Awake()
    {
        if (visualRoot == null)
            visualRoot = transform;

        closedLocalPosition = visualRoot.localPosition;
        closedLocalRotation = visualRoot.localRotation;

        if (blockingColliders.Count == 0)
            CacheBlockingColliders();
    }

    private void OnEnable()
    {
        triggerable.TriggerStateChanged += OnTriggerStateChanged;
        ApplyState(triggerable.IsTriggered, true);
    }

    private void OnDisable()
    {
        triggerable.TriggerStateChanged -= OnTriggerStateChanged;
        KillTransitionSequence();
    }

    private void OnTriggerStateChanged(Triggerable _, bool previous, bool current) => ApplyState(current, false);

    private void ApplyState(bool triggered, bool immediate)
    {
        bool nextOpen = triggered == openWhenTriggered;

        if (IsOpen == nextOpen && !immediate)
            return;

        IsOpen = nextOpen;

        if (disableCollidersWhenOpen)
            SetBlockingCollidersEnabled(!IsOpen);

        Vector3 targetPosition = IsOpen ? closedLocalPosition + openLocalPositionOffset : closedLocalPosition;
        Quaternion targetRotation = IsOpen
            ? closedLocalRotation * Quaternion.Euler(openLocalEulerOffset)
            : closedLocalRotation;

        KillTransitionSequence();

        if (immediate || transitionDuration <= 0f)
        {
            visualRoot.localPosition = targetPosition;
            visualRoot.localRotation = targetRotation;
            return;
        }

        transitionSequence = DOTween.Sequence().SetLink(gameObject);
        transitionSequence.Join(visualRoot.DOLocalMove(targetPosition, transitionDuration).SetEase(transitionEase));
        transitionSequence.Join(visualRoot.DOLocalRotateQuaternion(targetRotation, transitionDuration).SetEase(transitionEase));
    }

    private void SetBlockingCollidersEnabled(bool enabled)
    {
        for (int i = 0; i < blockingColliders.Count; i++)
        {
            Collider target = blockingColliders[i];

            if (target == null)
                continue;

            target.enabled = enabled;
        }
    }

    private void CacheBlockingColliders()
    {
        blockingColliders.Clear();
        GetComponentsInChildren(true, blockingColliders);
    }

    private void KillTransitionSequence()
    {
        if (transitionSequence == null)
            return;

        if (transitionSequence.IsActive())
            transitionSequence.Kill(false);

        transitionSequence = null;
    }
}