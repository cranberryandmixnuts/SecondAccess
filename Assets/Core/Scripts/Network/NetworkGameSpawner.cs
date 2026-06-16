using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public sealed class NetworkGameSpawner : NetworkBehaviour
{
    [SerializeField, Required] private NetworkObject playerPrefab;
    [SerializeField, Required] private Transform[] spawnPoints;

    private bool hasSpawned;

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        SpawnPlayers();
    }

    [Button]
    private void SpawnPlayers()
    {
        if (hasSpawned)
            return;

        if (playerPrefab == null)
        {
            Debug.LogError("Player Prefab이 설정되지 않았습니다.", this);
            return;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("Spawn Points가 설정되지 않았습니다.", this);
            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        List<ulong> clientIds = new(networkManager.ConnectedClientsIds);

        for (int i = 0; i < clientIds.Count; i++)
        {
            ulong clientId = clientIds[i];

            if (networkManager.ConnectedClients[clientId].PlayerObject != null)
                continue;

            Transform spawnPoint = spawnPoints[Mathf.Min(i, spawnPoints.Length - 1)];
            NetworkObject player = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
            player.SpawnAsPlayerObject(clientId, true);
        }

        hasSpawned = true;
    }
}
