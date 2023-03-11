using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AillieoUtils.EasyTimeSlicing.Sample
{
    public class TestCaseCancelTask : MonoBehaviour
    {
        private SliceableTask task = default;

        private void Start()
        {
            StartTask();
        }

        [ContextMenu(nameof(StartTask))]
        private void StartTask()
        {
            if (task == null)
            {
                var actions = Enumerable.Range(1, 1000).Select(TaskCreateHelper.CreateRandomTask);
                task = SliceableTask.Start(0.01f, actions);
            }
        }

        [ContextMenu(nameof(CancelTask))]
        private void CancelTask()
        {
            if (task != null)
            {
                task.Cancel();
                task = null;
            }
        }
    }
}
