using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(TriggerTarget))]
public sealed class ClearSceneOnTriggerTarget : NetworkBehaviour
{
    private TriggerTarget triggerTarget;
    private bool clearRequested;

    private void Awake()
    {
        triggerTarget = GetComponent<TriggerTarget>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        triggerTarget.TriggerStateChanged += OnTriggerStateChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
            return;

        triggerTarget.TriggerStateChanged -= OnTriggerStateChanged;
    }

    private void OnTriggerStateChanged(TriggerTarget target, bool previous, bool current)
    {
        if (clearRequested)
            return;

        if (!current)
            return;

        clearRequested = true;
        RequestClearScene();
    }

    private void RequestClearScene()
    {
        if (!MultiplayerRoomManager.HasInstance)
        {
            Debug.LogError("[SecondAccess] Clear trigger fired, but MultiplayerRoomManager does not exist.", this);
            return;
        }

        MultiplayerRoomManager.Instance.TryEndGameWithClearScene();
    }
}
