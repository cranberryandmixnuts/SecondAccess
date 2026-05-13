using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerMovementModule))]
[RequireComponent(typeof(PlayerInteractionModule))]
public sealed class Player : NetworkBehaviour
{
    public Camera Camera { get; private set; }
    [field: SerializeField] public Rigidbody Body { get; private set; }
    [field: SerializeField] public Collider Collider { get; private set; }

    public PlayerMovementModule Movement { get; private set; }
    public PlayerInteractionModule Interaction { get; private set; }

    private void Awake()
    {
        Camera = Camera.main;

        Movement = GetComponent<PlayerMovementModule>();
        Interaction = GetComponent<PlayerInteractionModule>();
    }
}
