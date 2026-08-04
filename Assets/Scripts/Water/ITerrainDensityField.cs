using UnityEngine;

namespace DefaultNamespace.Water
{
    public interface ITerrainDensityField
    {
        float IsoLevel { get; }
        Bounds WorldBounds { get; }
        float SampleDensity(Vector3 worldPosition);
    }
}
