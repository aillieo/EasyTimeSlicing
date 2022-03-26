using System.Collections;
using System.Collections.Generic;
using Action = System.Action;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using static AillieoUtils.EasyTimeSlicing.SliceableTask;

namespace AillieoUtils.EasyTimeSlicing.Sample
{
    public class TestCaseBasic : MonoBehaviour
    {
        private void Start()
        {
            TestActionArray();
            TestActionEnumerable();
            TestStateMachineFunc();
        }

        [ContextMenu(nameof(TestActionArray))]
        private void TestActionArray()
        {
            var actions = Enumerable.Range(1, 10).Select<int, Action>(i => () => RandomTask(i)).ToArray();
            SliceableTask task = new SliceableTask(0.01f, actions);
        }

        [ContextMenu(nameof(TestActionEnumerable))]
        private void TestActionEnumerable()
        {
            var actions = Enumerable.Range(1, 10).Select<int, Action>(i => () => RandomTask(i));
            SliceableTask task = new SliceableTask(0.01f, actions);
        }

        [ContextMenu(nameof(TestStateMachineFunc))]
        private void TestStateMachineFunc()
        {
            StateMachineFunc func = (ref int state) =>
            {
                RandomTask(state);
                return state++ == 10;
            };
            SliceableTask task = new SliceableTask(0.01f, 1, func);
        }

        private void RandomTask(int index)
        {
            float begin = Time.realtimeSinceStartup;
            Task.Delay(Random.Range(1, 6)).Wait();
            float end = Time.realtimeSinceStartup;
            UnityEngine.Debug.LogError($"In frame {Time.frameCount}: task {index} cost time {end - begin} s");
        }
    }
}
