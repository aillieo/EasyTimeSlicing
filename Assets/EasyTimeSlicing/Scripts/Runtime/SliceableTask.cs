using System;
using System.Collections;
using System.Collections.Generic;

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
        public delegate bool OpenStateMachineFunc(ref int state);

        public delegate bool ClosedStateMachineFunc();

        public delegate IEnumerator EnumFunc();

        private readonly ClosedStateMachineFunc func;

        public TaskStatus status { get; internal set; } = TaskStatus.Detached;

        public float executionTimePerFrame { get; private set; }

        private SliceableTask(float executionTimePerFrame)
        {
            this.executionTimePerFrame = executionTimePerFrame;
            TimeSlicingScheduler.Instance.Add(this);
        }

        public SliceableTask(float executionTimePerFrame, int initialState, OpenStateMachineFunc func)
            : this(executionTimePerFrame)
        {
            int state = initialState;
            this.func = () =>
            {
                return func(ref state);
            };
        }

        public SliceableTask(float executionTimePerFrame, ClosedStateMachineFunc func)
            : this(executionTimePerFrame)
        {
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
            int actionCount = actions.Length;
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
