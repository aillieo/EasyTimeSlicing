using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AillieoUtils.EasyTimeSlicing.Sample
{
    public class TestCaseInstantiate : MonoBehaviour
    {
        [SerializeField]
        private GameObject prefab;
        [SerializeField]
        private int range = 8;
        [SerializeField]
        private Vector2 offset = new Vector2(3f, 3f);
        [SerializeField]
        private float executionTime = 0.001f;

        private void Start()
        {
            RunInstantiateTask();
        }

        [ContextMenu(nameof(RunInstantiateTask))]
        private void RunInstantiateTask()
        {
            if (prefab == null)
            {
                UnityEngine.Debug.LogError("prefab null");
                return;
            }

            new SliceableTask(executionTime, 0, InstantiateItem);
        }

        private bool InstantiateItem(ref int index)
        {
            int total = range * range;

            if (index < 0 || index >= total)
            {
                return true;
            }

            Vector2 basePos = - offset * Vector2.one * 0.5f * range;
            int x = index / range;
            int y = index % range;

            GameObject go = Instantiate(prefab, this.transform);
            go.transform.localPosition = new Vector3(basePos.x + offset.x * x, 0, basePos.y + offset.y * y);

            index++;

            return index >= total;
        }
    }
}
