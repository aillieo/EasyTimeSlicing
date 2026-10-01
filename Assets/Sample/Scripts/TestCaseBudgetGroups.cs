namespace AillieoUtils.EasyTimeSlicing.Sample
{
    using UnityEngine;

    /// <summary>
    /// Demonstrates independent Group and Queue budget owners.
    /// </summary>
    public class TestCaseBudgetGroups : MonoBehaviour
    {
        private SliceableTaskGroup streamingGroup;
        private SliceableTaskQueue sceneBuildQueue;

        private int terrainChunk;
        private int vegetationChunk;

        private void Start()
        {
            this.streamingGroup = SliceableTaskGroup.Create("WorldStreaming", 0.003f);
            this.sceneBuildQueue = SliceableTaskQueue.Create(
                0.002f,
                new SliceableTaskOptions { profilerTag = "SceneBuild" });
        }

        [ContextMenu(nameof(ScheduleExampleWork))]
        private void ScheduleExampleWork()
        {
            this.terrainChunk = 0;
            this.vegetationChunk = 0;

            this.streamingGroup.Start(
                0.0015f,
                new SliceableTaskOptions { profilerTag = "Terrain" },
                this.LoadNextTerrainChunk);
            this.streamingGroup.Start(
                0.001f,
                new SliceableTaskOptions { profilerTag = "Vegetation" },
                this.LoadNextVegetationChunk);

            // Queue order is strict: all High work precedes Medium, then Low.
            this.sceneBuildQueue.Enqueue(this.BuildTerrainCollider, SliceableTaskQueue.Priority.High);
            this.sceneBuildQueue.Enqueue(this.BuildBuilding, SliceableTaskQueue.Priority.Medium);
            this.sceneBuildQueue.Enqueue(this.SpawnNpc, SliceableTaskQueue.Priority.Low);
        }

        private bool LoadNextTerrainChunk()
        {
            // Replace this with one small, indivisible unit of terrain loading.
            this.terrainChunk++;
            return this.terrainChunk >= 100;
        }

        private bool LoadNextVegetationChunk()
        {
            // Replace this with one small, indivisible unit of vegetation loading.
            this.vegetationChunk++;
            return this.vegetationChunk >= 100;
        }

        private void BuildTerrainCollider()
        {
            Debug.Log("Build terrain collider");
        }

        private void BuildBuilding()
        {
            Debug.Log("Build building");
        }

        private void SpawnNpc()
        {
            Debug.Log("Spawn NPC");
        }
    }
}
