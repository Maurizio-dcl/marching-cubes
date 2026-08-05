using System.Collections.Generic;

namespace DefaultNamespace.Terrain
{
    public sealed class IslandWorkScheduler
    {
        private readonly List<TerrainChunkRuntimeData> _queue = new();

        public int Count => _queue.Count;

        public void Clear()
        {
            _queue.Clear();
        }

        public void Enqueue(TerrainChunkRuntimeData chunk)
        {
            if (chunk == null || _queue.Contains(chunk))
            {
                return;
            }

            _queue.Add(chunk);
        }

        public TerrainChunkRuntimeData DequeueHighestPriority()
        {
            if (_queue.Count == 0)
            {
                return null;
            }

            int bestIndex = 0;
            float bestScore = Score(_queue[0]);

            for (int i = 1; i < _queue.Count; i++)
            {
                float score = Score(_queue[i]);

                if (score < bestScore)
                {
                    bestIndex = i;
                    bestScore = score;
                }
            }

            TerrainChunkRuntimeData result = _queue[bestIndex];
            _queue.RemoveAt(bestIndex);
            return result;
        }

        private static float Score(TerrainChunkRuntimeData chunk)
        {
            float score = chunk.DistanceToCamera;

            if (chunk.IsBeingModified)
            {
                score -= 10000f;
            }

            if (chunk.IsVisible)
            {
                score -= 1000f;
            }

            if (chunk.CurrentLod != chunk.DesiredLod)
            {
                score -= 100f;
            }

            return score;
        }
    }
}
