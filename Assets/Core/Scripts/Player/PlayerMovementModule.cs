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

    private bool ShouldSimulate => !Player.IsSpawned || Player.IsServer;

    private void FixedUpdate()
    {
        if (!SimulationEnabled)
            return;

        if (!ShouldSimulate)
            return;

        MoveDirection = ResolveMoveDirection(Input);
        DesiredVelocity = MoveDirection * moveSpeed;

        Player.Body.linearVelocity = new Vector3(DesiredVelocity.x, Player.Body.linearVelocity.y, DesiredVelocity.z);

        if (MoveDirection.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(MoveDirection, Vector3.up);
        Quaternion nextRotation = Quaternion.RotateTowards(Player.Body.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);

        Player.Body.MoveRotation(nextRotation);
    }

    public void SetInput(Vector2 input) => Player.SetMovementInput(input);

    internal void ApplyInputFromNetwork(Vector2 input) => Input = Vector2.ClampMagnitude(input, 1f);

    public void ApplyNetworkState()
    {
        Player.Body.isKinematic = Player.IsSpawned && !Player.IsServer;
    }

    public void ApplyLocalState()
    {
        Player.Body.isKinematic = false;
    }

    public void SetSimulationEnabled(bool enabled)
    {
        if (SimulationEnabled == enabled)
            return;

        SimulationEnabled = enabled;

        if (SimulationEnabled)
            return;

        Input = Vector2.zero;
        MoveDirection = Vector3.zero;
        DesiredVelocity = Vector3.zero;

        if (Player.IsServer || !Player.IsSpawned)
            Player.Body.linearVelocity = new Vector3(0f, Player.Body.linearVelocity.y, 0f);
    }

    private Vector3 ResolveMoveDirection(Vector2 input)
    {
        Vector3 direction = new(input.x, 0f, input.y);
        direction.Normalize();

        return direction;
    }
}