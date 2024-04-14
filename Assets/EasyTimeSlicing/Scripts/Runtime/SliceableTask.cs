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
    /// and try not to exceed the specified time budget <see cref="timeBudgetPerFrame"/>.
    /// </summary>
    public sealed class SliceableTask
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal StackTrace creatingStackTrace;
#endif

        private readonly ClosedStateMachineFunc func;

        private float timeBudgetPerFrameValue;

        private SliceableTask(float timeBudgetPerFrame, ClosedStateMachineFunc funcToExecute, int skipFrames)
        {
            if (timeBudgetPerFrame < 0)
            {
                throw new ArgumentException($"{nameof(timeBudgetPerFrame)} less than 0");
            }

            if (Application.targetFrameRate > 0 && timeBudgetPerFrame >= TimeSlicingUtils.frameInterval)
            {
                UnityEngine.Debug.LogWarning($"{nameof(timeBudgetPerFrame)} is {timeBudgetPerFrame} while expected time for frame {TimeSlicingUtils.frameInterval}");
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            this.creatingStackTrace = new StackTrace(skipFrames, true);
#endif

            this.timeBudgetPerFrame = timeBudgetPerFrame;
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
        /// Gets or sets the value indicating the time budget per frame for task execution.
        /// </summary>
        public float timeBudgetPerFrame
        {
            get => this.timeBudgetPerFrameValue;

            set
            {
                if (Application.targetFrameRate > 0 && value >= TimeSlicingUtils.frameInterval)
                {
                    UnityEngine.Debug.LogWarning($"{nameof(this.timeBudgetPerFrame)} is {value} while expected time for frame {TimeSlicingUtils.frameInterval}");
                }

                this.timeBudgetPerFrameValue = value;
            }
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with an <see cref="OpenStateMachineFunc"/>.
        /// </summary>
        /// <param name="timeBudgetPerFrame">The value for <see cref="timeBudgetPerFrame"/>.</param>
        /// <param name="initialState">The initial state of the <see cref="OpenStateMachineFunc"/>.</param>
        /// <param name="func">The state machine function contains tasks.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float timeBudgetPerFrame, int initialState, OpenStateMachineFunc func)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            var state = initialState;

            bool funcToExecute()
            {
                return func(ref state);
            }

            return new SliceableTask(timeBudgetPerFrame, funcToExecute, 2);
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with a <see cref="ClosedStateMachineFunc"/>.
        /// </summary>
        /// <param name="timeBudgetPerFrame">The value for <see cref="timeBudgetPerFrame"/>.</param>
        /// <param name="func">The state machine function contains tasks.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float timeBudgetPerFrame, ClosedStateMachineFunc func)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            return new SliceableTask(timeBudgetPerFrame, func, 2);
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with a series of <see cref="Action"/>s.
        /// </summary>
        /// <param name="timeBudgetPerFrame">The value for <see cref="timeBudgetPerFrame"/>.</param>
        /// <param name="actions">The actions to execute.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float timeBudgetPerFrame, IEnumerable<Action> actions)
        {
            IEnumerator<Action> e = actions.GetEnumerator();

            bool funcToExecute()
            {
                while (e.MoveNext())
                {
                    e.Current?.Invoke();
                    return false;
                }

                return true;
            }

            return new SliceableTask(timeBudgetPerFrame, funcToExecute, 2);
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with a series of <see cref="Action"/>s.
        /// </summary>
        /// <param name="timeBudgetPerFrame">The value for <see cref="timeBudgetPerFrame"/>.</param>
        /// <param name="actions">The actions to execute.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float timeBudgetPerFrame, params Action[] actions)
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

            bool funcToExecute()
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

                throw new IndexOutOfRangeException($"i = {index} while action count = {actionCount}");
            }

            return new SliceableTask(timeBudgetPerFrame, funcToExecute, 2);
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with an <see cref="EnumFunc"/>.
        /// </summary>
        /// <param name="timeBudgetPerFrame">The value for <see cref="timeBudgetPerFrame"/>.</param>
        /// <param name="func">The function that manages a series of tasks.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float timeBudgetPerFrame, EnumFunc func)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            IEnumerator e = func();
            bool funcToExecute()
            {
                if (e.MoveNext())
                {
                    return false;
                }

                return true;
            }

            return new SliceableTask(timeBudgetPerFrame, funcToExecute, 2);
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
