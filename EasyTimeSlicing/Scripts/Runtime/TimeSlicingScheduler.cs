using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

        public void Add(SliceableTask task)
        {
            managedTasks.Add(task);
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

                float beginTime = Time.realtimeSinceStartup;
                float executionTime = task.executionTimePerFrame;
                while (true)
                {
                    bool finished = false;
                    try
                    {
                        finished = task.Execute();
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogError(e.StackTrace);
                        finished = true;
                    }

                    if (finished)
                    {
                        task.taskStatus = SliceableTask.TaskStatus.Detached;
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
