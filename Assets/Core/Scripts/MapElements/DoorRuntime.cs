using DG.Tweening;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(TriggerTarget))]
public sealed class DoorRuntime : MonoBehaviour
{
    private TriggerTarget triggerable;

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

    private void Awake()
    {
        triggerable = GetComponent<TriggerTarget>();

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

    private void OnTriggerStateChanged(TriggerTarget _, bool previous, bool current) => ApplyState(current, false);

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
            visualRoot.SetLocalPositionAndRotation(targetPosition, targetRotation);
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