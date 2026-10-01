// -----------------------------------------------------------------------
// <copyright file="TimeSlicingScheduler.cs" company="AillieoTech">
// Copyright (c) AillieoTech. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AillieoUtils.EasyTimeSlicing
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Unity.Profiling;
    using UnityEngine;
    using UnityEngine.Assertions;

    [DefaultExecutionOrder(-100)]
    internal class TimeSlicingScheduler : MonoBehaviour
    {
        private static readonly ProfilerMarker UpdateProfilerMarker = new ProfilerMarker("EasyTimeSlicing.Scheduler.Update");

        private static TimeSlicingScheduler instance;

        private readonly List<SliceableTask> standaloneTasks = new List<SliceableTask>();

        private readonly List<SliceableTaskGroup> managedGroups = new List<SliceableTaskGroup>();

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private readonly HashSet<SliceableTask> validationSet = new HashSet<SliceableTask>();
#endif

        internal static TimeSlicingScheduler Instance
        {
            get
            {
                CreateInstance();
                return instance;
            }
        }

        internal void Add(SliceableTask task)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            if (task.schedulingState == SchedulingState.PendingRemove)
            {
                task.schedulingState = SchedulingState.Queued;
                return;
            }

            if (task.schedulingState != SchedulingState.Detached)
            {
                throw new InvalidOperationException($"Unexpected state {task.schedulingState}");
            }

            if (task.group == null)
            {
                if (this.standaloneTasks.Contains(task))
                {
                    throw new InvalidOperationException("Task is already managed by this scheduler.");
                }

                this.standaloneTasks.Add(task);
            }
            else
            {
                SliceableTaskGroup group = task.group;
                if (group.tasks.Contains(task))
                {
                    throw new InvalidOperationException("Task is already managed by its budget group.");
                }

                group.tasks.Add(task);
                if (!group.registered)
                {
                    group.registered = true;
                    this.managedGroups.Add(group);
                }
            }

            task.schedulingState = SchedulingState.Queued;
        }

        internal void Remove(SliceableTask task)
        {
            if (task.schedulingState == SchedulingState.Executing || task.schedulingState == SchedulingState.Queued)
            {
                task.schedulingState = SchedulingState.PendingRemove;
            }
        }

        private static long BudgetToTicks(float seconds)
        {
            return (long)(seconds * Stopwatch.Frequency);
        }

        private static bool BudgetElapsed(long beginTimestamp, long budgetTicks)
        {
            return Stopwatch.GetTimestamp() - beginTimestamp >= budgetTicks;
        }

        // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateInstance()
        {
            if (instance == null)
            {
                var go = new GameObject($"[{nameof(TimeSlicingScheduler)}]");
                instance = go.AddComponent<TimeSlicingScheduler>();
                DontDestroyOnLoad(go);
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
            }
        }

        private void Update()
        {
            if (this.standaloneTasks.Count == 0 && this.managedGroups.Count == 0)
            {
                return;
            }

            UpdateProfilerMarker.Begin();
            try
            {
                this.ExecuteStandaloneTasks();
                this.ExecuteGroups();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                this.ValidateManagedTasks();
#endif
            }
            finally
            {
                UpdateProfilerMarker.End();
            }
        }

        private void ExecuteStandaloneTasks()
        {
            var taskCount = this.standaloneTasks.Count;
            var removedTasks = 0;
            for (var index = 0; index < taskCount; index++)
            {
                SliceableTask task = this.standaloneTasks[index];
                if (task == null)
                {
                    removedTasks++;
                    continue;
                }

                var beginTimestamp = Stopwatch.GetTimestamp();
                var budgetTicks = BudgetToTicks(task.timeBudgetPerFrame);
                bool removed;
                this.ExecuteTask(this.standaloneTasks, index, beginTimestamp, budgetTicks, out removed);
                if (removed)
                {
                    removedTasks++;
                }
            }

            if (removedTasks > 0)
            {
                this.standaloneTasks.RemoveAll(task => task == null);
            }
        }

        private void ExecuteGroups()
        {
            var groupCount = this.managedGroups.Count;
            var removedGroups = 0;
            for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                SliceableTaskGroup group = this.managedGroups[groupIndex];
                if (group == null)
                {
                    removedGroups++;
                    continue;
                }

                group.profilerMarker.Begin();
                try
                {
                    this.ExecuteGroup(group);
                }
                finally
                {
                    group.profilerMarker.End();
                }

                if (group.tasks.Count == 0)
                {
                    group.registered = false;
                    group.nextTaskIndex = 0;
                    this.managedGroups[groupIndex] = null;
                    removedGroups++;
                }
            }

            if (removedGroups > 0)
            {
                this.managedGroups.RemoveAll(group => group == null);
            }
        }

        private void ExecuteGroup(SliceableTaskGroup group)
        {
            var taskCount = group.tasks.Count;
            if (taskCount == 0)
            {
                return;
            }

            var beginTimestamp = Stopwatch.GetTimestamp();
            var groupBudgetTicks = BudgetToTicks(group.timeBudgetPerFrame);
            var index = group.nextTaskIndex % taskCount;
            var visitedTasks = 0;
            var removedTasks = 0;
            var executedTask = false;

            while (visitedTasks < taskCount)
            {
                var currentIndex = index;
                index = (index + 1) % taskCount;
                visitedTasks++;

                SliceableTask task = group.tasks[currentIndex];
                if (task == null)
                {
                    removedTasks++;
                    continue;
                }

                bool removed;
                if (this.ExecuteTask(group.tasks, currentIndex, beginTimestamp, groupBudgetTicks, out removed))
                {
                    executedTask = true;
                }

                if (removed)
                {
                    removedTasks++;
                }

                if (executedTask && BudgetElapsed(beginTimestamp, groupBudgetTicks))
                {
                    break;
                }
            }

            if (removedTasks > 0)
            {
                SliceableTask nextTask = null;
                for (var offset = 0; offset < taskCount; offset++)
                {
                    SliceableTask candidate = group.tasks[(index + offset) % taskCount];
                    if (candidate != null)
                    {
                        nextTask = candidate;
                        break;
                    }
                }

                group.tasks.RemoveAll(task => task == null);
                group.nextTaskIndex = nextTask == null ? 0 : group.tasks.IndexOf(nextTask);
            }
            else
            {
                group.nextTaskIndex = index;
            }
        }

        private bool ExecuteTask(
            List<SliceableTask> ownerTasks,
            int taskIndex,
            long ownerBeginTimestamp,
            long ownerBudgetTicks,
            out bool removed)
        {
            SliceableTask task = ownerTasks[taskIndex];
            removed = false;
            if (task.schedulingState == SchedulingState.PendingRemove)
            {
                this.CompleteAndRemove(ownerTasks, taskIndex, task, SliceableTaskStatus.Cancelled);
                removed = true;
                return false;
            }

            Assert.AreEqual(SchedulingState.Queued, task.schedulingState);

            var taskBeginTimestamp = Stopwatch.GetTimestamp();
            var taskBudgetTicks = BudgetToTicks(task.timeBudgetPerFrame);
            var executed = false;
            while (true)
            {
                executed = true;
                var finished = false;
                task.schedulingState = SchedulingState.Executing;
                try
                {
                    finished = task.Execute();
                }
                catch (Exception e)
                {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    UnityEngine.Debug.LogError($"{e}\n......Registered: \n{task.creatingStackTrace}");
#else
                    UnityEngine.Debug.LogException(e);
#endif
                    this.CompleteAndRemove(ownerTasks, taskIndex, task, SliceableTaskStatus.Faulted, e);
                    removed = true;
                    break;
                }

                if (task.schedulingState == SchedulingState.PendingRemove)
                {
                    this.CompleteAndRemove(ownerTasks, taskIndex, task, SliceableTaskStatus.Cancelled);
                    removed = true;
                    break;
                }

                Assert.IsTrue(task.schedulingState == SchedulingState.Executing || task.schedulingState == SchedulingState.Queued);
                task.schedulingState = SchedulingState.Queued;

                if (finished)
                {
                    this.CompleteAndRemove(ownerTasks, taskIndex, task, SliceableTaskStatus.Succeeded);
                    removed = true;
                    break;
                }

                if (BudgetElapsed(taskBeginTimestamp, taskBudgetTicks) || BudgetElapsed(ownerBeginTimestamp, ownerBudgetTicks))
                {
                    break;
                }
            }

            return executed;
        }

        private void CompleteAndRemove(
            List<SliceableTask> ownerTasks,
            int taskIndex,
            SliceableTask task,
            SliceableTaskStatus finalStatus,
            Exception taskException = null)
        {
            task.schedulingState = SchedulingState.Detached;
            task.Complete(finalStatus, taskException);
            ownerTasks[taskIndex] = null;
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private void ValidateManagedTasks()
        {
            this.validationSet.Clear();
            foreach (SliceableTask task in this.standaloneTasks)
            {
                if (task != null)
                {
                    Assert.IsTrue(this.validationSet.Add(task));
                }
            }

            foreach (SliceableTaskGroup group in this.managedGroups)
            {
                if (group == null)
                {
                    continue;
                }

                foreach (SliceableTask task in group.tasks)
                {
                    if (task != null)
                    {
                        Assert.IsTrue(this.validationSet.Add(task));
                        Assert.AreEqual(group, task.group);
                    }
                }
            }
        }
#endif

        private void OnDestroy()
        {
            if (instance != this)
            {
                return;
            }

            foreach (SliceableTask task in this.standaloneTasks)
            {
                CancelTask(task);
            }

            foreach (SliceableTaskGroup group in this.managedGroups)
            {
                if (group == null)
                {
                    continue;
                }

                foreach (SliceableTask task in group.tasks)
                {
                    CancelTask(task);
                }

                group.tasks.Clear();
                group.registered = false;
                group.nextTaskIndex = 0;
            }

            this.standaloneTasks.Clear();
            this.managedGroups.Clear();
            instance = null;
        }

        private static void CancelTask(SliceableTask task)
        {
            if (task == null)
            {
                return;
            }

            task.schedulingState = SchedulingState.Detached;
            task.Complete(SliceableTaskStatus.Cancelled);
        }
    }
}
