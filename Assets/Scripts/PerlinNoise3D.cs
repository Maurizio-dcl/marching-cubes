using UnityEngine;

public static class PerlinNoise3D
{
    private static readonly int[] Permutation =
    {
        151, 160, 137, 91, 90, 15,
        131, 13, 201, 95, 96, 53, 194, 233, 7, 225,
        140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10,
        23, 190, 6, 148, 247, 120, 234, 75, 0, 26, 197, 62,
        94, 252, 219, 203, 117, 35, 11, 32, 57, 177, 33,
        88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168,
        68, 175, 74, 165, 71, 134, 139, 48, 27, 166, 77,
        146, 158, 231, 83, 111, 229, 122, 60, 211, 133,
        230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102,
        143, 54, 65, 25, 63, 161, 1, 216, 80, 73, 209,
        76, 132, 187, 208, 89, 18, 169, 200, 196, 135,
        130, 116, 188, 159, 86, 164, 100, 109, 198, 173,
        186, 3, 64, 52, 217, 226, 250, 124, 123, 5, 202,
        38, 147, 118, 126, 255, 82, 85, 212, 207, 206,
        59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223,
        183, 170, 213, 119, 248, 152, 2, 44, 154, 163,
        70, 221, 153, 101, 155, 167, 43, 172, 9, 129,
        22, 39, 253, 19, 98, 108, 110, 79, 113, 224,
        232, 178, 185, 112, 104, 218, 246, 97, 228,
        251, 34, 242, 193, 238, 210, 144, 12, 191, 179,
        162, 241, 81, 51, 145, 235, 249, 14, 239, 107,
        49, 192, 214, 31, 181, 199, 106, 157, 184, 84,
        204, 176, 115, 121, 50, 45, 127, 4, 150, 254,
        138, 236, 205, 93, 222, 114, 67, 29, 24, 72,
        243, 141, 128, 195, 78, 66, 215, 61, 156, 180
    };

    private static readonly int[] P = new int[512];

    static PerlinNoise3D()
    {
        // Duplicating the permutation avoids having to wrap every lookup.
        for (int i = 0; i < P.Length; i++)
        {
            P[i] = Permutation[i & 255];
        }
    }

    /// <summary>
    /// Returns 3D Perlin noise approximately in the range [0, 1].
    /// </summary>
    public static float Sample(float x, float y, float z)
    {
        return Mathf.Clamp01(SampleSigned(x, y, z) * 0.5f + 0.5f);
    }

    public static float Sample(Vector3 position)
    {
        return Sample(position.x, position.y, position.z);
    }

    /// <summary>
    /// Returns 3D Perlin noise approximately in the range [-1, 1].
    /// </summary>
    public static float SampleSigned(float x, float y, float z)
    {
        int floorX = Mathf.FloorToInt(x);
        int floorY = Mathf.FloorToInt(y);
        int floorZ = Mathf.FloorToInt(z);

        int latticeX = floorX & 255;
        int latticeY = floorY & 255;
        int latticeZ = floorZ & 255;

        // Position inside the current lattice cell.
        float localX = x - floorX;
        float localY = y - floorY;
        float localZ = z - floorZ;

        float fadeX = Fade(localX);
        float fadeY = Fade(localY);
        float fadeZ = Fade(localZ);

        int a = P[latticeX] + latticeY;
        int aa = P[a] + latticeZ;
        int ab = P[a + 1] + latticeZ;

        int b = P[latticeX + 1] + latticeY;
        int ba = P[b] + latticeZ;
        int bb = P[b + 1] + latticeZ;

        float bottomFront = Mathf.Lerp(
            Gradient(P[aa], localX, localY, localZ),
            Gradient(P[ba], localX - 1f, localY, localZ),
            fadeX);

        float bottomBack = Mathf.Lerp(
            Gradient(P[ab], localX, localY - 1f, localZ),
            Gradient(P[bb], localX - 1f, localY - 1f, localZ),
            fadeX);

        float bottom = Mathf.Lerp(bottomFront, bottomBack, fadeY);

        float topFront = Mathf.Lerp(
            Gradient(P[aa + 1], localX, localY, localZ - 1f),
            Gradient(P[ba + 1], localX - 1f, localY, localZ - 1f),
            fadeX);

        float topBack = Mathf.Lerp(
            Gradient(P[ab + 1], localX, localY - 1f, localZ - 1f),
            Gradient(P[bb + 1], localX - 1f, localY - 1f, localZ - 1f),
            fadeX);

        float top = Mathf.Lerp(topFront, topBack, fadeY);

        return Mathf.Lerp(bottom, top, fadeZ);
    }

    /// <summary>
    /// Combines several frequencies of Perlin noise.
    /// Returns a normalized value in approximately [0, 1].
    /// </summary>
    public static float Fractal(
        Vector3 position,
        int octaves,
        float frequency = 1f,
        float persistence = 0.5f,
        float lacunarity = 2f)
    {
        if (octaves <= 0)
        {
            return 0f;
        }

        float value = 0f;
        float amplitude = 1f;
        float maximumAmplitude = 0f;

        for (int octave = 0; octave < octaves; octave++)
        {
            value += Sample(position * frequency) * amplitude;
            maximumAmplitude += amplitude;

            frequency *= lacunarity;
            amplitude *= persistence;
        }

        return value / maximumAmplitude;
    }

    private static float Fade(float value)
    {
        // 6t^5 - 15t^4 + 10t^3
        return value * value * value
             * (value * (value * 6f - 15f) + 10f);
    }

    private static float Gradient(int hash, float x, float y, float z)
    {
        int gradient = hash & 15;

        float u = gradient < 8 ? x : y;
        float v = gradient < 4
            ? y
            : gradient == 12 || gradient == 14
                ? x
                : z;

        float first = (gradient & 1) == 0 ? u : -u;
        float second = (gradient & 2) == 0 ? v : -v;

        return first + second;
    }
}