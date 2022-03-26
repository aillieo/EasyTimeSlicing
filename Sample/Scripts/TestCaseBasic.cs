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
        }

        [ContextMenu(nameof(TestActionArray))]
        private void TestActionArray()
        {
            var actions = Enumerable.Range(1, 10).Select(TaskCreateHelper.CreateRandomTask).ToArray();
            SliceableTask task = new SliceableTask(0.01f, actions);
        }

        [ContextMenu(nameof(TestActionEnumerable))]
        private void TestActionEnumerable()
        {
            var actions = Enumerable.Range(1, 10).Select(TaskCreateHelper.CreateRandomTask);
            SliceableTask task = new SliceableTask(0.01f, actions);
        }

        [ContextMenu(nameof(TestStateMachineFunc))]
        private void TestStateMachineFunc()
        {
            StateMachineFunc func = (ref int state) =>
            {
                TaskCreateHelper.ExecuteRandomTask(state);
                return state++ == 10;
            };
            SliceableTask task = new SliceableTask(0.01f, 1, func);
        }
    }
}
