using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AillieoUtils.EasyTimeSlicing.Sample
{
    public class TestCaseTaskQueue : MonoBehaviour
    {
        private SliceableTaskQueue queue;
        private int index;

        private void Start()
        {
            queue = new SliceableTaskQueue(0.003f);
            index = 1;
        }

        //private void Update()
        //{
        //    if (Time.frameCount % 5 == 0)
        //    {
        //        queue.Enqueue(TaskCreateHelper.CreateRandomTask(index++));
        //    }
        //}

        [ContextMenu(nameof(AddTasksHighPriority60))]
        private void AddTasksHighPriority60()
        {
            Enumerable.Range(1, 60)
                .Select(i => TaskCreateHelper.CreateRandomTask($"{index++} High"))
                .ToList()
                .ForEach(t => queue.Enqueue(t, SliceableTaskQueue.Priority.High));
        }

        [ContextMenu(nameof(AddTasksMediumPriority60))]
        private void AddTasksMediumPriority60()
        {
            Enumerable.Range(1, 60)
                .Select(i => TaskCreateHelper.CreateRandomTask($"{index++} Medium"))
                .ToList()
                .ForEach(t => queue.Enqueue(t, SliceableTaskQueue.Priority.Medium));
        }

        [ContextMenu(nameof(AddTasksLowPriority60))]
        private void AddTasksLowPriority60()
        {
            Enumerable.Range(1, 60)
                .Select(i => TaskCreateHelper.CreateRandomTask($"{index++} Low"))
                .ToList()
                .ForEach(t => queue.Enqueue(t, SliceableTaskQueue.Priority.Low));
        }
    }
}
