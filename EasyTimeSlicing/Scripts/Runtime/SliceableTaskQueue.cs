using System;
using System.Collections.Generic;

namespace AillieoUtils.EasyTimeSlicing
{
    public class SliceableTaskQueue
    {
        public enum Priority
        {
            Low,
            Medium,
            High,
        }

        public class Handle
        {
            public TaskStatus status { get; internal set; } = TaskStatus.Queued;

            public void Cancel()
            {
                if (status == TaskStatus.Queued)
                {
                    status = TaskStatus.Detached;
                }
            }
        }

        private readonly SliceableTask sliceableTask;

        private Queue<Action> queueLow;
        private Queue<Action> queueMedium;
        private Queue<Action> queueHigh;

        public bool Scheduling { get => sliceableTask.status == TaskStatus.Executing || sliceableTask.status == TaskStatus.Queued; }

        public SliceableTaskQueue(float executionTimePerFrame)
        {
            this.sliceableTask = SliceableTask.Start(executionTimePerFrame, ProcessTask);
        }

        public void Enqueue(Action action, Priority priority = Priority.Medium)
        {
            Queue<Action> queue = GetQueue(priority);
            queue.Enqueue(action);
            if (!Scheduling)
            {
                sliceableTask.status = TaskStatus.Detached;
                Resume();
            }
        }

        public Handle EnqueueWithHandle(Action action, Priority priority = Priority.Medium)
        {
            Handle handle = new Handle();
            Enqueue(() =>
            {
                if (handle.status == TaskStatus.Queued)
                {
                    action();
                    handle.status = TaskStatus.Finished;
                }
            }, priority);
            return handle;
        }

        public void Pause()
        {
            if (Scheduling)
            {
                TimeSlicingScheduler.Instance.Remove(sliceableTask);
            }
        }

        public void Resume()
        {
            if (!Scheduling)
            {
                TimeSlicingScheduler.Instance.Add(sliceableTask);
            }
        }

        public void ClearAll()
        {
            if (queueLow != null)
            {
                queueLow.Clear();
            }

            if (queueMedium != null)
            {
                queueMedium.Clear();
            }

            if (queueHigh != null)
            {
                queueHigh.Clear();
            }
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

        private bool ProcessTask()
        {
            if (queueHigh != null && queueHigh.Count > 0)
            {
                try
                {
                    queueHigh.Dequeue()?.Invoke();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError(e.StackTrace);
                }

                if (queueHigh.Count > 0)
                {
                    return false;
                }
            }

            if (queueMedium != null && queueMedium.Count > 0)
            {
                try
                {
                    queueMedium.Dequeue()?.Invoke();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError(e.StackTrace);
                }

                if (queueMedium.Count > 0)
                {
                    return false;
                }
            }

            if (queueLow != null && queueLow.Count > 0)
            {
                try
                {
                    queueLow.Dequeue()?.Invoke();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError(e.StackTrace);
                }

                if (queueLow.Count > 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
