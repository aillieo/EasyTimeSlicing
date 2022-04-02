using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;

namespace AillieoUtils.EasyTimeSlicing
{
    [DefaultExecutionOrder(-100)]
    public class TimeSlicingScheduler : MonoBehaviour
    {
        //[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateInstance()
        {
            if (instance == null)
            {
                GameObject go = new GameObject($"[{nameof(TimeSlicingScheduler)}]");
                instance = go.AddComponent<TimeSlicingScheduler>();
                DontDestroyOnLoad(go);
            }
        }

        private static TimeSlicingScheduler instance;

        public static TimeSlicingScheduler Instance
        {
            get
            {
                CreateInstance();
                return instance;
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
            }
        }

        private readonly List<SliceableTask> managedTasks = new List<SliceableTask>();

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
                managedTasks.Add(task);
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

        private void Update()
        {
            int taskToRemove = 0;
            int taskCount = managedTasks.Count;
            for (int i = 0; i < taskCount; ++i)
            {
                SliceableTask task = managedTasks[i];
                if (task == null)
                {
                    taskToRemove++;
                    continue;
                }

                if (task.status == TaskStatus.PendingRemove)
                {
                    task.status = TaskStatus.Detached;
                    managedTasks[i] = null;
                    taskToRemove++;
                    continue;
                }

                Assert.AreEqual(task.status, TaskStatus.Queued);

                float beginTime = Time.realtimeSinceStartup;
                float executionTime = task.executionTimePerFrame;
                while (true)
                {
                    bool finished = false;
                    task.status = TaskStatus.Executing;
                    try
                    {
                        finished = task.Execute();
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogError(e.StackTrace);
                    }

                    if (task.status == TaskStatus.PendingRemove)
                    {
                        task.status = TaskStatus.Detached;
                        managedTasks[i] = null;
                        taskToRemove++;
                        break;
                    }

                    Assert.AreEqual(task.status, TaskStatus.Executing);
                    task.status = TaskStatus.Queued;

                    if (finished)
                    {
                        task.status = TaskStatus.Finished;
                        managedTasks[i] = null;
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
                managedTasks.RemoveAll(o => o == null);
            }

#if DEBUG
            // 检查是否有重复的
            Assert.AreEqual(managedTasks.Count(o => o != null), new HashSet<SliceableTask>(managedTasks).Count);
#endif
        }
    }
}
