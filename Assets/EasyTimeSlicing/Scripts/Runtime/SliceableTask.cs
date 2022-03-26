using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AillieoUtils.EasyTimeSlicing
{
    public class SliceableTask : AbstractSliceableTask
    {
        public delegate bool StateMachineFunc(ref int state);

        public delegate bool RepeatingFunc();

        public delegate IEnumerator CoroutineFunc();

        private readonly RepeatingFunc func;

        protected SliceableTask(float executionTimePerFrame)
            : base(executionTimePerFrame)
        {
            TimeSlicingScheduler.Instance.Add(this);
        }

        public SliceableTask(float executionTimePerFrame, int initialState, StateMachineFunc func)
            : this(executionTimePerFrame)
        {
            int state = initialState;
            this.func = () =>
            {
                return func(ref state);
            };
        }

        public SliceableTask(float executionTimePerFrame, RepeatingFunc func)
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

        public SliceableTask(float executionTimePerFrame, CoroutineFunc func)
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

        public override bool Execute()
        {
            return func();
        }
    }
}
