// -----------------------------------------------------------------------
// <copyright file="SliceableTaskQueue.cs" company="AillieoTech">
// Copyright (c) AillieoTech. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AillieoUtils.EasyTimeSlicing
{
    using System;
    using System.Collections.Generic;
    using Unity.Profiling;

    /// <summary>
    /// Represents a queue of tasks that can be scheduled and executed in slices.
    /// </summary>
    public class SliceableTaskQueue
    {
        private static int nextQueueId;

        private readonly SliceableTask sliceableTask;

        private readonly ProfilerMarker profilerMarker;

        private Queue<Entry> queueLow;
        private Queue<Entry> queueMedium;
        private Queue<Entry> queueHigh;

        private bool paused;

        private SliceableTaskQueue(float timeBudgetPerFrame, SliceableTaskOptions options = null)
        {
            var profilerTag = string.IsNullOrWhiteSpace(options?.profilerTag)
                ? $"Queue#{++nextQueueId}"
                : options.profilerTag;
            this.profilerMarker = new ProfilerMarker($"EasyTimeSlicing.Queue/{profilerTag}");
            this.sliceableTask = SliceableTask.StartRestartable(timeBudgetPerFrame, this.ProcessTask, options);
        }

        /// <summary>
        /// Represents the priority levels for enqueuing tasks.
        /// </summary>
        public enum Priority
        {
            /// <summary>
            /// Low priority level.
            /// </summary>
            Low,

            /// <summary>
            /// Medium priority level.
            /// </summary>
            Medium,

            /// <summary>
            /// High priority level.
            /// </summary>
            High,
        }

        /// <summary>
        /// Gets or sets the value indicating the time budget per frame for task execution.
        /// </summary>
        public float timeBudgetPerFrame
        {
            get => this.sliceableTask.timeBudgetPerFrame;
            set => this.sliceableTask.timeBudgetPerFrame = value;
        }

        /// <summary>
        /// Gets a value indicating whether the task queue is currently scheduling and executing tasks.
        /// </summary>
        public bool scheduling => !this.paused &&
            (this.sliceableTask.schedulingState == SchedulingState.Executing || this.sliceableTask.schedulingState == SchedulingState.Queued);

        /// <summary>
        /// Gets the number of pending tasks in the task queue.
        /// </summary>
        public int pendingTasks { get => this.GetPendingTasks(Priority.High) + this.GetPendingTasks(Priority.Medium) + this.GetPendingTasks(Priority.Low); }

        /// <summary>
        /// Creates a new instance of the <see cref="SliceableTaskQueue"/> class with the specified time budget per frame..
        /// </summary>
        /// <param name="timeBudgetPerFrame">The time budget per frame for task execution.</param>
        /// <returns>A new instance of the <see cref="SliceableTaskQueue"/> class.</returns>
        public static SliceableTaskQueue Create(float timeBudgetPerFrame)
        {
            return new SliceableTaskQueue(timeBudgetPerFrame);
        }

        /// <summary>
        /// Creates a new instance of the <see cref="SliceableTaskQueue"/> class with diagnostic options.
        /// </summary>
        /// <param name="timeBudgetPerFrame">The time budget per frame for task execution.</param>
        /// <param name="options">Diagnostic options applied to actions executed by this queue.</param>
        /// <returns>A new instance of the <see cref="SliceableTaskQueue"/> class.</returns>
        public static SliceableTaskQueue Create(float timeBudgetPerFrame, SliceableTaskOptions options)
        {
            return new SliceableTaskQueue(timeBudgetPerFrame, options);
        }

        /// <summary>
        /// Enqueues a task with the specified priority.
        /// </summary>
        /// <param name="action">The task to enqueue.</param>
        /// <param name="priority">The priority of the task. The default is <see cref="Priority.Medium"/>.</param>
        public void Enqueue(Action action, Priority priority = Priority.Medium)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            this.Enqueue(new Entry(action, null, priority), priority);
        }

        /// <summary>
        /// Enqueues a task with the specified priority and returns a handle for the task.
        /// </summary>
        /// <param name="action">The task to enqueue.</param>
        /// <param name="priority">The priority of the task. The default is <see cref="Priority.Medium"/>.</param>
        /// <returns>A handle for the enqueued task.</returns>
        public Handle EnqueueWithHandle(Action action, Priority priority = Priority.Medium)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            var handle = new Handle();
            this.Enqueue(new Entry(action, handle, priority), priority);
            return handle;
        }

        /// <summary>
        /// Pauses the task queue, suspending task scheduling and execution.
        /// </summary>
        public void Pause()
        {
            var wasScheduling = this.scheduling;
            this.paused = true;
            if (wasScheduling)
            {
                TimeSlicingScheduler.Instance.Remove(this.sliceableTask);
            }
        }

        /// <summary>
        /// Resumes the task queue, allowing task scheduling and execution.
        /// </summary>
        public void Resume()
        {
            this.paused = false;
            if (!this.scheduling && this.pendingTasks > 0)
            {
                this.sliceableTask.Restart();
            }
        }

        /// <summary>
        /// Clears all the tasks in the task queue.
        /// </summary>
        public void ClearAll()
        {
            Clear(this.queueLow);
            Clear(this.queueMedium);
            Clear(this.queueHigh);
        }

        /// <summary>
        /// Gets the number of pending tasks with the specifiedpriority in the task queue.
        /// </summary>
        /// <param name="priority">The priority level.</param>
        /// <returns>The number of pending tasks with the specified priority.</returns>
        public int GetPendingTasks(Priority priority)
        {
            Queue<Entry> queue = this.GetQueue(priority, false);
            if (queue == null)
            {
                return 0;
            }

            var count = 0;
            foreach (Entry entry in queue)
            {
                if (entry.handle == null || entry.handle.status == SliceableTaskStatus.Pending)
                {
                    count++;
                }
            }

            return count;
        }

        private static void Clear(Queue<Entry> queue)
        {
            if (queue == null)
            {
                return;
            }

            while (queue.Count > 0)
            {
                queue.Dequeue().handle?.Cancel();
            }
        }

        private void Enqueue(Entry entry, Priority priority)
        {
            Queue<Entry> queue = this.GetQueue(priority, true);
            queue.Enqueue(entry);
            if (!this.paused && !this.scheduling)
            {
                this.sliceableTask.Restart();
            }
        }

        private Queue<Entry> GetQueue(Priority priority, bool createIfNotExist)
        {
            switch (priority)
            {
                case Priority.Low:
                    if (this.queueLow == null && createIfNotExist)
                    {
                        this.queueLow = new Queue<Entry>();
                    }

                    return this.queueLow;
                case Priority.Medium:
                    if (this.queueMedium == null && createIfNotExist)
                    {
                        this.queueMedium = new Queue<Entry>();
                    }

                    return this.queueMedium;
                case Priority.High:
                    if (this.queueHigh == null && createIfNotExist)
                    {
                        this.queueHigh = new Queue<Entry>();
                    }

                    return this.queueHigh;
            }

            throw new IndexOutOfRangeException(nameof(priority));
        }

        private bool ProcessTask()
        {
            this.profilerMarker.Begin();
            try
            {
                Entry entry = this.DequeueNext();
                if (entry != null &&
                    (entry.handle == null || entry.handle.status == SliceableTaskStatus.Pending))
                {
                    try
                    {
                        entry.Invoke();
                        entry.handle?.Complete();
                    }
                    catch (Exception e)
                    {
                        entry.handle?.Fault(e);
                        UnityEngine.Debug.LogException(e);
                    }
                }

                return !this.HasQueuedEntries();
            }
            finally
            {
                this.profilerMarker.End();
            }
        }

        private bool HasQueuedEntries()
        {
            return (this.queueHigh != null && this.queueHigh.Count > 0) ||
                (this.queueMedium != null && this.queueMedium.Count > 0) ||
                (this.queueLow != null && this.queueLow.Count > 0);
        }

        private Entry DequeueNext()
        {
            if (this.queueHigh != null && this.queueHigh.Count > 0)
            {
                return this.queueHigh.Dequeue();
            }

            if (this.queueMedium != null && this.queueMedium.Count > 0)
            {
                return this.queueMedium.Dequeue();
            }

            if (this.queueLow != null && this.queueLow.Count > 0)
            {
                return this.queueLow.Dequeue();
            }

            return null;
        }

        private sealed class Entry
        {
            internal readonly Action action;
            internal readonly Handle handle;

            private readonly ProfilerMarker profilerMarker;

            internal Entry(Action action, Handle handle, Priority priority)
            {
                this.action = action;
                this.handle = handle;
                this.profilerMarker = new ProfilerMarker($"EasyTimeSlicing.QueueEntry/{priority}/{SliceableTask.GetProfilerTag(null, action)}");
            }

            internal void Invoke()
            {
                this.profilerMarker.Begin();
                try
                {
                    this.action.Invoke();
                }
                finally
                {
                    this.profilerMarker.End();
                }
            }
        }

        /// <summary>
        /// Represents a handle for a task in the task queue.
        /// </summary>
        public sealed class Handle : ISliceableTaskHandle
        {
            /// <summary>
            /// Gets the observable result of the task.
            /// </summary>
            public SliceableTaskStatus status { get; private set; } = SliceableTaskStatus.Pending;

            /// <summary>
            /// Gets a value indicating whether the task has reached a terminal result.
            /// </summary>
            public bool isCompleted => this.status != SliceableTaskStatus.Pending;

            /// <summary>
            /// Gets the exception thrown by the task, or null if the task did not fault.
            /// </summary>
            public Exception exception { get; private set; }

            /// <summary>
            /// Cancels the task if it has not completed.
            /// </summary>
            public void Cancel()
            {
                if (this.status == SliceableTaskStatus.Pending)
                {
                    this.status = SliceableTaskStatus.Cancelled;
                }
            }

            internal void Complete()
            {
                if (this.status == SliceableTaskStatus.Pending)
                {
                    this.status = SliceableTaskStatus.Succeeded;
                }
            }

            internal void Fault(Exception taskException)
            {
                if (this.status == SliceableTaskStatus.Pending)
                {
                    this.exception = taskException;
                    this.status = SliceableTaskStatus.Faulted;
                }
            }
        }
    }
}
