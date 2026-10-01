// -----------------------------------------------------------------------
// <copyright file="SliceableTaskGroup.cs" company="AillieoTech">
// Copyright (c) AillieoTech. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AillieoUtils.EasyTimeSlicing
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Unity.Profiling;
    using UnityEngine;

    /// <summary>
    /// Shares one per-frame budget across a set of directly scheduled <see cref="SliceableTask"/> instances.
    /// A group does not define task priority and cannot contain a <see cref="SliceableTaskQueue"/>.
    /// </summary>
    public sealed class SliceableTaskGroup
    {
        internal readonly List<SliceableTask> tasks = new List<SliceableTask>();

        internal readonly ProfilerMarker profilerMarker;

        internal int nextTaskIndex;

        internal bool registered;

        private float timeBudgetPerFrameValue;

        private SliceableTaskGroup(string name, float timeBudgetPerFrame)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A budget group requires a non-empty name.", nameof(name));
            }

            ValidateTimeBudget(timeBudgetPerFrame, nameof(timeBudgetPerFrame));
            this.name = name;
            this.timeBudgetPerFrameValue = timeBudgetPerFrame;
            this.profilerMarker = new ProfilerMarker($"EasyTimeSlicing.Group/{name}");
            WarnIfBudgetExceedsFrame(timeBudgetPerFrame);
        }

        /// <summary>
        /// Gets the name used to identify this group in the Unity Profiler.
        /// </summary>
        public string name { get; }

        /// <summary>
        /// Gets or sets the aggregate per-frame time budget shared by tasks in this group.
        /// </summary>
        public float timeBudgetPerFrame
        {
            get => this.timeBudgetPerFrameValue;
            set
            {
                ValidateTimeBudget(value, nameof(value));
                WarnIfBudgetExceedsFrame(value);
                this.timeBudgetPerFrameValue = value;
            }
        }

        /// <summary>
        /// Gets the number of unfinished tasks in this group.
        /// </summary>
        public int activeTasks
        {
            get
            {
                var count = 0;
                foreach (SliceableTask task in this.tasks)
                {
                    if (task != null && task.status == SliceableTaskStatus.Pending)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// Creates a named budget group. The name is also used as its Unity Profiler label.
        /// </summary>
        /// <param name="name">A stable, descriptive name for the workload.</param>
        /// <param name="timeBudgetPerFrame">The aggregate time budget shared by this group's tasks.</param>
        /// <returns>A new budget group.</returns>
        public static SliceableTaskGroup Create(string name, float timeBudgetPerFrame)
        {
            return new SliceableTaskGroup(name, timeBudgetPerFrame);
        }

        /// <summary>
        /// Starts a grouped task backed by an open state machine.
        /// </summary>
        public SliceableTask Start(float taskTimeBudgetPerFrame, int initialState, SliceableTask.OpenStateMachineFunc func)
        {
            return this.Start(taskTimeBudgetPerFrame, null, initialState, func);
        }

        /// <summary>
        /// Starts a grouped task backed by an open state machine with diagnostic options.
        /// </summary>
        public SliceableTask Start(float taskTimeBudgetPerFrame, SliceableTaskOptions options, int initialState, SliceableTask.OpenStateMachineFunc func)
        {
            return SliceableTask.Start(this, taskTimeBudgetPerFrame, options, initialState, func);
        }

        /// <summary>
        /// Starts a grouped task backed by a closed state machine.
        /// </summary>
        public SliceableTask Start(float taskTimeBudgetPerFrame, SliceableTask.ClosedStateMachineFunc func)
        {
            return this.Start(taskTimeBudgetPerFrame, null, func);
        }

        /// <summary>
        /// Starts a grouped task backed by a closed state machine with diagnostic options.
        /// </summary>
        public SliceableTask Start(float taskTimeBudgetPerFrame, SliceableTaskOptions options, SliceableTask.ClosedStateMachineFunc func)
        {
            return SliceableTask.Start(this, taskTimeBudgetPerFrame, options, func);
        }

        /// <summary>
        /// Starts a grouped task backed by an enumerable sequence of actions.
        /// </summary>
        public SliceableTask Start(float taskTimeBudgetPerFrame, IEnumerable<Action> actions)
        {
            return this.Start(taskTimeBudgetPerFrame, null, actions);
        }

        /// <summary>
        /// Starts a grouped task backed by an enumerable sequence of actions with diagnostic options.
        /// </summary>
        public SliceableTask Start(float taskTimeBudgetPerFrame, SliceableTaskOptions options, IEnumerable<Action> actions)
        {
            return SliceableTask.Start(this, taskTimeBudgetPerFrame, options, actions);
        }

        /// <summary>
        /// Starts a grouped task backed by an array of actions.
        /// </summary>
        public SliceableTask Start(float taskTimeBudgetPerFrame, params Action[] actions)
        {
            return this.Start(taskTimeBudgetPerFrame, null, actions);
        }

        /// <summary>
        /// Starts a grouped task backed by an array of actions with diagnostic options.
        /// </summary>
        public SliceableTask Start(float taskTimeBudgetPerFrame, SliceableTaskOptions options, params Action[] actions)
        {
            return SliceableTask.Start(this, taskTimeBudgetPerFrame, options, actions);
        }

        /// <summary>
        /// Starts a grouped task backed by an enumerator function.
        /// </summary>
        public SliceableTask Start(float taskTimeBudgetPerFrame, SliceableTask.EnumFunc func)
        {
            return this.Start(taskTimeBudgetPerFrame, null, func);
        }

        /// <summary>
        /// Starts a grouped task backed by an enumerator function with diagnostic options.
        /// </summary>
        public SliceableTask Start(float taskTimeBudgetPerFrame, SliceableTaskOptions options, SliceableTask.EnumFunc func)
        {
            return SliceableTask.Start(this, taskTimeBudgetPerFrame, options, func);
        }

        private static void ValidateTimeBudget(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Time budget must be a finite, non-negative value.");
            }
        }

        private static void WarnIfBudgetExceedsFrame(float value)
        {
            if (value >= TimeSlicingUtils.frameInterval)
            {
                Debug.LogWarning($"{nameof(timeBudgetPerFrame)} is {value} while expected time for frame {TimeSlicingUtils.frameInterval}");
            }
        }
    }
}
