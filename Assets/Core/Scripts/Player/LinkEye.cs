using System.Collections.Generic;
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
    public bool IsLaserPenaltyActive => IsPenaltyLocked || IsEnergyLinkLaserized;

    private readonly List<Vector3> pathPoints = new();

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
            return manager != null && manager.Mode.Value == LinkMode.Energy && !IsLaserPenaltyActive;
        }
    }

    private bool CanUseInteraction => !IsLaserPenaltyActive;

    private bool IsEnergyLinkLaserized
    {
        get
        {
            LinkManager manager = LinkManager.Instance;
            return manager != null && manager.IsEnergyLinkLaserized;
        }
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

        if (IsLaserPenaltyActive)
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

        if (!manager.TryGetLinkPath(pathPoints))
        {
            position = ownerPlayer.transform.position + Vector3.up * heightOffset;
            return true;
        }

        bool ownerIsFirst = ownerPlayer.NetworkObjectId == firstPlayer.NetworkObjectId;
        position = ResolvePathPosition(pathPoints, NormalizedPosition, !ownerIsFirst) + Vector3.up * heightOffset;
        return true;
    }

    private Vector3 ResolvePathPosition(IReadOnlyList<Vector3> path, float t, bool reverse)
    {
        if (path.Count == 0)
            return ownerPlayer.transform.position;

        if (path.Count == 1)
            return path[0];

        float totalLength = 0f;

        for (int i = 1; i < path.Count; i++)
            totalLength += Vector3.Distance(path[i - 1], path[i]);

        if (totalLength <= 0.0001f)
            return reverse ? path[^1] : path[0];

        float targetDistance = Mathf.Clamp01(t) * totalLength;
        float walkedDistance = 0f;
        int startIndex = reverse ? path.Count - 1 : 0;
        int endIndex = reverse ? 0 : path.Count - 1;
        int step = reverse ? -1 : 1;

        for (int i = startIndex; i != endIndex; i += step)
        {
            Vector3 segmentStart = path[i];
            Vector3 segmentEnd = path[i + step];
            float segmentLength = Vector3.Distance(segmentStart, segmentEnd);

            if (segmentLength <= 0.0001f)
                continue;

            if (walkedDistance + segmentLength >= targetDistance)
                return Vector3.Lerp(segmentStart, segmentEnd, (targetDistance - walkedDistance) / segmentLength);

            walkedDistance += segmentLength;
        }

        return reverse ? path[0] : path[^1];
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