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

        private SliceableTask(float executionTimePerFrame, ClosedStateMachineFunc funcToExecute)
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
            this.func = funcToExecute;
            TimeSlicingScheduler.Instance.Add(this);
        }

        public static SliceableTask Start(float executionTimePerFrame, int initialState, OpenStateMachineFunc func)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            int state = initialState;

            return new SliceableTask(executionTimePerFrame, () =>
            {
                return func(ref state);
            });
        }

        public static SliceableTask Start(float executionTimePerFrame, ClosedStateMachineFunc func)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            return new SliceableTask(executionTimePerFrame, func);
        }

        public static SliceableTask Start(float executionTimePerFrame, IEnumerable<Action> actions)
        {
            IEnumerator<Action> e = actions.GetEnumerator();

            return new SliceableTask(executionTimePerFrame, () =>
            {
                while (e.MoveNext())
                {
                    e.Current?.Invoke();
                    return false;
                }

                return true;
            });
        }

        public static SliceableTask Start(float executionTimePerFrame, params Action[] actions)
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

            return new SliceableTask(executionTimePerFrame, () =>
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
            });
        }

        public static SliceableTask Start(float executionTimePerFrame, EnumFunc func)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            IEnumerator e = func();
            return new SliceableTask(executionTimePerFrame, () =>
            {
                if (e.MoveNext())
                {
                    return false;
                }

                return true;
            });
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
