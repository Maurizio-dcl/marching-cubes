using UnityEngine;

namespace DefaultNamespace.Water
{
    [System.Serializable]
    public sealed class WaterSimulationSettings
    {
        [Header("Grid")]
        [Range(1, 128)] public int cellsPerTerrainChunkAxis = 32;
        [Min(0.05f)] public float groundSearchStep = 0.25f;
        [Min(0f)] public float groundSearchPadding = 1f;

        [Header("Simulation")]
        [Min(0.001f)] public float fixedSimulationStep = 0.03333334f;
        [Range(1, 8)] public int simulationSubsteps = 2;
        [Min(0f)] public float flowRate = 0.45f;
        [Min(0f)] public float maximumFlowPerStep = 0.35f;
        [Min(0f)] public float minimumFlowHeightDifference = 0.015f;
        [Min(0f)] public float minimumWaterDepth = 0.001f;
        [Min(0f)] public float settleDepthDelta = 0.0001f;

        [Header("Rendering")]
        [Min(0f)] public float renderDepthThreshold = 0.015f;
        [Range(0, 2)] public int shorelineSkirtCells = 1;
        [Min(0f)] public float shorelineOverlap = 0.02f;
        [Range(0f, 1f)] public float renderSmoothing = 0.35f;
        [Min(0f)] public float waterfallDropHeight = 5f;
        [Min(0.001f)] public float waterfallWidth = 0.75f;
        [Min(0f)] public float waterfallMinimumOutflow = 0.0005f;
        [Range(0, 128)] public int maxWaterfallQuads = 48;
    }
}
