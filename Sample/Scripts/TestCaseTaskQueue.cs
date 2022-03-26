using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AillieoUtils.EasyTimeSlicing.Sample
{
    public class TestCaseTaskQueue : MonoBehaviour
    {
        private SliceableTaskQueue queue;
        private int index;

        private void Start()
        {
            queue = new SliceableTaskQueue(0.003f, true);
            index = 0;
        }

        private void Update()
        {
            if (Time.frameCount % 5 == 0)
            {
                queue.Enqueue(TaskCreateHelper.CreateRandomTask(index++));
                queue.Enqueue(TaskCreateHelper.CreateRandomTask(index++));
                queue.Enqueue(TaskCreateHelper.CreateRandomTask(index++));
                queue.Enqueue(TaskCreateHelper.CreateRandomTask(index++));
                queue.Enqueue(TaskCreateHelper.CreateRandomTask(index++));
            }
        }
    }
}
