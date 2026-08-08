using UnityEngine;

public static class SimplexNoise3D
{
    private const float F3 = 1f / 3f;
    private const float G3 = 1f / 6f;

    private static readonly int[] Gradients =
    {
        1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1, 0,
        1, 0, 1, -1, 0, 1, 1, 0, -1, -1, 0, -1,
        0, 1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1
    };

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

    static SimplexNoise3D()
    {
        for (int i = 0; i < P.Length; i++)
        {
            P[i] = Permutation[i & 255];
        }
    }

    public static float Sample(float x, float y, float z)
    {
        return Mathf.Clamp01(SampleSigned(x, y, z) * 0.5f + 0.5f);
    }

    public static float SampleSigned(float x, float y, float z)
    {
        float skew = (x + y + z) * F3;
        int i = Mathf.FloorToInt(x + skew);
        int j = Mathf.FloorToInt(y + skew);
        int k = Mathf.FloorToInt(z + skew);

        float unskew = (i + j + k) * G3;
        float x0 = x - (i - unskew);
        float y0 = y - (j - unskew);
        float z0 = z - (k - unskew);

        int i1;
        int j1;
        int k1;
        int i2;
        int j2;
        int k2;

        if (x0 >= y0)
        {
            if (y0 >= z0)
            {
                i1 = 1; j1 = 0; k1 = 0;
                i2 = 1; j2 = 1; k2 = 0;
            }
            else if (x0 >= z0)
            {
                i1 = 1; j1 = 0; k1 = 0;
                i2 = 1; j2 = 0; k2 = 1;
            }
            else
            {
                i1 = 0; j1 = 0; k1 = 1;
                i2 = 1; j2 = 0; k2 = 1;
            }
        }
        else
        {
            if (y0 < z0)
            {
                i1 = 0; j1 = 0; k1 = 1;
                i2 = 0; j2 = 1; k2 = 1;
            }
            else if (x0 < z0)
            {
                i1 = 0; j1 = 1; k1 = 0;
                i2 = 0; j2 = 1; k2 = 1;
            }
            else
            {
                i1 = 0; j1 = 1; k1 = 0;
                i2 = 1; j2 = 1; k2 = 0;
            }
        }

        float x1 = x0 - i1 + G3;
        float y1 = y0 - j1 + G3;
        float z1 = z0 - k1 + G3;
        float x2 = x0 - i2 + 2f * G3;
        float y2 = y0 - j2 + 2f * G3;
        float z2 = z0 - k2 + 2f * G3;
        float x3 = x0 - 1f + 3f * G3;
        float y3 = y0 - 1f + 3f * G3;
        float z3 = z0 - 1f + 3f * G3;

        int ii = i & 255;
        int jj = j & 255;
        int kk = k & 255;

        float n0 = CornerContribution(P[ii + P[jj + P[kk]]] % 12, x0, y0, z0);
        float n1 = CornerContribution(P[ii + i1 + P[jj + j1 + P[kk + k1]]] % 12, x1, y1, z1);
        float n2 = CornerContribution(P[ii + i2 + P[jj + j2 + P[kk + k2]]] % 12, x2, y2, z2);
        float n3 = CornerContribution(P[ii + 1 + P[jj + 1 + P[kk + 1]]] % 12, x3, y3, z3);

        return Mathf.Clamp((n0 + n1 + n2 + n3) * 32f, -1f, 1f);
    }

    private static float CornerContribution(int gradientIndex, float x, float y, float z)
    {
        float t = 0.6f - x * x - y * y - z * z;

        if (t <= 0f)
        {
            return 0f;
        }

        int index = gradientIndex * 3;
        t *= t;
        return t * t * (Gradients[index] * x + Gradients[index + 1] * y + Gradients[index + 2] * z);
    }
}
