using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AillieoUtils.EasyTimeSlicing
{
    public enum TaskStatus
    {
        Detached,
        Queued,
        Executing,
        Finished,
    }

    public abstract class AbstractSliceableTask
    {
        public TaskStatus status { get; internal protected set; } = TaskStatus.Detached;

        public float executionTimePerFrame { get; private set; }

        public abstract bool Execute();

        protected AbstractSliceableTask(float executionTimePerFrame)
        {
            this.executionTimePerFrame = executionTimePerFrame;
        }
    }
}
