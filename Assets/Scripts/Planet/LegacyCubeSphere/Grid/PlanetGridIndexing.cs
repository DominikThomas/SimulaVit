using UnityEngine;

public static class PlanetGridIndexing
{
    public static int GetCellCount(int resolution)
    {
        if (resolution <= 0)
        {
            return 0;
        }

        return 6 * resolution * resolution;
    }

    public static int DirectionToCellIndex(Vector3 dir, int resolution)
    {
        if (resolution <= 0)
        {
            return 0;
        }

        if (!TryDirectionToFaceUV(dir, out int faceIndex, out Vector2 uv))
        {
            return 0;
        }

        int maxCoord = resolution - 1;
        float normalizedX = (uv.x + 1f) * 0.5f;
        float normalizedY = (uv.y + 1f) * 0.5f;

        int x = Mathf.Clamp(Mathf.RoundToInt(normalizedX * maxCoord), 0, maxCoord);
        int y = Mathf.Clamp(Mathf.RoundToInt(normalizedY * maxCoord), 0, maxCoord);

        int localIndex = y * resolution + x;
        return faceIndex * (resolution * resolution) + localIndex;
    }

    public static bool TryDirectionToFaceUV(Vector3 dir, out int faceIndex, out Vector2 uv)
    {
        uv = default;
        faceIndex = -1;

        float sqrMagnitude = dir.sqrMagnitude;
        if (sqrMagnitude <= Mathf.Epsilon)
        {
            return false;
        }

        Vector3 n = dir / Mathf.Sqrt(sqrMagnitude);

        float ax = Mathf.Abs(n.x);
        float ay = Mathf.Abs(n.y);
        float az = Mathf.Abs(n.z);

        if (ay >= ax && ay >= az)
        {
            if (n.y >= 0f)
            {
                // +Y face (index 0): pointOnCube = (u, 1, -v)
                faceIndex = 0;
                float inv = 1f / n.y;
                uv = new Vector2(n.x * inv, -n.z * inv);
            }
            else
            {
                // -Y face (index 1): pointOnCube = (-u, -1, -v)
                faceIndex = 1;
                float inv = 1f / n.y;
                uv = new Vector2(n.x * inv, n.z * inv);
            }

            uv.x = Mathf.Clamp(uv.x, -1f, 1f);
            uv.y = Mathf.Clamp(uv.y, -1f, 1f);
            return true;
        }

        if (ax >= ay && ax >= az)
        {
            if (n.x < 0f)
            {
                // -X face (index 2): pointOnCube = (-1, -v, -u)
                faceIndex = 2;
                float inv = 1f / n.x;
                uv = new Vector2(n.z * inv, n.y * inv);
            }
            else
            {
                // +X face (index 3): pointOnCube = (1, -v, u)
                faceIndex = 3;
                float inv = 1f / n.x;
                uv = new Vector2(n.z * inv, -n.y * inv);
            }

            uv.x = Mathf.Clamp(uv.x, -1f, 1f);
            uv.y = Mathf.Clamp(uv.y, -1f, 1f);
            return true;
        }

        if (n.z >= 0f)
        {
            // +Z face (index 4): pointOnCube = (-v, u, 1)
            faceIndex = 4;
            float inv = 1f / n.z;
            uv = new Vector2(n.y * inv, -n.x * inv);
        }
        else
        {
            // -Z face (index 5): pointOnCube = (-v, -u, -1)
            faceIndex = 5;
            float inv = 1f / n.z;
            uv = new Vector2(n.y * inv, n.x * inv);
        }

        uv.x = Mathf.Clamp(uv.x, -1f, 1f);
        uv.y = Mathf.Clamp(uv.y, -1f, 1f);
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EditorPlayModeValidation()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            return;
        }

        DebugValidateMappings(10, 8);
#endif
    }

    public static void DebugValidateMappings(int resolution = 10, int randomSamples = 8)
    {
#if UNITY_EDITOR
        if (resolution <= 0)
        {
            Debug.LogWarning("PlanetGridIndexing validation skipped: resolution must be > 0.");
            return;
        }

        int cellCount = GetCellCount(resolution);
        Vector3[] knownDirections =
        {
            Vector3.up,
            Vector3.down,
            Vector3.left,
            Vector3.right,
            Vector3.forward,
            Vector3.back
        };

        for (int i = 0; i < knownDirections.Length; i++)
        {
            LogValidationResult(knownDirections[i], resolution, cellCount);
        }

        for (int i = 0; i < randomSamples; i++)
        {
            Vector3 randomDir = GenerateDeterministicDirection(i);
            LogValidationResult(randomDir, resolution, cellCount);
        }
#endif
    }

#if UNITY_EDITOR
    private static void LogValidationResult(Vector3 dir, int resolution, int cellCount)
    {
        int index = DirectionToCellIndex(dir, resolution);
        bool inBounds = index >= 0 && index < cellCount;
        Debug.Log($"PlanetGridIndexing dir={dir.normalized} index={index} bounds=[0,{cellCount - 1}] valid={inBounds}");
    }

    private static Vector3 GenerateDeterministicDirection(int sampleIndex)
    {
        float t = sampleIndex + 1f;
        Vector3 dir = new Vector3(
            Mathf.Sin(t * 1.37f + 0.31f),
            Mathf.Cos(t * 0.93f + 1.17f),
            Mathf.Sin(t * 2.11f + 2.03f));

        return dir.normalized;
    }
#endif
}
