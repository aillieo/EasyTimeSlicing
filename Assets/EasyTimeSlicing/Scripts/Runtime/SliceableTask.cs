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

    public enum TaskStatus
    {
        Detached,
        Queued,
        Executing,
        Finished,
        PendingRemove,
    }

    public class SliceableTask
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
            this.creatingStackTrace = new StackTrace(2, true);
#endif

            this.executionTimePerFrame = executionTimePerFrame;
            this.func = funcToExecute;
            TimeSlicingScheduler.Instance.Add(this);
        }

        public delegate bool OpenStateMachineFunc(ref int state);

        public delegate bool ClosedStateMachineFunc();

        public delegate IEnumerator EnumFunc();

        public TaskStatus status { get; internal set; } = TaskStatus.Detached;

        public float executionTimePerFrame { get; private set; }

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

        public static SliceableTask Start(float executionTimePerFrame, ClosedStateMachineFunc func)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            return new SliceableTask(executionTimePerFrame, func, 2);
        }

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
