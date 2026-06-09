using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(InteractionSource))]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public sealed class LinkEye : MonoBehaviour
{
    [SerializeField, Required, TitleGroup("References")]
    private InteractionSource interactionSource;

    [SerializeField, Required, TitleGroup("References")]
    private Rigidbody body;

    [SerializeField, TitleGroup("Movement"), MinValue(0f)]
    private float scrollSensitivity = 0.05f;

    [SerializeField, TitleGroup("Movement"), MinValue(0f)]
    private float minimumScrollMagnitude = 0.01f;

    [SerializeField, TitleGroup("Movement"), MinValue(0.001f)]
    private float moveSmoothTime = 0.15f;

    [SerializeField, TitleGroup("Movement"), MinValue(0f)]
    private float heightOffset = 0f;

    [SerializeField, TitleGroup("Camera")]
    private Vector3 cameraOffset = new(0f, 12f, -6f);

    public InteractionSource InteractionSource => interactionSource;
    public float NormalizedPosition { get; private set; }
    public bool IsPenaltyLocked { get; private set; }

    private NetworkPlayer ownerPlayer;
    private PlayerInteractionModule registeredInteractionModule;
    private Camera ownerCamera;
    private bool registered;
    private float targetNormalizedPosition;
    private float normalizedVelocity;

    private bool IsBound => ownerPlayer != null;

    private bool CanMove
    {
        get
        {
            LinkManager manager = LinkManager.Instance;
            return manager != null && manager.Mode.Value == LinkMode.Energy && !IsPenaltyLocked;
        }
    }

    private bool CanUseInteraction => !IsPenaltyLocked;

    private void Reset()
    {
        interactionSource = GetComponent<InteractionSource>();
        body = GetComponent<Rigidbody>();
    }

    private void Awake()
    {
        interactionSource = GetComponent<InteractionSource>();
        body = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        if (!IsBound)
            return;

        TryRegisterInteractionSource();
        RefreshInteractionEnabled();
        ApplyResolvedPosition();
    }

    private void Update()
    {
        if (!IsBound)
            return;

        TryRegisterInteractionSource();
        RefreshInteractionEnabled();
        UpdateMovementInput();
        UpdateSmoothPosition();
        ApplyResolvedPosition();
    }

    private void LateUpdate()
    {
        if (!IsBound)
            return;

        UpdateCamera();
    }

    private void OnDisable() => UnregisterInteractionSource();

    private void OnDestroy() => UnregisterInteractionSource();

    public void Bind(NetworkPlayer owner)
    {
        ownerPlayer = owner;
        ownerCamera = owner.Player.Camera;
        NormalizedPosition = 0f;
        targetNormalizedPosition = 0f;
        normalizedVelocity = 0f;
        IsPenaltyLocked = false;

        TryRegisterInteractionSource();
        RefreshInteractionEnabled();
        ApplyResolvedPosition();
        UpdateCamera();
    }

    public void SetPenaltyLocked(bool locked)
    {
        IsPenaltyLocked = locked;

        if (!IsBound)
            return;

        if (IsPenaltyLocked)
            SnapNormalizedPosition(0f);

        RefreshInteractionEnabled();
        ApplyResolvedPosition();
        UpdateCamera();
    }

    private void UpdateMovementInput()
    {
        if (!CanMove)
        {
            SnapNormalizedPosition(0f);
            return;
        }

        float scroll = Mouse.current.scroll.ReadValue().y;

        if (Mathf.Abs(scroll) <= minimumScrollMagnitude)
            return;

        targetNormalizedPosition = Mathf.Clamp01(targetNormalizedPosition + scroll * scrollSensitivity);
    }

    private void UpdateSmoothPosition()
    {
        if (!CanMove)
            return;

        NormalizedPosition = Mathf.SmoothDamp(
            NormalizedPosition,
            targetNormalizedPosition,
            ref normalizedVelocity,
            moveSmoothTime
        );
    }

    private void SnapNormalizedPosition(float value)
    {
        NormalizedPosition = value;
        targetNormalizedPosition = value;
        normalizedVelocity = 0f;
    }

    private void TryRegisterInteractionSource()
    {
        if (registered)
            return;

        if (!IsBound)
            return;

        registeredInteractionModule = ownerPlayer.Interaction;
        registeredInteractionModule.RegisterInteractionSource(interactionSource);
        registered = true;
    }

    private void UnregisterInteractionSource()
    {
        if (!registered)
            return;

        if (registeredInteractionModule != null)
            registeredInteractionModule.UnregisterInteractionSource(interactionSource);

        registeredInteractionModule = null;
        registered = false;
    }

    private void RefreshInteractionEnabled()
    {
        if (interactionSource.enabled == CanUseInteraction)
            return;

        interactionSource.enabled = CanUseInteraction;
    }

    private void ApplyResolvedPosition()
    {
        if (!TryResolvePosition(out Vector3 position))
            return;

        transform.position = position;
    }

    private bool TryResolvePosition(out Vector3 position)
    {
        position = transform.position;

        LinkManager manager = LinkManager.Instance;

        if (manager == null)
        {
            position = ownerPlayer.transform.position + Vector3.up * heightOffset;
            return true;
        }

        if (!manager.TryGetRegisteredPlayers(out NetworkPlayer firstPlayer, out _))
        {
            position = ownerPlayer.transform.position + Vector3.up * heightOffset;
            return true;
        }

        if (!manager.IsRegisteredPlayer(ownerPlayer))
        {
            position = ownerPlayer.transform.position + Vector3.up * heightOffset;
            return true;
        }

        if (!manager.TryGetLinkPath(out Vector3 firstPosition, out Vector3 relayPosition, out Vector3 secondPosition, out bool usesRelay))
        {
            position = ownerPlayer.transform.position + Vector3.up * heightOffset;
            return true;
        }

        bool ownerIsFirst = ownerPlayer.NetworkObjectId == firstPlayer.NetworkObjectId;

        Vector3 start = ownerIsFirst ? firstPosition : secondPosition;
        Vector3 end = ownerIsFirst ? secondPosition : firstPosition;

        position = usesRelay
            ? ResolveSegmentedPosition(start, relayPosition, end, NormalizedPosition)
            : Vector3.Lerp(start, end, NormalizedPosition);

        position += Vector3.up * heightOffset;
        return true;
    }

    private Vector3 ResolveSegmentedPosition(Vector3 start, Vector3 middle, Vector3 end, float t)
    {
        float firstLength = Vector3.Distance(start, middle);
        float secondLength = Vector3.Distance(middle, end);
        float totalLength = firstLength + secondLength;

        if (totalLength <= 0.0001f)
            return start;

        float targetDistance = Mathf.Clamp01(t) * totalLength;

        if (targetDistance <= firstLength)
        {
            if (firstLength <= 0.0001f)
                return middle;

            return Vector3.Lerp(start, middle, targetDistance / firstLength);
        }

        if (secondLength <= 0.0001f)
            return end;

        return Vector3.Lerp(middle, end, (targetDistance - firstLength) / secondLength);
    }

    private void UpdateCamera()
    {
        ownerCamera.transform.position = transform.position + cameraOffset;

        Vector3 lookDirection = transform.position - ownerCamera.transform.position;

        if (lookDirection.sqrMagnitude <= 0.0001f)
            return;

        ownerCamera.transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
    }
}