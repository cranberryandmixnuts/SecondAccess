using Sirenix.OdinInspector;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class PlayerMovementModule : PlayerModule
{
    [SerializeField, Required, TitleGroup("References")]
    private Rigidbody body;

    [SerializeField, Required, TitleGroup("References")]
    private Transform rotationRoot;

    [SerializeField, TitleGroup("References")]
    private Transform cameraTransform;

    [SerializeField, MinValue(0f), TitleGroup("Movement")]
    private float moveSpeed = 10f;

    [SerializeField, MinValue(0f), TitleGroup("Movement")]
    private float acceleration = 30f;

    [SerializeField, MinValue(0f), TitleGroup("Movement")]
    private float deceleration = 40f;

    [SerializeField, TitleGroup("Movement")]
    private bool useCameraRelativeMovement;

    [SerializeField, TitleGroup("Rotation")]
    private bool rotateToMovement = true;

    [SerializeField, MinValue(0f), TitleGroup("Rotation")]
    private float rotationSpeed = 1080f;

    public Vector2 Input { get; private set; }
    public Vector3 MoveDirection { get; private set; }
    public Vector3 DesiredVelocity { get; private set; }
    public Vector3 PlanarVelocity => new(body.linearVelocity.x, 0f, body.linearVelocity.z);

    private void Reset()
    {
        body = GetComponent<Rigidbody>();
        rotationRoot = transform;

        if (Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void Update()
    {
        Input = InputManager.Instance.Move;
        MoveDirection = ResolveMoveDirection(Input);
        DesiredVelocity = MoveDirection * moveSpeed;

        if (rotateToMovement)
            UpdateRotation(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        Vector3 currentPlanarVelocity = new(body.linearVelocity.x, 0f, body.linearVelocity.z);
        float deltaSpeed = DesiredVelocity.sqrMagnitude > currentPlanarVelocity.sqrMagnitude ? acceleration : deceleration;
        Vector3 nextPlanarVelocity = Vector3.MoveTowards(currentPlanarVelocity, DesiredVelocity, deltaSpeed * Time.fixedDeltaTime);
        body.linearVelocity = new Vector3(nextPlanarVelocity.x, body.linearVelocity.y, nextPlanarVelocity.z);
    }

    private Vector3 ResolveMoveDirection(Vector2 input)
    {
        Vector3 direction;

        if (useCameraRelativeMovement)
        {
            Vector3 forward = cameraTransform.forward;
            Vector3 right = cameraTransform.right;
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();
            direction = right * input.x + forward * input.y;
        }
        else
        {
            direction = new Vector3(input.x, 0f, input.y);
        }

        if (direction.sqrMagnitude > 1f)
            direction.Normalize();

        return direction;
    }

    private void UpdateRotation(float deltaTime)
    {
        Transform target = rotationRoot != null ? rotationRoot : transform;
        Vector3 direction = MoveDirection;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        target.rotation = Quaternion.RotateTowards(target.rotation, targetRotation, rotationSpeed * deltaTime);
    }
}
