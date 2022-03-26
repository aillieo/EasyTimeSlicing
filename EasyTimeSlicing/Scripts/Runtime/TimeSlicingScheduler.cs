using System;
using System.Collections;
using System.Collections.Generic;
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

        private readonly List<AbstractSliceableTask> managedTasks = new List<AbstractSliceableTask>();

        public void Add(AbstractSliceableTask task)
        {
            Assert.AreEqual(task.status, TaskStatus.Detached);
            managedTasks.Add(task);
            task.status = TaskStatus.Queued;
        }

        private void Update()
        {
            int taskToRemove = 0;
            int taskCount = managedTasks.Count;
            for (int i = 0; i < taskCount; ++i)
            {
                AbstractSliceableTask task = managedTasks[i];
                if (task == null)
                {
                    taskToRemove++;
                    continue;
                }

                if (task.status == TaskStatus.Detached || task.status == TaskStatus.Finished)
                {
                    taskToRemove++;
                    continue;
                }

                if (task.status == TaskStatus.Executing)
                {
                    continue;
                }

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
                    finally
                    {
                        task.status = TaskStatus.Queued;
                    }

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
        }
    }
}
