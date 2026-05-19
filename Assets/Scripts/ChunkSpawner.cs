using System.Collections.Generic;
using UnityEngine;

public class ChunkSpawner : MonoBehaviour
{
    [Header("Chunk Settings")]
    [SerializeField] private GameObject[] chunkPrefabs;
    [SerializeField] private float chunkLength = 18f;
    [SerializeField] private int chunksOnScreen = 4;

    [Header("Background Chunk Settings")]
    [SerializeField] private GameObject[] backgroundChunkPrefabs;
    [SerializeField] private float backgroundChunkLength = 112f;
    [SerializeField] private int backgroundChunksOnScreen = 2;
    [SerializeField] private Vector3 backgroundOffset = Vector3.zero;

    [Header("Movement")]
    [SerializeField] private float speed = 6f;

    [Header("Spawn / Despawn")]
    [SerializeField] private float despawnZ = -25f;
    [SerializeField] private float backgroundDespawnZ = -112f;

    private readonly List<GameObject> activeChunks = new List<GameObject>();
    private readonly List<GameObject> activeBackgroundChunks = new List<GameObject>();

    private float nextSpawnZ = 0f;
    private float nextBackgroundSpawnZ = 0f;

    private void Start()
    {
        for (int i = 0; i < chunksOnScreen; i++)
        {
            SpawnChunk();
        }

        for (int i = 0; i < backgroundChunksOnScreen; i++)
        {
            SpawnBackgroundChunk();
        }
    }

    private void Update()
    {
        MoveChunks();
        RemoveOldChunks();

        while (activeChunks.Count < chunksOnScreen)
        {
            SpawnChunk();
        }

        while (activeBackgroundChunks.Count < backgroundChunksOnScreen)
        {
            SpawnBackgroundChunk();
        }
    }

    private void SpawnChunk()
    {
        if (chunkPrefabs == null || chunkPrefabs.Length == 0)
        {
            Debug.LogWarning("No chunk prefabs assigned.");
            return;
        }

        int randomIndex = Random.Range(0, chunkPrefabs.Length);
        GameObject prefab = chunkPrefabs[randomIndex];

        Vector3 spawnPosition = new Vector3(0f, 0f, nextSpawnZ);
        GameObject chunk = Instantiate(prefab, spawnPosition, Quaternion.identity, transform);

        activeChunks.Add(chunk);
        nextSpawnZ += chunkLength;
    }

    private void SpawnBackgroundChunk()
    {
        if (backgroundChunkPrefabs == null || backgroundChunkPrefabs.Length == 0)
        {
            return;
        }

        int randomIndex = Random.Range(0, backgroundChunkPrefabs.Length);
        GameObject prefab = backgroundChunkPrefabs[randomIndex];

        Vector3 spawnPosition = new Vector3(0f, 0f, nextBackgroundSpawnZ) + backgroundOffset;
        GameObject backgroundChunk = Instantiate(prefab, spawnPosition, Quaternion.identity, transform);

        activeBackgroundChunks.Add(backgroundChunk);
        nextBackgroundSpawnZ += backgroundChunkLength;
    }

    private void MoveChunks()
    {
        float moveAmount = speed * Time.deltaTime;

        for (int i = 0; i < activeChunks.Count; i++)
        {
            activeChunks[i].transform.position += Vector3.back * moveAmount;
        }

        for (int i = 0; i < activeBackgroundChunks.Count; i++)
        {
            activeBackgroundChunks[i].transform.position += Vector3.back * moveAmount;
        }

        nextSpawnZ -= moveAmount;
        nextBackgroundSpawnZ -= moveAmount;
    }

    private void RemoveOldChunks()
    {
        for (int i = activeChunks.Count - 1; i >= 0; i--)
        {
            GameObject chunk = activeChunks[i];

            if (chunk.transform.position.z <= despawnZ)
            {
                activeChunks.RemoveAt(i);
                Destroy(chunk);
            }
        }

        for (int i = activeBackgroundChunks.Count - 1; i >= 0; i--)
        {
            GameObject backgroundChunk = activeBackgroundChunks[i];

            if (backgroundChunk.transform.position.z <= backgroundDespawnZ)
            {
                activeBackgroundChunks.RemoveAt(i);
                Destroy(backgroundChunk);
            }
        }
    }
}