using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AillieoUtils.EasyTimeSlicing
{
    public class SliceableTaskQueue : AbstractSliceableTask
    {
        public enum Priority
        {
            Low,
            Medium,
            High,
        }

        private Queue<Action> queueLow;
        private Queue<Action> queueMedium;
        private Queue<Action> queueHigh;
        private readonly bool repeated = false;

        public SliceableTaskQueue(float executionTimePerFrame, bool repeated)
            : base(executionTimePerFrame)
        {
            this.repeated = repeated;
            TimeSlicingScheduler.Instance.Add(this);
        }

        public void Enqueue(Action action, Priority priority = Priority.Medium)
        {
            Queue<Action> queue = GetQueue(priority);
            queue.Enqueue(action);
        }

        private Queue<Action> GetQueue(Priority priority)
        {
            switch (priority)
            {
            case Priority.Low:
                if (queueLow == null)
                {
                    queueLow = new Queue<Action>();
                }

                return queueLow;
            case Priority.Medium:
                if (queueMedium == null)
                {
                    queueMedium = new Queue<Action>();
                }

                return queueMedium;
            case Priority.High:
                if (queueHigh == null)
                {
                    queueHigh = new Queue<Action>();
                }

                return queueHigh;
            default:
                break;
            }

            throw new Exception();
        }

        public override bool Execute()
        {
            if (queueHigh != null && queueHigh.Count > 0)
            {
                queueHigh.Dequeue()?.Invoke();
                if (queueHigh.Count > 0)
                {
                    return false;
                }
            }

            if (queueMedium != null && queueMedium.Count > 0)
            {
                queueMedium.Dequeue()?.Invoke();
                if (queueMedium.Count > 0)
                {
                    return false;
                }
            }

            if (queueLow != null && queueLow.Count > 0)
            {
                queueLow.Dequeue()?.Invoke();
                if (queueLow.Count > 0)
                {
                    return false;
                }
            }

            return !repeated;
        }
    }
}
