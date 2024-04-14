// -----------------------------------------------------------------------
// <copyright file="SliceableTaskQueue.cs" company="AillieoTech">
// Copyright (c) AillieoTech. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AillieoUtils.EasyTimeSlicing
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Represents a queue of tasks that can be scheduled and executed in slices.
    /// </summary>
    public class SliceableTaskQueue
    {
        private readonly SliceableTask sliceableTask;

        private Queue<Action> queueLow;
        private Queue<Action> queueMedium;
        private Queue<Action> queueHigh;

        private SliceableTaskQueue(float timeBudgetPerFrame)
        {
            this.sliceableTask = SliceableTask.Start(timeBudgetPerFrame, this.ProcessTask);
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
        public bool scheduling { get => this.sliceableTask.status == TaskStatus.Executing || this.sliceableTask.status == TaskStatus.Queued; }

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
        /// Enqueues a task with the specified priority.
        /// </summary>
        /// <param name="action">The task to enqueue.</param>
        /// <param name="priority">The priority of the task. The default is <see cref="Priority.Medium"/>.</param>
        public void Enqueue(Action action, Priority priority = Priority.Medium)
        {
            Queue<Action> queue = this.GetQueue(priority, true);
            queue.Enqueue(action);
            if (!this.scheduling)
            {
                this.sliceableTask.status = TaskStatus.Detached;
                this.Resume();
            }
        }

        /// <summary>
        /// Enqueues a task with the specified priority and returns a handle for the task.
        /// </summary>
        /// <param name="action">The task to enqueue.</param>
        /// <param name="priority">The priority of the task. The default is <see cref="Priority.Medium"/>.</param>
        /// <returns>A handle for the enqueued task.</returns>
        public Handle EnqueueWithHandle(Action action, Priority priority = Priority.Medium)
        {
            var handle = new Handle();
            void wrapped()
            {
                if (handle.status == TaskStatus.Queued)
                {
                    try
                    {
                        action();
                    }
                    finally
                    {
                        handle.status = TaskStatus.Finished;
                    }
                }
            }

            this.Enqueue(wrapped, priority);
            return handle;
        }

        /// <summary>
        /// Pauses the task queue, suspending task scheduling and execution.
        /// </summary>
        public void Pause()
        {
            if (this.scheduling)
            {
                TimeSlicingScheduler.Instance.Remove(this.sliceableTask);
            }
        }

        /// <summary>
        /// Resumes the task queue, allowing task scheduling and execution.
        /// </summary>
        public void Resume()
        {
            if (!this.scheduling)
            {
                TimeSlicingScheduler.Instance.Add(this.sliceableTask);
            }
        }

        /// <summary>
        /// Clears all the tasks in the task queue.
        /// </summary>
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

        /// <summary>
        /// Gets the number of pending tasks with the specifiedpriority in the task queue.
        /// </summary>
        /// <param name="priority">The priority level.</param>
        /// <returns>The number of pending tasks with the specified priority.</returns>
        public int GetPendingTasks(Priority priority)
        {
            var queue = this.GetQueue(priority, false);
            if (queue == null)
            {
                return 0;
            }

            return queue.Count;
        }

        private Queue<Action> GetQueue(Priority priority, bool createIfNotExist)
        {
            switch (priority)
            {
                case Priority.Low:
                    if (this.queueLow == null && createIfNotExist)
                    {
                        this.queueLow = new Queue<Action>();
                    }

                    return this.queueLow;
                case Priority.Medium:
                    if (this.queueMedium == null && createIfNotExist)
                    {
                        this.queueMedium = new Queue<Action>();
                    }

                    return this.queueMedium;
                case Priority.High:
                    if (this.queueHigh == null && createIfNotExist)
                    {
                        this.queueHigh = new Queue<Action>();
                    }

                    return this.queueHigh;
            }

            throw new IndexOutOfRangeException(nameof(priority));
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
                    UnityEngine.Debug.LogException(e);
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
                    UnityEngine.Debug.LogException(e);
                }

                if (this.queueLow.Count > 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Represents a handle for a task in the task queue.
        /// </summary>
        public class Handle
        {
            /// <summary>
            /// Gets the status of the task.
            /// </summary>
            public TaskStatus status { get; internal set; } = TaskStatus.Queued;

            /// <summary>
            /// Cancels the task, detaching it from the task queue.
            /// </summary>
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
