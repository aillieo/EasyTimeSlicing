using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Action = System.Action;

namespace AillieoUtils.EasyTimeSlicing.Sample
{
    public static class TaskCreateHelper
    {
        public static Action CreateRandomTask(int index)
        {
            return () => ExecuteRandomTask(index);
        }

        public static void ExecuteRandomTask(int index)
        {
            float begin = Time.realtimeSinceStartup;
            Task.Delay(Random.Range(1, 6)).Wait();
            float end = Time.realtimeSinceStartup;
            UnityEngine.Debug.LogError($"In frame {Time.frameCount}: task {index} cost time {end - begin} s");
        }
    }
}
