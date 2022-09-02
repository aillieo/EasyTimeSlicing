using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Assertions;

namespace AillieoUtils.EasyTimeSlicing
{
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

        public delegate bool OpenStateMachineFunc(ref int state);

        public delegate bool ClosedStateMachineFunc();

        public delegate IEnumerator EnumFunc();

        public TaskStatus status { get; internal set; } = TaskStatus.Detached;

        public float executionTimePerFrame { get; private set; }

        private SliceableTask(float executionTimePerFrame)
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
            creatingStackTrace = new StackTrace(2, true);
#endif

            this.executionTimePerFrame = executionTimePerFrame;
            TimeSlicingScheduler.Instance.Add(this);
        }

        public SliceableTask(float executionTimePerFrame, int initialState, OpenStateMachineFunc func)
            : this(executionTimePerFrame)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            int state = initialState;
            this.func = () =>
            {
                return func(ref state);
            };
        }

        public SliceableTask(float executionTimePerFrame, ClosedStateMachineFunc func)
            : this(executionTimePerFrame)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            this.func = func;
        }

        public SliceableTask(float executionTimePerFrame, IEnumerable<Action> actions)
            : this(executionTimePerFrame)
        {
            IEnumerator<Action> e = actions.GetEnumerator();
            this.func = () =>
            {
                while (e.MoveNext())
                {
                    e.Current?.Invoke();
                    return false;
                }

                return true;
            };
        }

        public SliceableTask(float executionTimePerFrame, params Action[] actions)
            : this(executionTimePerFrame)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }

            int actionCount = actions.Length;

            if (actionCount == 0)
            {
                throw new ArgumentException(nameof(actions));
            }

            int index = 0;
            this.func = () =>
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
            };
        }

        public SliceableTask(float executionTimePerFrame, EnumFunc func)
            : this(executionTimePerFrame)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            IEnumerator e = func();
            this.func = () =>
            {
                if (e.MoveNext())
                {
                    return false;
                }

                return true;
            };
        }

        public void Cancel()
        {
            if (status == TaskStatus.Executing || status == TaskStatus.Queued)
            {
                TimeSlicingScheduler.Instance.Remove(this);
            }
        }

        internal bool Execute()
        {
            return func();
        }
    }
}
