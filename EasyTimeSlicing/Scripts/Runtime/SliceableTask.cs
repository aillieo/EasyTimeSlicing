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
    using Unity.Profiling;
    using UnityEngine;

    /// <summary>
    /// Describes the observable result of submitted time-sliced work.
    /// </summary>
    public enum SliceableTaskStatus
    {
        /// <summary>
        /// The work has been submitted and has not reached a terminal result.
        /// </summary>
        Pending,

        /// <summary>
        /// The work completed successfully.
        /// </summary>
        Succeeded,

        /// <summary>
        /// The work stopped because its callback threw an exception.
        /// </summary>
        Faulted,

        /// <summary>
        /// The work was cancelled before it completed.
        /// </summary>
        Cancelled,
    }

    /// <summary>
    /// Controls how a callback that exceeds its task's per-frame budget is reported in the Editor and Development Builds.
    /// </summary>
    public enum SliceOverrunPolicy
    {
        /// <summary>
        /// Do not report callback overruns.
        /// </summary>
        Ignore,

        /// <summary>
        /// Report the first callback overrun as a warning.
        /// </summary>
        Warning,

        /// <summary>
        /// Report the first callback overrun as an error without faulting the task.
        /// </summary>
        Error,
    }

    /// <summary>
    /// Configures diagnostics for a <see cref="SliceableTask"/>.
    /// </summary>
    public sealed class SliceableTaskOptions
    {
        /// <summary>
        /// Gets or sets how a callback overrun is reported in the Editor and Development Builds.
        /// </summary>
        public SliceOverrunPolicy overrunPolicy { get; set; } = SliceOverrunPolicy.Warning;

        /// <summary>
        /// Gets or sets the label shown for this work in the Unity Profiler.
        /// When omitted, a label is derived from the callback method.
        /// </summary>
        public string profilerTag { get; set; }
    }

    /// <summary>
    /// Provides a common way to observe and cancel submitted time-sliced work.
    /// </summary>
    public interface ISliceableTaskHandle
    {
        /// <summary>
        /// Gets the observable result of the work.
        /// </summary>
        SliceableTaskStatus status { get; }

        /// <summary>
        /// Gets a value indicating whether the work has reached a terminal result.
        /// </summary>
        bool isCompleted { get; }

        /// <summary>
        /// Gets the exception that stopped the work, or null if the work did not fault.
        /// </summary>
        Exception exception { get; }

        /// <summary>
        /// Cancels the work if it is still pending.
        /// </summary>
        void Cancel();
    }

    internal enum SchedulingState
    {
        Detached,
        Queued,
        Executing,
        PendingRemove,
    }

    /// <summary>
    /// A <see cref="SliceableTask"/> contains one or more tasks.
    /// Once started, a certain amount of tasks will be executed each frame,
    /// and try not to exceed the specified time budget <see cref="timeBudgetPerFrame"/>.
    /// An ungrouped task owns its budget. Tasks started through a <see cref="SliceableTaskGroup"/>
    /// additionally share that group's budget.
    /// </summary>
    public sealed class SliceableTask : ISliceableTaskHandle
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal StackTrace creatingStackTrace;
#endif

        private readonly ClosedStateMachineFunc func;

        private readonly Action cleanup;

        private readonly bool restartable;

        private readonly bool profileCallback;

        private readonly ProfilerMarker profilerMarker;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private readonly SliceOverrunPolicy overrunPolicy;

        private bool overrunReported;
#endif

        private bool cleanedUp;

        private float timeBudgetPerFrameValue;

        private SliceableTask(
            float timeBudgetPerFrame,
            ClosedStateMachineFunc funcToExecute,
            int skipFrames,
            Action cleanup = null,
            bool restartable = false,
            SliceOverrunPolicy overrunPolicy = SliceOverrunPolicy.Warning,
            SliceableTaskGroup group = null,
            string profilerTag = null,
            bool profileCallback = true)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            this.creatingStackTrace = new StackTrace(skipFrames, true);
#endif

            ValidateTimeBudget(timeBudgetPerFrame, nameof(timeBudgetPerFrame));
            this.timeBudgetPerFrameValue = timeBudgetPerFrame;
            WarnIfBudgetExceedsFrame(timeBudgetPerFrame);
            this.func = funcToExecute;
            this.cleanup = cleanup;
            this.restartable = restartable;
            this.profileCallback = profileCallback;
            this.profilerMarker = new ProfilerMarker($"EasyTimeSlicing.Task/{GetProfilerTag(profilerTag, funcToExecute)}");
            this.group = group;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            this.overrunPolicy = ValidateOverrunPolicy(overrunPolicy);
#else
            ValidateOverrunPolicy(overrunPolicy);
#endif
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
        /// Gets the observable result of the <see cref="SliceableTask"/>.
        /// </summary>
        public SliceableTaskStatus status { get; private set; } = SliceableTaskStatus.Pending;

        /// <summary>
        /// Gets a value indicating whether the task has reached a terminal result.
        /// </summary>
        public bool isCompleted => this.status != SliceableTaskStatus.Pending;

        /// <summary>
        /// Gets the exception that stopped this task, or null if the task did not fault.
        /// </summary>
        public Exception exception { get; private set; }

        internal SchedulingState schedulingState { get; set; } = SchedulingState.Detached;

        internal SliceableTaskGroup group { get; }

        /// <summary>
        /// Gets or sets the value indicating the time budget per frame for task execution.
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
        /// Start a <see cref="SliceableTask"/> with an <see cref="OpenStateMachineFunc"/>.
        /// </summary>
        /// <param name="timeBudgetPerFrame">The value for <see cref="timeBudgetPerFrame"/>.</param>
        /// <param name="initialState">The initial state of the <see cref="OpenStateMachineFunc"/>.</param>
        /// <param name="func">The state machine function contains tasks.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float timeBudgetPerFrame, int initialState, OpenStateMachineFunc func)
        {
            return Start(timeBudgetPerFrame, null, initialState, func);
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with an <see cref="OpenStateMachineFunc"/> and diagnostic options.
        /// </summary>
        /// <param name="timeBudgetPerFrame">The value for <see cref="timeBudgetPerFrame"/>.</param>
        /// <param name="options">Diagnostic options, or null to use defaults.</param>
        /// <param name="initialState">The initial state of the <see cref="OpenStateMachineFunc"/>.</param>
        /// <param name="func">The state machine function contains tasks.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float timeBudgetPerFrame, SliceableTaskOptions options, int initialState, OpenStateMachineFunc func)
        {
            return Start(null, timeBudgetPerFrame, options, initialState, func);
        }

        internal static SliceableTask Start(SliceableTaskGroup group, float timeBudgetPerFrame, SliceableTaskOptions options, int initialState, OpenStateMachineFunc func)
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

            return new SliceableTask(
                timeBudgetPerFrame,
                funcToExecute,
                2,
                overrunPolicy: GetOverrunPolicy(options),
                group: group,
                profilerTag: options?.profilerTag);
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with a <see cref="ClosedStateMachineFunc"/>.
        /// </summary>
        /// <param name="timeBudgetPerFrame">The value for <see cref="timeBudgetPerFrame"/>.</param>
        /// <param name="func">The state machine function contains tasks.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float timeBudgetPerFrame, ClosedStateMachineFunc func)
        {
            return Start(timeBudgetPerFrame, null, func);
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with a <see cref="ClosedStateMachineFunc"/> and diagnostic options.
        /// </summary>
        /// <param name="timeBudgetPerFrame">The value for <see cref="timeBudgetPerFrame"/>.</param>
        /// <param name="options">Diagnostic options, or null to use defaults.</param>
        /// <param name="func">The state machine function contains tasks.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float timeBudgetPerFrame, SliceableTaskOptions options, ClosedStateMachineFunc func)
        {
            return Start(null, timeBudgetPerFrame, options, func);
        }

        internal static SliceableTask Start(SliceableTaskGroup group, float timeBudgetPerFrame, SliceableTaskOptions options, ClosedStateMachineFunc func)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            return new SliceableTask(
                timeBudgetPerFrame,
                func,
                2,
                overrunPolicy: GetOverrunPolicy(options),
                group: group,
                profilerTag: options?.profilerTag);
        }

        internal static SliceableTask StartRestartable(float timeBudgetPerFrame, ClosedStateMachineFunc func, SliceableTaskOptions options = null)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            return new SliceableTask(
                timeBudgetPerFrame,
                func,
                2,
                restartable: true,
                overrunPolicy: GetOverrunPolicy(options),
                profilerTag: options?.profilerTag,
                profileCallback: false);
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with a series of <see cref="Action"/>s.
        /// </summary>
        /// <param name="timeBudgetPerFrame">The value for <see cref="timeBudgetPerFrame"/>.</param>
        /// <param name="actions">The actions to execute.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float timeBudgetPerFrame, IEnumerable<Action> actions)
        {
            return Start(timeBudgetPerFrame, null, actions);
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with a series of <see cref="Action"/>s and diagnostic options.
        /// </summary>
        /// <param name="timeBudgetPerFrame">The value for <see cref="timeBudgetPerFrame"/>.</param>
        /// <param name="options">Diagnostic options, or null to use defaults.</param>
        /// <param name="actions">The actions to execute.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float timeBudgetPerFrame, SliceableTaskOptions options, IEnumerable<Action> actions)
        {
            return Start(null, timeBudgetPerFrame, options, actions);
        }

        internal static SliceableTask Start(SliceableTaskGroup group, float timeBudgetPerFrame, SliceableTaskOptions options, IEnumerable<Action> actions)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }

            ValidateTimeBudget(timeBudgetPerFrame, nameof(timeBudgetPerFrame));
            SliceOverrunPolicy overrunPolicy = GetOverrunPolicy(options);
            IEnumerator<Action> e = actions.GetEnumerator();
            if (e == null)
            {
                throw new InvalidOperationException($"{nameof(actions)} returned a null enumerator");
            }

            bool funcToExecute()
            {
                while (e.MoveNext())
                {
                    e.Current?.Invoke();
                    return false;
                }

                return true;
            }

            try
            {
                return new SliceableTask(
                    timeBudgetPerFrame,
                    funcToExecute,
                    2,
                    e.Dispose,
                    overrunPolicy: overrunPolicy,
                    group: group,
                    profilerTag: options?.profilerTag);
            }
            catch
            {
                DisposeAfterFailedStart(e);
                throw;
            }
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with a series of <see cref="Action"/>s.
        /// </summary>
        /// <param name="timeBudgetPerFrame">The value for <see cref="timeBudgetPerFrame"/>.</param>
        /// <param name="actions">The actions to execute.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float timeBudgetPerFrame, params Action[] actions)
        {
            return Start(timeBudgetPerFrame, null, actions);
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with a series of <see cref="Action"/>s and diagnostic options.
        /// </summary>
        /// <param name="timeBudgetPerFrame">The value for <see cref="timeBudgetPerFrame"/>.</param>
        /// <param name="options">Diagnostic options, or null to use defaults.</param>
        /// <param name="actions">The actions to execute.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float timeBudgetPerFrame, SliceableTaskOptions options, params Action[] actions)
        {
            return Start(null, timeBudgetPerFrame, options, actions);
        }

        internal static SliceableTask Start(SliceableTaskGroup group, float timeBudgetPerFrame, SliceableTaskOptions options, params Action[] actions)
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

            return new SliceableTask(
                timeBudgetPerFrame,
                funcToExecute,
                2,
                overrunPolicy: GetOverrunPolicy(options),
                group: group,
                profilerTag: options?.profilerTag);
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with an <see cref="EnumFunc"/>.
        /// </summary>
        /// <param name="timeBudgetPerFrame">The value for <see cref="timeBudgetPerFrame"/>.</param>
        /// <param name="func">The function that manages a series of tasks.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float timeBudgetPerFrame, EnumFunc func)
        {
            return Start(timeBudgetPerFrame, null, func);
        }

        /// <summary>
        /// Start a <see cref="SliceableTask"/> with an <see cref="EnumFunc"/> and diagnostic options.
        /// </summary>
        /// <param name="timeBudgetPerFrame">The value for <see cref="timeBudgetPerFrame"/>.</param>
        /// <param name="options">Diagnostic options, or null to use defaults.</param>
        /// <param name="func">The function that manages a series of tasks.</param>
        /// <returns>The <see cref="SliceableTask"/> instance create.</returns>
        public static SliceableTask Start(float timeBudgetPerFrame, SliceableTaskOptions options, EnumFunc func)
        {
            return Start(null, timeBudgetPerFrame, options, func);
        }

        internal static SliceableTask Start(SliceableTaskGroup group, float timeBudgetPerFrame, SliceableTaskOptions options, EnumFunc func)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            ValidateTimeBudget(timeBudgetPerFrame, nameof(timeBudgetPerFrame));
            SliceOverrunPolicy overrunPolicy = GetOverrunPolicy(options);
            IEnumerator e = func();
            if (e == null)
            {
                throw new InvalidOperationException($"{nameof(func)} returned null");
            }

            bool funcToExecute()
            {
                if (e.MoveNext())
                {
                    return false;
                }

                return true;
            }

            try
            {
                return new SliceableTask(
                    timeBudgetPerFrame,
                    funcToExecute,
                    2,
                    () => (e as IDisposable)?.Dispose(),
                    overrunPolicy: overrunPolicy,
                    group: group,
                    profilerTag: options?.profilerTag);
            }
            catch
            {
                DisposeAfterFailedStart(e as IDisposable);
                throw;
            }
        }

        /// <summary>
        /// Cancel a <see cref="SliceableTask"/> instance.
        /// </summary>
        public void Cancel()
        {
            if (this.status != SliceableTaskStatus.Pending)
            {
                return;
            }

            this.status = SliceableTaskStatus.Cancelled;
            if (this.schedulingState == SchedulingState.Executing || this.schedulingState == SchedulingState.Queued)
            {
                TimeSlicingScheduler.Instance.Remove(this);
            }
        }

        internal bool Execute()
        {
            if (this.profileCallback)
            {
                this.profilerMarker.Begin();
            }

            try
            {
                return this.ExecuteCallback();
            }
            finally
            {
                if (this.profileCallback)
                {
                    this.profilerMarker.End();
                }
            }
        }

        private bool ExecuteCallback()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var budget = this.timeBudgetPerFrameValue;
            if (budget > 0 && this.overrunPolicy != SliceOverrunPolicy.Ignore && !this.overrunReported)
            {
                var beginTimestamp = Stopwatch.GetTimestamp();
                bool finished = this.func();
                var elapsedSeconds = (double)(Stopwatch.GetTimestamp() - beginTimestamp) / Stopwatch.Frequency;
                if (elapsedSeconds > budget)
                {
                    this.overrunReported = true;
                    var message = $"{nameof(SliceableTask)} callback took {elapsedSeconds * 1000:F3} ms and exceeded its {budget * 1000:F3} ms budget.";
                    if (this.overrunPolicy == SliceOverrunPolicy.Error)
                    {
                        UnityEngine.Debug.LogError(message);
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning(message);
                    }
                }

                return finished;
            }
#endif
            return this.func();
        }

        internal void Complete(SliceableTaskStatus finalStatus, Exception taskException = null)
        {
            if (finalStatus == SliceableTaskStatus.Pending)
            {
                throw new ArgumentException("A completed task must have a terminal status.", nameof(finalStatus));
            }

            if (this.status == SliceableTaskStatus.Pending)
            {
                this.status = finalStatus;
                this.exception = taskException;
            }

            if (!this.cleanedUp)
            {
                this.cleanedUp = true;
                try
                {
                    this.cleanup?.Invoke();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }
            }
        }

        internal void Restart()
        {
            if (!this.restartable)
            {
                throw new InvalidOperationException("Only an internal restartable task can be restarted.");
            }

            this.status = SliceableTaskStatus.Pending;
            this.exception = null;
            this.cleanedUp = false;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            this.overrunReported = false;
#endif
            TimeSlicingScheduler.Instance.Add(this);
        }

        private static SliceOverrunPolicy GetOverrunPolicy(SliceableTaskOptions options)
        {
            return ValidateOverrunPolicy(options?.overrunPolicy ?? SliceOverrunPolicy.Warning);
        }

        private static void DisposeAfterFailedStart(IDisposable disposable)
        {
            if (disposable == null)
            {
                return;
            }

            try
            {
                disposable.Dispose();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        internal static string GetProfilerTag(string configuredTag, Delegate callback)
        {
            if (!string.IsNullOrWhiteSpace(configuredTag))
            {
                return configuredTag;
            }

            var method = callback.Method;
            var declaringType = method.DeclaringType;
            return declaringType == null ? method.Name : $"{declaringType.Name}.{method.Name}";
        }

        private static SliceOverrunPolicy ValidateOverrunPolicy(SliceOverrunPolicy policy)
        {
            switch (policy)
            {
                case SliceOverrunPolicy.Ignore:
                case SliceOverrunPolicy.Warning:
                case SliceOverrunPolicy.Error:
                    return policy;
                default:
                    throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown slice overrun policy.");
            }
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
                UnityEngine.Debug.LogWarning($"{nameof(timeBudgetPerFrame)} is {value} while expected time for frame {TimeSlicingUtils.frameInterval}");
            }
        }
    }
}
