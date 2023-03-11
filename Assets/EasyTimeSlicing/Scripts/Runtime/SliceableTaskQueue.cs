// -----------------------------------------------------------------------
// <copyright file="SliceableTaskQueue.cs" company="AillieoTech">
// Copyright (c) AillieoTech. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AillieoUtils.EasyTimeSlicing
{
    using System;
    using System.Collections.Generic;

    public class SliceableTaskQueue
    {
        private readonly SliceableTask sliceableTask;

        private Queue<Action> queueLow;
        private Queue<Action> queueMedium;
        private Queue<Action> queueHigh;

        private SliceableTaskQueue(float executionTimePerFrame)
        {
            this.sliceableTask = SliceableTask.Start(executionTimePerFrame, this.ProcessTask);
        }

        public enum Priority
        {
            Low,
            Medium,
            High,
        }

        public bool Scheduling { get => this.sliceableTask.status == TaskStatus.Executing || this.sliceableTask.status == TaskStatus.Queued; }

        public static SliceableTaskQueue Create(float executionTimePerFrame)
        {
            return new SliceableTaskQueue(executionTimePerFrame);
        }

        public void Enqueue(Action action, Priority priority = Priority.Medium)
        {
            Queue<Action> queue = this.GetQueue(priority);
            queue.Enqueue(action);
            if (!this.Scheduling)
            {
                this.sliceableTask.status = TaskStatus.Detached;
                this.Resume();
            }
        }

        public Handle EnqueueWithHandle(Action action, Priority priority = Priority.Medium)
        {
            var handle = new Handle();
            this.Enqueue(
                () =>
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
            if (this.Scheduling)
            {
                TimeSlicingScheduler.Instance.Remove(this.sliceableTask);
            }
        }

        public void Resume()
        {
            if (!this.Scheduling)
            {
                TimeSlicingScheduler.Instance.Add(this.sliceableTask);
            }
        }

        public void ClearAll()
        {
            if (this.queueLow != null)
            {
                this.queueLow.Clear();
            }

            if (this.queueMedium != null)
            {
                this.queueMedium.Clear();
            }

            if (this.queueHigh != null)
            {
                this.queueHigh.Clear();
            }
        }

        private Queue<Action> GetQueue(Priority priority)
        {
            switch (priority)
            {
            case Priority.Low:
                if (this.queueLow == null)
                {
                    this.queueLow = new Queue<Action>();
                }

                return this.queueLow;
            case Priority.Medium:
                if (this.queueMedium == null)
                {
                    this.queueMedium = new Queue<Action>();
                }

                return this.queueMedium;
            case Priority.High:
                if (this.queueHigh == null)
                {
                    this.queueHigh = new Queue<Action>();
                }

                return this.queueHigh;
            default:
                break;
            }

            throw new Exception();
        }

        private bool ProcessTask()
        {
            if (this.queueHigh != null && this.queueHigh.Count > 0)
            {
                try
                {
                    this.queueHigh.Dequeue()?.Invoke();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError(e.StackTrace);
                }

                if (this.queueHigh.Count > 0)
                {
                    return false;
                }
            }

            if (this.queueMedium != null && this.queueMedium.Count > 0)
            {
                try
                {
                    this.queueMedium.Dequeue()?.Invoke();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }

                if (this.queueMedium.Count > 0)
                {
                    return false;
                }
            }

            if (this.queueLow != null && this.queueLow.Count > 0)
            {
                try
                {
                    this.queueLow.Dequeue()?.Invoke();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError(e.StackTrace);
                }

                if (this.queueLow.Count > 0)
                {
                    return false;
                }
            }

            return true;
        }

        public class Handle
        {
            public TaskStatus status { get; internal set; } = TaskStatus.Queued;

            public void Cancel()
            {
                if (this.status == TaskStatus.Queued)
                {
                    this.status = TaskStatus.Detached;
                }
            }
        }
    }
}
