using System;
using UnityEngine;

namespace DefaultNamespace.Terrain
{
    [Serializable]
    public sealed class IslandChunkDebugSettings
    {
        public bool enabled;
        public bool drawOnlySelected = true;
        public bool drawBounds = true;
        public bool drawSamplePoints;
        public bool drawCells;
        public bool drawNormals;
        [Min(1)] public int maxSamples = 512;
        [Min(1)] public int maxCells = 256;
        [Min(1)] public int maxNormals = 256;
        [Min(0.005f)] public float sampleRadius = 0.035f;
        public Color boundsColor = Color.yellow;
        public Color insideSampleColor = Color.green;
        public Color outsideSampleColor = Color.red;
        public Color cellColor = Color.gray;
        public Color normalColor = Color.cyan;
    }
}
