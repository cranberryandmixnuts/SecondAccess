using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(Rigidbody))]
public sealed class SecondAccessNetworkPlayer : NetworkBehaviour
{
    [SerializeField, Required, TitleGroup("References")]
    private Player player;

    [SerializeField, Required, TitleGroup("References")]
    private Rigidbody body;

    [SerializeField, Required, TitleGroup("References")]
    private PlayerMovementModule movement;

    [SerializeField, Required, TitleGroup("References")]
    private PlayerInteractionModule interaction;

    [SerializeField, TitleGroup("Owner Only")]
    private Behaviour[] ownerOnlyBehaviours;

    [SerializeField, TitleGroup("Owner Only")]
    private GameObject[] ownerOnlyObjects;

    [SerializeField, MinValue(0.01f), TitleGroup("Network Input")]
    private float inputSendInterval = 0.033f;

    public NetworkVariable<int> PlayerSlot { get; } = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public Player Player => player;
    public Rigidbody Body => body;
    public PlayerMovementModule Movement => movement;

    private Vector2 lastSentInput;
    private float lastInputSendTime;

    private void Reset()
    {
        player = GetComponent<Player>();
        body = GetComponent<Rigidbody>();
        movement = GetComponent<PlayerMovementModule>();
        interaction = GetComponent<PlayerInteractionModule>();
    }

    private void Awake()
    {
        player = GetComponent<Player>();
        body = GetComponent<Rigidbody>();
        movement = GetComponent<PlayerMovementModule>();
        interaction = GetComponent<PlayerInteractionModule>();
    }

    public override void OnNetworkSpawn()
    {
        ApplyAuthorityState();

        if (IsServer)
            SecondAccessLinkManager.Instance.RegisterPlayer(this);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
            SecondAccessLinkManager.Instance.UnregisterPlayer(this);
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        if (!IsOwner)
            return;

        Vector2 input = InputManager.Instance.Move;
        bool changed = (input - lastSentInput).sqrMagnitude >= 0.0001f;
        bool intervalPassed = Time.unscaledTime - lastInputSendTime >= inputSendInterval;

        if (!changed && !intervalPassed)
            return;

        SubmitMoveInputRpc(input);
        lastSentInput = input;
        lastInputSendTime = Time.unscaledTime;
    }

    public void SetPlayerSlot(int slot)
    {
        if (!IsServer)
            return;

        PlayerSlot.Value = slot;
    }

    private void ApplyAuthorityState()
    {
        body.isKinematic = !IsServer;
        movement.SetSimulationEnabled(IsServer);
        SetOwnerOnlyActive(IsOwner);

        if (!IsOwner)
            interaction.ForceEndInteraction();

        interaction.enabled = IsOwner;
    }

    private void SetOwnerOnlyActive(bool active)
    {
        for (int i = 0; i < ownerOnlyBehaviours.Length; i++)
            ownerOnlyBehaviours[i].enabled = active;

        for (int i = 0; i < ownerOnlyObjects.Length; i++)
            ownerOnlyObjects[i].SetActive(active);
    }

    [Rpc(SendTo.Server)]
    private void SubmitMoveInputRpc(Vector2 input, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        movement.SetInput(input);
    }
}
