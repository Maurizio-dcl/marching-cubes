using System;
using UnityEngine;

namespace DefaultNamespace.Terrain
{
    [Serializable]
    public sealed class TerrainLodLevel
    {
        [Min(0f)] public float enterDistance = 24f;
        [Min(0f)] public float exitDistance = 28f;
        [Range(1, 64)] public int terrainCellsPerAxis = 16;
        [Range(1, 128)] public int waterCellsPerTerrainChunkAxis = 32;
        [Min(1)] public int meshUpdateIntervalFrames = 1;
        [Min(1)] public int simulationIntervalFrames = 1;
        public bool generateCollision;
        public bool simulateWater = true;
        public bool renderWater = true;
        public bool castShadows = true;
    }

    public readonly struct TerrainLodDecision
    {
        public TerrainLodDecision(int lod, bool render, bool simulateWater, bool renderWater, bool visible, float distance)
        {
            Lod = lod;
            Render = render;
            SimulateWater = simulateWater;
            RenderWater = renderWater;
            Visible = visible;
            Distance = distance;
        }

        public int Lod { get; }
        public bool Render { get; }
        public bool SimulateWater { get; }
        public bool RenderWater { get; }
        public bool Visible { get; }
        public float Distance { get; }
    }

    public sealed class TerrainLodSelector
    {
        private readonly TerrainLodLevel[] _levels;
        private readonly int _recentVisibilityFrames;

        public TerrainLodSelector(TerrainLodLevel[] levels, int recentVisibilityFrames)
        {
            _levels = levels != null && levels.Length > 0
                ? levels
                : new[] { new TerrainLodLevel() };
            _recentVisibilityFrames = Mathf.Max(0, recentVisibilityFrames);
        }

        public TerrainLodLevel GetLevel(int lod)
        {
            return _levels[Mathf.Clamp(lod, 0, _levels.Length - 1)];
        }

        public TerrainLodDecision Evaluate(
            TerrainChunkRuntimeData chunk,
            Camera camera,
            Plane[] frustumPlanes,
            Vector3 focusPosition,
            int currentFrame)
        {
            Bounds bounds = chunk.Bounds;
            Vector3 point = bounds.ClosestPoint(focusPosition);
            float distance = Vector3.Distance(focusPosition, point);
            bool inFrustum = camera == null || GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
            bool behindCamera = false;

            if (camera != null)
            {
                Vector3 toChunk = bounds.center - camera.transform.position;
                behindCamera = Vector3.Dot(camera.transform.forward, toChunk) < -bounds.extents.magnitude;
            }

            bool visible = inFrustum && !behindCamera;
            bool recent = visible || currentFrame - chunk.LastVisibleFrame <= _recentVisibilityFrames;
            int lod = SelectLod(chunk.CurrentLod, distance, chunk.IsBeingModified);
            TerrainLodLevel level = GetLevel(lod);
            bool render = visible || recent || chunk.IsBeingModified;

            return new TerrainLodDecision(lod, render, level.simulateWater || chunk.IsBeingModified, level.renderWater && render, visible, distance);
        }

        private int SelectLod(int currentLod, float distance, bool forceHighResolution)
        {
            if (forceHighResolution)
            {
                return 0;
            }

            int selected = _levels.Length - 1;

            for (int i = 0; i < _levels.Length; i++)
            {
                TerrainLodLevel level = _levels[i];
                float threshold = currentLod == i ? level.exitDistance : level.enterDistance;

                if (distance <= threshold)
                {
                    selected = i;
                    break;
                }
            }

            return selected;
        }
    }
}
