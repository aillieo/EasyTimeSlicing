using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
            TestEnumFunc();
        }

        [ContextMenu(nameof(TestActionArray))]
        private void TestActionArray()
        {
            var actions = Enumerable.Range(1, 10).Select(TaskCreateHelper.CreateRandomTask).ToArray();
            SliceableTask task = SliceableTask.Start(0.01f, actions);
        }

        [ContextMenu(nameof(TestActionEnumerable))]
        private void TestActionEnumerable()
        {
            var actions = Enumerable.Range(1, 10).Select(TaskCreateHelper.CreateRandomTask);
            SliceableTask task = SliceableTask.Start(0.01f, actions);
        }

        [ContextMenu(nameof(TestStateMachineFunc))]
        private void TestStateMachineFunc()
        {
            OpenStateMachineFunc func = (ref int state) =>
            {
                TaskCreateHelper.ExecuteRandomTask(state);
                return state++ == 10;
            };
            SliceableTask task = SliceableTask.Start(0.01f, 1, func);
        }

        [ContextMenu(nameof(TestEnumFunc))]
        private void TestEnumFunc()
        {
            SliceableTask task = SliceableTask.Start(0.01f, EnumFunc);
        }

        private IEnumerator EnumFunc()
        {
            for (int state = 1; state <= 10; state++)
            {
                TaskCreateHelper.ExecuteRandomTask(state);
                yield return null;
            }
        }
    }
}
