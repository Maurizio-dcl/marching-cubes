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
}
