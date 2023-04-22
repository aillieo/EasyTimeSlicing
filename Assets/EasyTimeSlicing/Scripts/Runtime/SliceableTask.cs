// -----------------------------------------------------------------------
// <copyright file="SliceableTask.cs" company="AillieoTech">
// Copyright (c) AillieoTech. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AillieoUtils.EasyTimeSlicing
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using UnityEngine;

    /// <summary>
    /// Status of a <see cref="SliceableTask"/>.
    /// </summary>
    public enum TaskStatus
    {
        /// <summary>
        /// Status that a <see cref="SliceableTask"/> is not managed by the scheduler, which means it has been removed or is going to be added.
        /// </summary>
        Detached,

        /// <summary>
        /// Status that a <see cref="SliceableTask"/> is in the queue and will be executed when time budget is available.
        /// </summary>
        Queued,

        /// <summary>
        /// Status that a <see cref="SliceableTask"/> is being executed at the moment.
        /// </summary>
        Executing,

        /// <summary>
        /// Status that a <see cref="SliceableTask"/> has been executed.
        /// </summary>
        Finished,

        /// <summary>
        /// Status that a <see cref="SliceableTask"/> is going to be removed from the queue.
        /// </summary>
        PendingRemove,
    }

    /// <summary>
    /// A <see cref="SliceableTask"/> contains one or more tasks.
    /// Once started, a certain amount of tasks will be executed each frame,
    /// and try not to exceed the specified time budget <see cref="executionTimePerFrame"/>.
    /// </summary>
    public sealed class SliceableTask
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal StackTrace creatingStackTrace;
#endif

        private readonly ClosedStateMachineFunc func;

        private SliceableTask(float executionTimePerFrame, ClosedStateMachineFunc funcToExecute, int skipFrames)
        {
            if (executionTimePerFrame < 0)
            {
                throw new ArgumentException($"{nameof(executionTimePerFrame)} less than 0");
            }

            if (Application.targetFrameRate > 0 && executionTimePerFrame >= 1 / Application.targetFrameRate)
            {
                UnityEngine.Debug.LogWarning($"{nameof(executionTimePerFrame)} is {executionTimePerFrame} while expected time for frame {1f / Application.targetFrameRate}");
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            this.creatingStackTrace = new StackTrace(skipFrames, true);
#endif

            this.executionTimePerFrame = executionTimePerFrame;
            this.func = funcToExecute;
            TimeSlicingScheduler.Instance.Add(this);
        }

        /// <summary>
        /// An <see cref="OpenStateMachineFunc"/> can stand for a series of tasks and modify the state each time it is called, and it should return true when all tasks are finished.
        /// The function should be stateless, and the state should be manually maintained somewhere else.
        /// </summary>
        /// <param name="state">The state for this function.</param>
        /// <returns>All the tasks are finished.</returns>
        public delegate bool OpenStateMachineFunc(ref int state);

        /// <summary>
        /// A <see cref="ClosedStateMachineFunc"/> can stand for a series of tasks and is repeatedly called to execute them, and return true when all tasks are finished.
        /// The function should manage its state inside.
        /// </summary>
        /// <returns>All the tasks are finished.</returns>
        public delegate bool ClosedStateMachineFunc();

        /// <summary>
        /// An <see cref="EnumFunc"/> can stand for a series of tasks and is managed by an <see cref="IEnumerator"/>.
        /// </summary>
        /// <returns>A <see cref="IEnumerator"/> to iterate over all tasks.</returns>
        public delegate IEnumerator EnumFunc();

        /// <summary>
        /// Gets a value indicating current status of the <see cref="SliceableTask"/>.
        /// </summary>
        public TaskStatus status { get; internal set; } = TaskStatus.Detached;

        /// <summary>
        /// Gets a value indicating the expected execution time in a single frame.
        /// </summary>
        public float executionTimePerFrame { get; private set; }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with an <see cref="OpenStateMachineFunc"/>.
        /// </summary>
        /// <param name="executionTimePerFrame">The value for <see cref="executionTimePerFrame"/>.</param>
        /// <param name="initialState">The initial state of the <see cref="executionTimePerFrame"/>.</param>
        /// <param name="func">The state machine function contains tasks.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float executionTimePerFrame, int initialState, OpenStateMachineFunc func)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            var state = initialState;

            return new SliceableTask(
                executionTimePerFrame,
                () =>
                {
                    return func(ref state);
                },
                2);
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with a <see cref="ClosedStateMachineFunc"/>.
        /// </summary>
        /// <param name="executionTimePerFrame">The value for <see cref="executionTimePerFrame"/>.</param>
        /// <param name="func">The state machine function contains tasks.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float executionTimePerFrame, ClosedStateMachineFunc func)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            return new SliceableTask(executionTimePerFrame, func, 2);
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with a series of <see cref="Action"/>s.
        /// </summary>
        /// <param name="executionTimePerFrame">The value for <see cref="executionTimePerFrame"/>.</param>
        /// <param name="actions">The actions to execute.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float executionTimePerFrame, IEnumerable<Action> actions)
        {
            IEnumerator<Action> e = actions.GetEnumerator();

            return new SliceableTask(
                executionTimePerFrame,
                () =>
                {
                    while (e.MoveNext())
                    {
                        e.Current?.Invoke();
                        return false;
                    }

                    return true;
                },
                2);
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with a series of <see cref="Action"/>s.
        /// </summary>
        /// <param name="executionTimePerFrame">The value for <see cref="executionTimePerFrame"/>.</param>
        /// <param name="actions">The actions to execute.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float executionTimePerFrame, params Action[] actions)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }

            var actionCount = actions.Length;

            if (actionCount == 0)
            {
                throw new ArgumentException("no actions provided", nameof(actions));
            }

            var index = 0;

            return new SliceableTask(
                executionTimePerFrame,
                () =>
                {
                    if (index < actionCount)
                    {
                        actions[index]?.Invoke();
                        if (index == actionCount - 1)
                        {
                            return true;
                        }
                        else
                        {
                            index++;
                            return false;
                        }
                    }

                    throw new Exception();
                },
                2);
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with an <see cref="EnumFunc"/>.
        /// </summary>
        /// <param name="executionTimePerFrame">The value for <see cref="executionTimePerFrame"/>.</param>
        /// <param name="func">The function that manages a series of tasks.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float executionTimePerFrame, EnumFunc func)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            IEnumerator e = func();
            return new SliceableTask(
                executionTimePerFrame,
                () =>
                {
                    if (e.MoveNext())
                    {
                        return false;
                    }

                    return true;
                },
                2);
        }

        /// <summary>
        /// Cancel a <see cref="SliceableTask"/> instance.
        /// </summary>
        public void Cancel()
        {
            if (this.status == TaskStatus.Executing || this.status == TaskStatus.Queued)
            {
                TimeSlicingScheduler.Instance.Remove(this);
            }
        }

        internal bool Execute()
        {
            return this.func();
        }
    }
}
