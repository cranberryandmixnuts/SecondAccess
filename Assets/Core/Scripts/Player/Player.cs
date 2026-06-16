using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerMovementModule))]
[RequireComponent(typeof(PlayerInteractionModule))]
public sealed class Player : NetworkBehaviour
{
    public Camera Camera { get; private set; }

    [field: SerializeField] public Rigidbody Body { get; private set; }
    [field: SerializeField] public Collider Collider { get; private set; }
    [field: SerializeField] public Animator Animator { get; private set; }

    public PlayerMovementModule Movement { get; private set; }
    public PlayerInteractionModule Interaction { get; private set; }

    private void Awake()
    {
        Camera = Camera.main;

        Movement = GetComponent<PlayerMovementModule>();
        Interaction = GetComponent<PlayerInteractionModule>();
    }

    public override void OnNetworkSpawn()
    {
        Movement.ApplyNetworkState();
        Interaction.SetInputEnabled(IsOwner);
    }

    public override void OnNetworkDespawn()
    {
        Movement.ApplyLocalState();
        Interaction.SetInputEnabled(true);
    }

    public void SetMovementInput(Vector2 input)
    {
        Vector2 clampedInput = Vector2.ClampMagnitude(input, 1f);

        if (!IsSpawned)
        {
            Movement.ApplyInputFromNetwork(clampedInput);
            return;
        }

        if (IsServer)
        {
            Movement.ApplyInputFromNetwork(clampedInput);
            return;
        }

        if (!IsOwner)
            return;

        SetMovementInputServerRpc(clampedInput);
    }

    [ServerRpc]
    private void SetMovementInputServerRpc(Vector2 input)
    {
        Movement.ApplyInputFromNetwork(Vector2.ClampMagnitude(input, 1f));
    }
}