using UnityEngine;

namespace DefaultNamespace.Water
{
    public struct WaterOutflow
    {
        public Vector3 Position;
        public Vector2 Direction;
        public float Amount;
    }

    public interface IWaterOutflowConsumer
    {
        void ConsumeOutflows(WaterOutflow[] outflows, int count);
    }

    public readonly struct WaterSource
    {
        public WaterSource(Vector2 worldXZ, float radius, float volumePerSecond, float maximumSurfaceHeight)
        {
            WorldXZ = worldXZ;
            Radius = radius;
            VolumePerSecond = volumePerSecond;
            MaximumSurfaceHeight = maximumSurfaceHeight;
        }

        public Vector2 WorldXZ { get; }
        public float Radius { get; }
        public float VolumePerSecond { get; }
        public float MaximumSurfaceHeight { get; }
    }

    public readonly struct LakeWaterBody
    {
        public LakeWaterBody(Vector2 worldXZ, float radius, float shoreWidth, float surfaceHeight)
        {
            WorldXZ = worldXZ;
            Radius = radius;
            ShoreWidth = shoreWidth;
            SurfaceHeight = surfaceHeight;
        }

        public Vector2 WorldXZ { get; }
        public float Radius { get; }
        public float ShoreWidth { get; }
        public float SurfaceHeight { get; }
    }

    public readonly struct RiverWaterBody
    {
        public RiverWaterBody(
            Vector2 startWorldXZ,
            Vector2 endWorldXZ,
            float width,
            float startSurfaceHeight,
            float endSurfaceHeight,
            Vector2 noiseOriginWorldXZ,
            Vector2 noiseSeedOffsets,
            float meanderFrequency,
            float meanderStrength)
        {
            StartWorldXZ = startWorldXZ;
            EndWorldXZ = endWorldXZ;
            Width = width;
            StartSurfaceHeight = startSurfaceHeight;
            EndSurfaceHeight = endSurfaceHeight;
            NoiseOriginWorldXZ = noiseOriginWorldXZ;
            NoiseSeedOffsets = noiseSeedOffsets;
            MeanderFrequency = meanderFrequency;
            MeanderStrength = meanderStrength;
        }

        public Vector2 StartWorldXZ { get; }
        public Vector2 EndWorldXZ { get; }
        public float Width { get; }
        public float StartSurfaceHeight { get; }
        public float EndSurfaceHeight { get; }
        public Vector2 NoiseOriginWorldXZ { get; }
        public Vector2 NoiseSeedOffsets { get; }
        public float MeanderFrequency { get; }
        public float MeanderStrength { get; }
    }
}
