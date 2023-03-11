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

            SliceableTask.Start(executionTime, InstantiateItem2);
        }

        [ContextMenu(nameof(RunInstantiateInCoroutine))]
        private void RunInstantiateInCoroutine()
        {
            if (prefab == null)
            {
                UnityEngine.Debug.LogError("prefab null");
                return;
            }

            StartCoroutine(InstantiateItem1());
        }

        private IEnumerator InstantiateItem1()
        {
            int index = 0;
            int total = range * range;

            while (index < total)
            {
                if (index < 0 || index >= total)
                {
                    break;
                }

                Vector2 basePos = -offset * Vector2.one * 0.5f * range;
                int x = index / range;
                int y = index % range;

                GameObject go = Instantiate(prefab, this.transform);
                go.transform.localPosition = new Vector3(basePos.x + offset.x * x, 0, basePos.y + offset.y * y);

                index++;

                yield return new WaitForSeconds(executionTime);
            }
        }

        private IEnumerator InstantiateItem2()
        {
            int index = 0;
            int total = range * range;

            while (index < total)
            {
                if (index < 0 || index >= total)
                {
                    yield break;
                }

                Vector2 basePos = -offset * Vector2.one * 0.5f * range;
                int x = index / range;
                int y = index % range;

                GameObject go = Instantiate(prefab, this.transform);
                go.transform.localPosition = new Vector3(basePos.x + offset.x * x, 0, basePos.y + offset.y * y);

                index++;

                yield return null;
            }
        }
    }
}
