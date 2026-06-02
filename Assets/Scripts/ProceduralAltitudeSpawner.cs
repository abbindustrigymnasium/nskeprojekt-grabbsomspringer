using System.Collections.Generic;
using UnityEngine;

public class ProceduralAltitudeSpawner : MonoBehaviour
{
    private enum ChallengeType
    {
        Recovery,
        StepUp,
        LongGap,
        DoubleJump,
        DropRoll,
        SplitPath,
        BarSwing
    }

    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private PlayerController playerController;

    [Header("World Movement")]
    [SerializeField] private float worldSpeed = 6f;

    [Header("Bar Tracking")]
    [SerializeField] private bool trackYDuringBar = false;
    [SerializeField] private bool trackBackgroundDuringBar = true;
    [SerializeField] private float maxBarTrackingStep = 0.5f;

    [Header("Platform Visuals")]
    [SerializeField] private Material platformMaterial;
    [SerializeField] private Material ceilingMaterial;
    [SerializeField] private float platformHeight = 1f;
    [SerializeField] private float platformWidthX = 5f;

    [Header("Generation")]
    [SerializeField] private float spawnAheadDistance = 90f;
    [SerializeField] private float despawnBehindDistance = 35f;
    [SerializeField] private int maxGenerationAttempts = 30;
    [SerializeField] private int startGenerationSafetyLimit = 100;
    [SerializeField] private int updateGenerationSafetyLimit = 20;

    [Header("Platform Size")]
    [SerializeField] private float minPlatformLength = 4f;
    [SerializeField] private float maxPlatformLength = 9f;
    [SerializeField] private float recoveryPlatformLength = 12f;

    [Header("Gaps")]
    [SerializeField] private float easyGapMin = 2.2f;
    [SerializeField] private float easyGapMax = 4.2f;
    [SerializeField] private float mediumGapMin = 3.5f;
    [SerializeField] private float mediumGapMax = 6.2f;
    [SerializeField] private float hardGapMin = 5.5f;
    [SerializeField] private float hardGapMax = 8.5f;

    [Header("Altitude")]
    [SerializeField] private float upwardBias = 0.25f;
    [SerializeField] private float stepUpMin = 0.75f;
    [SerializeField] private float stepUpMax = 2.25f;
    [SerializeField] private float dropMin = 3f;
    [SerializeField] private float dropMax = 5f;

    [Header("Vertical Split Path")]
    [SerializeField] private float splitPathPlatformLength = 6f;
    [SerializeField] private int splitPathSegments = 3;

    [Header("Roll / Low Ceiling")]
    [SerializeField] private bool addLowCeilingAfterRollDrop = true;
    [SerializeField] private float lowCeilingExtraMargin = 0.15f;
    [SerializeField] private float lowCeilingLength = 8f;
    [SerializeField] private float lowCeilingTopBlockHeight = 8f;

    [Header("Roll Tunnel Timing")]
    [SerializeField] private float rollLandingBufferDistance = 6f;
    [SerializeField] private float rollExitBufferDistance = 3f;

    [Header("Bar Swing")]
    [SerializeField] private GameObject barPrefab;
    [SerializeField] private float barGap = 5f;
    [SerializeField] private float barPlatformLength = 8f;
    [SerializeField] private float barHeightAboveStart = 3.2f;
    [SerializeField] private float barZOffsetFromGapStart = 2.2f;
    [SerializeField] private float barLandingHeightChange = 1.5f;

    [Header("Reachability Simulation")]
    [SerializeField] private float simulationTimeStep = 0.02f;
    [SerializeField] private float landingTolerance = 0.2f;
    [SerializeField] private float maxSimulationTime = 8f;

    [Header("Difficulty")]
    [SerializeField] private float difficultyRampAltitude = 120f;

    [Header("Background Chunk Settings")]
    [SerializeField] private GameObject[] backgroundChunkPrefabs;
    [SerializeField] private float backgroundChunkLength = 112f;
    [SerializeField] private int backgroundChunksOnScreen = 2;
    [SerializeField] private Vector3 backgroundOffset = Vector3.zero;
    [SerializeField] private float backgroundDespawnBehindDistance = 120f;

    [Header("Debug")]
    [SerializeField] private bool debugSpawnBarsOften = false;
    [SerializeField] private int debugBarEverySections = 2;

    private readonly List<GameObject> activeGameplayObjects = new List<GameObject>();
    private readonly List<GameObject> activeBackgroundChunks = new List<GameObject>();

    private float nextPlatformStartZ;
    private float currentPlatformTopY;
    private float currentPlatformCenterX;
    private float nextBackgroundSpawnZ;
    private int generatedSections;

    private bool isBarTracking;
    private Transform trackedBarAttachPoint;
    private Transform trackedLeftHand;
    private Transform trackedRightHand;

    private void Start()
    {
        if (!ValidateSetup())
            return;

        CreateStartPlatform();
        GenerateUntilAhead(startGenerationSafetyLimit);

        for (int i = 0; i < backgroundChunksOnScreen; i++)
        {
            SpawnBackgroundChunk();
        }
    }

    private void Update()
    {
        if (isBarTracking)
        {
            MoveWorldToTrackBar();
        }
        else
        {
            MoveWorldNormally();
        }

        GenerateUntilAhead(updateGenerationSafetyLimit);

        RemoveOldGameplayObjects();
        RemoveOldBackgroundChunks();

        while (activeBackgroundChunks.Count < backgroundChunksOnScreen)
        {
            SpawnBackgroundChunk();
        }
    }

    public void BeginBarTracking(Transform barAttachPoint, Transform leftHand, Transform rightHand)
    {
        if (barAttachPoint == null || leftHand == null || rightHand == null)
            return;

        trackedBarAttachPoint = barAttachPoint;
        trackedLeftHand = leftHand;
        trackedRightHand = rightHand;
        isBarTracking = true;
    }

    public void EndBarTracking()
    {
        isBarTracking = false;
        trackedBarAttachPoint = null;
        trackedLeftHand = null;
        trackedRightHand = null;
    }

    private bool ValidateSetup()
    {
        if (player == null)
        {
            Debug.LogError("No player assigned to ProceduralAltitudeSpawner.");
            enabled = false;
            return false;
        }

        if (playerController == null)
        {
            playerController = player.GetComponent<PlayerController>();
        }

        if (playerController == null)
        {
            Debug.LogError("No PlayerController found on player.");
            enabled = false;
            return false;
        }

        if (worldSpeed <= 0.01f)
        {
            Debug.LogError("World speed is too small.");
            enabled = false;
            return false;
        }

        return true;
    }

    private void GenerateUntilAhead(int safetyLimit)
    {
        int safety = 0;

        while (nextPlatformStartZ < spawnAheadDistance && safety < safetyLimit)
        {
            GenerateNextSection();
            safety++;
        }

        if (safety >= safetyLimit)
        {
            Debug.LogWarning("Generation safety limit hit. Check platform/gap/jump settings.");
        }
    }

    private void MoveWorldNormally()
    {
        Vector3 move = Vector3.back * (worldSpeed * Time.deltaTime);
        MoveGameplayObjects(move);
        MoveBackgroundObjects(move);

        nextPlatformStartZ += move.z;
        nextBackgroundSpawnZ += move.z;
    }

    private void MoveWorldToTrackBar()
    {
        if (trackedBarAttachPoint == null || trackedLeftHand == null || trackedRightHand == null)
        {
            EndBarTracking();
            return;
        }

        Vector3 handCenter = (trackedLeftHand.position + trackedRightHand.position) * 0.5f;
        Vector3 correction = handCenter - trackedBarAttachPoint.position;

        correction.x = 0f;

        if (!trackYDuringBar)
            correction.y = 0f;

        correction = Vector3.ClampMagnitude(correction, maxBarTrackingStep);

        MoveGameplayObjects(correction);

        if (trackBackgroundDuringBar)
            MoveBackgroundObjects(correction);

        nextPlatformStartZ += correction.z;
        nextBackgroundSpawnZ += correction.z;

        if (trackYDuringBar)
            currentPlatformTopY += correction.y;
    }

    private void MoveGameplayObjects(Vector3 movement)
    {
        for (int i = activeGameplayObjects.Count - 1; i >= 0; i--)
        {
            if (activeGameplayObjects[i] == null)
            {
                activeGameplayObjects.RemoveAt(i);
                continue;
            }

            activeGameplayObjects[i].transform.position += movement;
        }
    }

    private void MoveBackgroundObjects(Vector3 movement)
    {
        for (int i = activeBackgroundChunks.Count - 1; i >= 0; i--)
        {
            if (activeBackgroundChunks[i] == null)
            {
                activeBackgroundChunks.RemoveAt(i);
                continue;
            }

            activeBackgroundChunks[i].transform.position += movement;
        }
    }

    private void CreateStartPlatform()
    {
        currentPlatformTopY = 0f;
        currentPlatformCenterX = 0f;
        nextPlatformStartZ = 0f;

        CreatePlatform(0f, 0f, 16f, 0f, "Start_Platform");

        nextPlatformStartZ = 16f;
    }

    private void GenerateNextSection()
    {
        generatedSections++;

        ChallengeType type = PickChallengeType();
        bool generated = false;

        switch (type)
        {
            case ChallengeType.Recovery:
                generated = GenerateRecovery();
                break;

            case ChallengeType.StepUp:
                generated = GenerateStepUp();
                break;

            case ChallengeType.LongGap:
                generated = GenerateLongGap();
                break;

            case ChallengeType.DoubleJump:
                generated = GenerateDoubleJump();
                break;

            case ChallengeType.DropRoll:
                generated = GenerateDropRoll();
                break;

            case ChallengeType.SplitPath:
                generated = GenerateSplitPath();
                break;

            case ChallengeType.BarSwing:
                generated = GenerateBarSwing();
                break;
        }

        if (!generated)
            generated = GenerateRecovery();

        if (!generated)
            ForceCreateSafePlatform();
    }

    private ChallengeType PickChallengeType()
    {
        float difficulty = GetDifficulty();

        if (debugSpawnBarsOften && barPrefab != null)
        {
            if (generatedSections > 2 && generatedSections % debugBarEverySections == 0)
                return ChallengeType.BarSwing;
        }

        if (generatedSections < 4)
        {
            return Random.value < 0.7f ? ChallengeType.Recovery : ChallengeType.StepUp;
        }

        float recoveryWeight = Mathf.Lerp(0.35f, 0.15f, difficulty);
        float stepUpWeight = Mathf.Lerp(0.35f, 0.22f, difficulty);
        float longGapWeight = Mathf.Lerp(0.15f, 0.22f, difficulty);
        float doubleJumpWeight = Mathf.Lerp(0.05f, 0.18f, difficulty);
        float dropRollWeight = Mathf.Lerp(0.05f, 0.13f, difficulty);
        float splitPathWeight = Mathf.Lerp(0.05f, 0.10f, difficulty);

        float barSwingWeight = barPrefab != null
            ? Mathf.Lerp(0.03f, 0.14f, difficulty)
            : 0f;

        float total =
            recoveryWeight +
            stepUpWeight +
            longGapWeight +
            doubleJumpWeight +
            dropRollWeight +
            splitPathWeight +
            barSwingWeight;

        float roll = Random.Range(0f, total);

        if ((roll -= recoveryWeight) <= 0f) return ChallengeType.Recovery;
        if ((roll -= stepUpWeight) <= 0f) return ChallengeType.StepUp;
        if ((roll -= longGapWeight) <= 0f) return ChallengeType.LongGap;
        if ((roll -= doubleJumpWeight) <= 0f) return ChallengeType.DoubleJump;
        if ((roll -= dropRollWeight) <= 0f) return ChallengeType.DropRoll;
        if ((roll -= splitPathWeight) <= 0f) return ChallengeType.SplitPath;

        return ChallengeType.BarSwing;
    }

    private float GetDifficulty()
    {
        float altitude = Mathf.Max(0f, currentPlatformTopY);
        return Mathf.Clamp01(altitude / difficultyRampAltitude);
    }

    private bool GenerateRecovery()
    {
        float gap = Random.Range(1.5f, 3f);
        float length = recoveryPlatformLength;
        float heightChange = Random.Range(-0.5f, 0.75f) + upwardBias * 0.5f;

        return TryCreateReachablePlatform(gap, length, heightChange, currentPlatformCenterX, "Recovery");
    }

    private bool GenerateStepUp()
    {
        float gap = Random.Range(easyGapMin, easyGapMax);
        float length = Random.Range(minPlatformLength, maxPlatformLength);
        float heightChange = Random.Range(stepUpMin, stepUpMax) + upwardBias;

        return TryCreateReachablePlatform(gap, length, heightChange, currentPlatformCenterX, "Step_Up");
    }

    private bool GenerateLongGap()
    {
        for (int attempt = 0; attempt < maxGenerationAttempts; attempt++)
        {
            float gap = Random.Range(mediumGapMin, hardGapMax);
            float length = Random.Range(minPlatformLength, maxPlatformLength);
            float heightChange = Random.Range(-1f, 1.25f) + upwardBias;

            if (TryCreateReachablePlatform(gap, length, heightChange, currentPlatformCenterX, "Long_Gap"))
                return true;
        }

        return false;
    }

    private bool GenerateDoubleJump()
    {
        if (playerController.MaxJumps < 2)
            return false;

        for (int attempt = 0; attempt < maxGenerationAttempts; attempt++)
        {
            float gap = Random.Range(hardGapMin, hardGapMax);
            float length = Random.Range(minPlatformLength, maxPlatformLength * 0.9f);
            float heightChange = Random.Range(1.5f, 3.8f) + upwardBias;

            if (TryCreateReachablePlatform(gap, length, heightChange, currentPlatformCenterX, "Double_Jump"))
                return true;
        }

        return false;
    }

    private bool GenerateDropRoll()
    {
        float gap = Random.Range(2f, 4f);
        float dropAmount = Random.Range(dropMin, dropMax);
        float heightChange = -dropAmount;

        float length =
            rollLandingBufferDistance +
            lowCeilingLength +
            rollExitBufferDistance;

        float platformStartZ = nextPlatformStartZ + gap;

        bool created = TryCreateReachablePlatform(
            gap,
            length,
            heightChange,
            currentPlatformCenterX,
            "Drop_Roll_Platform"
        );

        if (!created)
            return false;

        if (addLowCeilingAfterRollDrop)
        {
            float ceilingStartZ = platformStartZ + rollLandingBufferDistance;

            CreateLowCeiling(
                currentPlatformCenterX,
                ceilingStartZ,
                lowCeilingLength,
                currentPlatformTopY
            );
        }

        return true;
    }

    private bool GenerateSplitPath()
    {
        float originalTopY = currentPlatformTopY;

        float entryGap = Random.Range(2.5f, 4.5f);
        float branchStartZ = nextPlatformStartZ + entryGap;
        float branchLength = splitPathPlatformLength;

        float upperHeightChange = Random.Range(2.0f, 4.0f);
        float lowerHeightChange = -Random.Range(2.5f, 5.0f);

        bool upperReachable = CanLandOnPlatform(entryGap, entryGap + branchLength, upperHeightChange);
        bool lowerReachable = lowerHeightChange < -1.0f;

        if (!upperReachable || !lowerReachable)
            return false;

        float upperTopY = originalTopY + upperHeightChange;
        float lowerTopY = originalTopY + lowerHeightChange;

        CreatePlatform(currentPlatformCenterX, branchStartZ, branchLength, upperTopY, "Split_Upper_Entry");
        CreatePlatform(currentPlatformCenterX, branchStartZ, branchLength, lowerTopY, "Split_Lower_Entry");

        float sectionEndZ = branchStartZ + branchLength;

        for (int i = 1; i < splitPathSegments; i++)
        {
            float segmentGap = Random.Range(2.5f, 4.5f);
            float segmentLength = Random.Range(4.5f, 7f);

            sectionEndZ += segmentGap;

            upperTopY += Random.Range(-0.5f, 1.2f);
            lowerTopY += Random.Range(-0.8f, 1.0f);

            CreatePlatform(currentPlatformCenterX, sectionEndZ, segmentLength, upperTopY, $"Split_Upper_{i}");
            CreatePlatform(currentPlatformCenterX, sectionEndZ, segmentLength, lowerTopY, $"Split_Lower_{i}");

            sectionEndZ += segmentLength;
        }

        float mergeGap = Random.Range(2.5f, 4.5f);
        float mergeLength = Random.Range(9f, 13f);

        float mergeStartZ = sectionEndZ + mergeGap;
        float mergeTopY = Mathf.Lerp(lowerTopY, upperTopY, 0.55f);

        CreatePlatform(currentPlatformCenterX, mergeStartZ, mergeLength, mergeTopY, "Split_Merge");

        nextPlatformStartZ = mergeStartZ + mergeLength;
        currentPlatformTopY = mergeTopY;

        return true;
    }

    private bool GenerateBarSwing()
    {
        if (barPrefab == null)
            return false;

        float gap = barGap;
        float length = barPlatformLength;
        float heightChange = barLandingHeightChange;

        bool landingReachable = CanLandOnPlatform(gap, gap + length, heightChange);

        if (!landingReachable)
            return false;

        float platformStartZ = nextPlatformStartZ + gap;
        float landingTopY = currentPlatformTopY + heightChange;

        float barZ = nextPlatformStartZ + barZOffsetFromGapStart;
        float barY = currentPlatformTopY + barHeightAboveStart;

        CreateBar(currentPlatformCenterX, barZ, barY);

        CreatePlatform(
            currentPlatformCenterX,
            platformStartZ,
            length,
            landingTopY,
            "Bar_Swing_Landing"
        );

        nextPlatformStartZ = platformStartZ + length;
        currentPlatformTopY = landingTopY;

        return true;
    }

    private bool TryCreateReachablePlatform(float gap, float length, float heightChange, float centerX, string name)
    {
        bool reachable = CanLandOnPlatform(gap, gap + length, heightChange);

        if (!reachable)
            return false;

        float startZ = nextPlatformStartZ + gap;
        float topY = currentPlatformTopY + heightChange;

        CreatePlatform(centerX, startZ, length, topY, name);

        nextPlatformStartZ = startZ + length;
        currentPlatformTopY = topY;
        currentPlatformCenterX = centerX;

        return true;
    }

    private void ForceCreateSafePlatform()
    {
        float gap = 2f;
        float length = recoveryPlatformLength;
        float heightChange = 0f;

        float startZ = nextPlatformStartZ + gap;
        float topY = currentPlatformTopY + heightChange;

        CreatePlatform(currentPlatformCenterX, startZ, length, topY, "Forced_Safe_Platform");

        nextPlatformStartZ = startZ + length;
        currentPlatformTopY = topY;
    }

    private bool CanLandOnPlatform(float gapStart, float gapEnd, float heightDifference)
    {
        if (worldSpeed <= 0.01f)
            return false;

        float jumpVelocity = playerController.JumpVelocity;
        float gravity = playerController.Gravity;
        int maxJumps = Mathf.Max(1, playerController.MaxJumps);

        float[] secondJumpTimes =
        {
            -1f,
            0.35f,
            0.55f,
            0.75f,
            1.0f,
            1.25f
        };

        foreach (float secondJumpTime in secondJumpTimes)
        {
            if (secondJumpTime > 0f && maxJumps < 2)
                continue;

            if (SimulateJumpLanding(
                gapStart,
                gapEnd,
                heightDifference,
                worldSpeed,
                jumpVelocity,
                gravity,
                secondJumpTime
            ))
            {
                return true;
            }
        }

        return false;
    }

    private bool SimulateJumpLanding(
        float gapStart,
        float gapEnd,
        float heightDifference,
        float speed,
        float jumpVelocity,
        float gravity,
        float secondJumpTime
    )
    {
        if (simulationTimeStep <= 0.001f)
            return false;

        float y = 0f;
        float previousY = 0f;
        float verticalVelocity = jumpVelocity;
        bool usedSecondJump = false;

        float maxTime = Mathf.Min((gapEnd / speed) + 2f, maxSimulationTime);

        for (float t = 0f; t <= maxTime; t += simulationTimeStep)
        {
            if (!usedSecondJump && secondJumpTime > 0f && t >= secondJumpTime)
            {
                verticalVelocity = jumpVelocity;
                usedSecondJump = true;
            }

            previousY = y;

            verticalVelocity += gravity * simulationTimeStep;
            y += verticalVelocity * simulationTimeStep;

            float z = speed * t;

            bool insidePlatformZ = z >= gapStart && z <= gapEnd;
            bool falling = verticalVelocity <= 0f;

            bool crossedPlatformTop =
                previousY >= heightDifference + landingTolerance &&
                y <= heightDifference + landingTolerance;

            if (insidePlatformZ && crossedPlatformTop && falling)
                return true;

            if (z > gapEnd && y < heightDifference - 3f)
                return false;
        }

        return false;
    }

    private void CreatePlatform(float centerX, float startZ, float length, float topY, string name)
    {
        GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name = name;
        platform.transform.SetParent(transform);

        platform.transform.position = new Vector3(
            centerX,
            topY - platformHeight * 0.5f,
            startZ + length * 0.5f
        );

        platform.transform.localScale = new Vector3(
            platformWidthX,
            platformHeight,
            length
        );

        if (platformMaterial != null)
            platform.GetComponent<Renderer>().material = platformMaterial;

        activeGameplayObjects.Add(platform);
    }

    private void CreateLowCeiling(float centerX, float startZ, float length, float platformTopY)
    {
        float rollClearance = playerController.RollTotalClearance + lowCeilingExtraMargin;
        float ceilingBottomY = platformTopY + rollClearance;
        float blockCenterY = ceilingBottomY + lowCeilingTopBlockHeight * 0.5f;

        GameObject ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ceiling.name = "Roll_Tunnel_Block";
        ceiling.transform.SetParent(transform);

        ceiling.transform.position = new Vector3(
            centerX,
            blockCenterY,
            startZ + length * 0.5f
        );

        ceiling.transform.localScale = new Vector3(
            platformWidthX,
            lowCeilingTopBlockHeight,
            length
        );

        if (ceilingMaterial != null)
            ceiling.GetComponent<Renderer>().material = ceilingMaterial;
        else if (platformMaterial != null)
            ceiling.GetComponent<Renderer>().material = platformMaterial;

        activeGameplayObjects.Add(ceiling);
    }

    private void CreateBar(float centerX, float z, float y)
    {
        if (barPrefab == null)
            return;

        GameObject bar = Instantiate(
            barPrefab,
            new Vector3(centerX, y, z),
            Quaternion.identity,
            transform
        );

        bar.name = "Bar_Swing";
        activeGameplayObjects.Add(bar);
    }

    private void RemoveOldGameplayObjects()
    {
        for (int i = activeGameplayObjects.Count - 1; i >= 0; i--)
        {
            GameObject obj = activeGameplayObjects[i];

            if (obj == null)
            {
                activeGameplayObjects.RemoveAt(i);
                continue;
            }

            float halfLength = Mathf.Abs(obj.transform.localScale.z) * 0.5f;
            float backEdgeZ = obj.transform.position.z + halfLength;

            if (backEdgeZ < -despawnBehindDistance)
            {
                activeGameplayObjects.RemoveAt(i);
                Destroy(obj);
            }
        }
    }

    private void SpawnBackgroundChunk()
    {
        if (backgroundChunkPrefabs == null || backgroundChunkPrefabs.Length == 0)
            return;

        int randomIndex = Random.Range(0, backgroundChunkPrefabs.Length);
        GameObject prefab = backgroundChunkPrefabs[randomIndex];

        Vector3 spawnPosition = new Vector3(
            0f,
            0f,
            nextBackgroundSpawnZ
        ) + backgroundOffset;

        GameObject backgroundChunk = Instantiate(
            prefab,
            spawnPosition,
            Quaternion.identity,
            transform
        );

        activeBackgroundChunks.Add(backgroundChunk);
        nextBackgroundSpawnZ += backgroundChunkLength;
    }

    private void RemoveOldBackgroundChunks()
    {
        for (int i = activeBackgroundChunks.Count - 1; i >= 0; i--)
        {
            GameObject backgroundChunk = activeBackgroundChunks[i];

            if (backgroundChunk == null)
            {
                activeBackgroundChunks.RemoveAt(i);
                continue;
            }

            if (backgroundChunk.transform.position.z < -backgroundDespawnBehindDistance)
            {
                activeBackgroundChunks.RemoveAt(i);
                Destroy(backgroundChunk);
            }
        }
    }
}