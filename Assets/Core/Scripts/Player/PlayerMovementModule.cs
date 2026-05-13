using Sirenix.OdinInspector;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class PlayerMovementModule : PlayerModule
{
    [SerializeField, MinValue(0f), TitleGroup("Movement")]
    private float moveSpeed = 10f;

    [SerializeField, MinValue(0f), TitleGroup("Rotation")]
    private float rotationSpeed = 1080f;

    public Vector2 Input { get; private set; }
    public Vector3 MoveDirection { get; private set; }
    public Vector3 DesiredVelocity { get; private set; }
    public Vector3 PlanarVelocity => new(Player.Body.linearVelocity.x, 0f, Player.Body.linearVelocity.z);
    public bool SimulationEnabled { get; private set; } = true;

    private void Update()
    {
        if (!SimulationEnabled) return;

        MoveDirection = ResolveMoveDirection(Input);
        DesiredVelocity = MoveDirection * moveSpeed;

        Vector3 direction = MoveDirection;
        if (direction.sqrMagnitude <= 0.0001f) return;
        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);

        Debug.Log(Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime));
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (!SimulationEnabled) return;

        Player.Body.linearVelocity = new Vector3(DesiredVelocity.x, Player.Body.linearVelocity.y, DesiredVelocity.z);
    }

    public void SetInput(Vector2 input) => Input = Vector2.ClampMagnitude(input, 1f);

    public void SetSimulationEnabled(bool enabled)
    {
        if (SimulationEnabled == enabled) return;

        SimulationEnabled = enabled;

        if (SimulationEnabled) return;

        Input = Vector2.zero;
        MoveDirection = Vector3.zero;
        DesiredVelocity = Vector3.zero;
        if (Player.IsServer) Player.Body.linearVelocity = new Vector3(0f, Player.Body.linearVelocity.y, 0f);
    }

    private Vector3 ResolveMoveDirection(Vector2 input)
    {
        Vector3 direction;

        direction = new Vector3(input.x, 0f, input.y);
        direction.Normalize();

        return direction;
    }
}