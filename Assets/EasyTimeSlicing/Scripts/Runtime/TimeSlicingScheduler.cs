// -----------------------------------------------------------------------
// <copyright file="TimeSlicingScheduler.cs" company="AillieoTech">
// Copyright (c) AillieoTech. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AillieoUtils.EasyTimeSlicing
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEngine;
    using UnityEngine.Assertions;

    [DefaultExecutionOrder(-100)]
    internal class TimeSlicingScheduler : MonoBehaviour
    {
        private static TimeSlicingScheduler instance;

        private readonly List<SliceableTask> managedTasks = new List<SliceableTask>();

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        // 检查是否有重复的
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
                throw new Exception();
            }

            if (task.status == TaskStatus.PendingRemove)
            {
                task.status = TaskStatus.Queued;
            }
            else if (task.status == TaskStatus.Detached)
            {
                this.managedTasks.Add(task);
                task.status = TaskStatus.Queued;
            }
            else
            {
                throw new Exception($"Unexpected state {task.status}");
            }
        }

        internal void Remove(SliceableTask task)
        {
            if (task.status == TaskStatus.Executing || task.status == TaskStatus.Queued)
            {
                task.status = TaskStatus.PendingRemove;
            }
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
            var taskToRemove = 0;
            var taskCount = this.managedTasks.Count;
            for (var i = 0; i < taskCount; ++i)
            {
                SliceableTask task = this.managedTasks[i];
                if (task == null)
                {
                    taskToRemove++;
                    continue;
                }

                if (task.status == TaskStatus.PendingRemove)
                {
                    task.status = TaskStatus.Detached;
                    this.managedTasks[i] = null;
                    taskToRemove++;
                    continue;
                }

                Assert.AreEqual(task.status, TaskStatus.Queued);

                var beginTime = Time.realtimeSinceStartup;
                var executionTime = task.executionTimePerFrame;
                while (true)
                {
                    var finished = false;
                    task.status = TaskStatus.Executing;
                    try
                    {
                        finished = task.Execute();
                    }
                    catch (Exception e)
                    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        Debug.LogError($"{e}\n......Registered: \n{task.creatingStackTrace}");
#else
                        UnityEngine.Debug.LogError(e.StackTrace);
#endif
                    }

                    if (task.status == TaskStatus.PendingRemove)
                    {
                        task.status = TaskStatus.Detached;
                        this.managedTasks[i] = null;
                        taskToRemove++;
                        break;
                    }

                    Assert.AreEqual(task.status, TaskStatus.Executing);
                    task.status = TaskStatus.Queued;

                    if (finished)
                    {
                        task.status = TaskStatus.Finished;
                        this.managedTasks[i] = null;
                        taskToRemove++;
                        break;
                    }
                    else if (Time.realtimeSinceStartup - beginTime >= executionTime)
                    {
                        break;
                    }
                }
            }

            if (taskToRemove > 8 || taskToRemove >= (taskCount >> 2))
            {
                this.managedTasks.RemoveAll(o => o == null);
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // 检查是否有重复的
            this.validationSet.Clear();
            this.validationSet.UnionWith(this.managedTasks);
            Assert.AreEqual(this.managedTasks.Count(o => o != null), this.validationSet.Count(o => o != null));
#endif
        }
    }
}
