namespace AillieoUtils.EasyTimeSlicing.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using System.Threading;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public class EasyTimeSlicingTests
    {
        [Test]
        public void StartRejectsNonFiniteTimeBudgets()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SliceableTask.Start(float.NaN, () => true));
            Assert.Throws<ArgumentOutOfRangeException>(() => SliceableTask.Start(float.PositiveInfinity, () => true));
            Assert.Throws<ArgumentOutOfRangeException>(() => SliceableTask.Start(-0.001f, () => true));
        }

        [Test]
        public void FrameIntervalIsAlwaysPositive()
        {
            Assert.Greater(TimeSlicingUtils.frameInterval, 0);
            Assert.IsFalse(float.IsInfinity(TimeSlicingUtils.frameInterval));
            Assert.IsFalse(float.IsNaN(TimeSlicingUtils.frameInterval));
        }

        [Test]
        public void StartRejectsUnknownOverrunPolicy()
        {
            var options = new SliceableTaskOptions
            {
                overrunPolicy = (SliceOverrunPolicy)999,
            };

            Assert.Throws<ArgumentOutOfRangeException>(() => SliceableTask.Start(0.01f, options, () => true));
        }

        [UnityTest]
        public IEnumerator TimeBudgetSetterRejectsInvalidValues()
        {
            SliceableTask task = SliceableTask.Start(0, () => false);

            Assert.Throws<ArgumentOutOfRangeException>(() => task.timeBudgetPerFrame = float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => task.timeBudgetPerFrame = float.PositiveInfinity);
            Assert.Throws<ArgumentOutOfRangeException>(() => task.timeBudgetPerFrame = -0.001f);

            task.Cancel();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExceptionFaultsTaskInsteadOfRetrying()
        {
            var executions = 0;
            LogAssert.Expect(LogType.Error, new Regex("boom"));
            SliceableTask task = SliceableTask.Start(0.01f, (SliceableTask.ClosedStateMachineFunc)(() =>
            {
                executions++;
                throw new InvalidOperationException("boom");
            }));

            yield return null;
            yield return null;

            Assert.AreEqual(1, executions);
            Assert.AreEqual(SliceableTaskStatus.Faulted, task.status);
            Assert.IsTrue(task.isCompleted);
            Assert.IsInstanceOf<InvalidOperationException>(task.exception);
        }

        [UnityTest]
        public IEnumerator DirectTaskUsesObservableHandleStatus()
        {
            ISliceableTaskHandle handle = SliceableTask.Start(0, () => true);

            Assert.AreEqual(SliceableTaskStatus.Pending, handle.status);
            Assert.IsFalse(handle.isCompleted);

            yield return null;

            Assert.AreEqual(SliceableTaskStatus.Succeeded, handle.status);
            Assert.IsTrue(handle.isCompleted);
            Assert.IsNull(handle.exception);
        }

        [UnityTest]
        public IEnumerator OverrunErrorDoesNotFaultTask()
        {
            var options = new SliceableTaskOptions
            {
                overrunPolicy = SliceOverrunPolicy.Error,
            };
            var executions = 0;
            LogAssert.Expect(LogType.Error, new Regex("exceeded its .* budget"));
            SliceableTask task = SliceableTask.Start(0.0001f, options, () =>
            {
                Thread.Sleep(2);
                executions++;
                return executions == 2;
            });

            for (var frame = 0; frame < 4 && !task.isCompleted; frame++)
            {
                yield return null;
            }

            Assert.AreEqual(SliceableTaskStatus.Succeeded, task.status);
            Assert.IsNull(task.exception);
        }

        [UnityTest]
        public IEnumerator ReentrantHigherPriorityEnqueueIsNotLost()
        {
            SliceableTaskQueue queue = SliceableTaskQueue.Create(0.01f);
            var executions = 0;
            queue.Enqueue(
                () => queue.Enqueue(() => executions++, SliceableTaskQueue.Priority.High),
                SliceableTaskQueue.Priority.Medium);

            for (var frame = 0; frame < 5 && executions == 0; frame++)
            {
                yield return null;
            }

            Assert.AreEqual(1, executions);
            Assert.AreEqual(0, queue.pendingTasks);
        }

        [UnityTest]
        public IEnumerator CancelledQueueEntriesRespectTheFrameBudget()
        {
            SliceableTaskQueue queue = SliceableTaskQueue.Create(0);
            var executions = 0;
            queue.EnqueueWithHandle(() => { }, SliceableTaskQueue.Priority.High).Cancel();
            queue.EnqueueWithHandle(() => { }, SliceableTaskQueue.Priority.High).Cancel();
            queue.Enqueue(() => executions++, SliceableTaskQueue.Priority.Low);

            yield return null;

            Assert.AreEqual(0, executions);
            Assert.AreEqual(1, queue.pendingTasks);

            queue.ClearAll();
            yield return null;
        }

        [UnityTest]
        public IEnumerator QueueUsesStrictPriorityOrder()
        {
            SliceableTaskQueue queue = SliceableTaskQueue.Create(0);
            var order = new List<string>();
            queue.Enqueue(() => order.Add("low"), SliceableTaskQueue.Priority.Low);
            queue.Enqueue(() => order.Add("medium"), SliceableTaskQueue.Priority.Medium);
            queue.Enqueue(() => order.Add("high-1"), SliceableTaskQueue.Priority.High);
            queue.Enqueue(() => order.Add("high-2"), SliceableTaskQueue.Priority.High);

            for (var frame = 0; frame < 8 && queue.pendingTasks > 0; frame++)
            {
                yield return null;
            }

            CollectionAssert.AreEqual(new[] { "high-1", "high-2", "medium", "low" }, order);
        }

        [UnityTest]
        public IEnumerator PauseRemainsEffectiveWhenWorkIsEnqueued()
        {
            SliceableTaskQueue queue = SliceableTaskQueue.Create(0);
            var executions = 0;
            queue.Enqueue(() => executions++);
            queue.Pause();
            queue.Enqueue(() => executions++);

            yield return null;
            Assert.IsFalse(queue.scheduling);
            Assert.AreEqual(0, executions);

            queue.Resume();
            for (var frame = 0; frame < 5 && queue.pendingTasks > 0; frame++)
            {
                yield return null;
            }

            Assert.AreEqual(2, executions);
            Assert.AreEqual(0, queue.pendingTasks);
        }

        [Test]
        public void ClearAllCancelsHandles()
        {
            SliceableTaskQueue queue = SliceableTaskQueue.Create(0);
            queue.Pause();
            SliceableTaskQueue.Handle handle = queue.EnqueueWithHandle(() => { });

            queue.ClearAll();

            Assert.AreEqual(SliceableTaskStatus.Cancelled, handle.status);
            Assert.IsTrue(handle.isCompleted);
            Assert.AreEqual(0, queue.pendingTasks);
        }

        [UnityTest]
        public IEnumerator QueueHandleUsesObservableStatus()
        {
            SliceableTaskQueue queue = SliceableTaskQueue.Create(0);
            ISliceableTaskHandle handle = queue.EnqueueWithHandle(() => { });

            Assert.AreEqual(SliceableTaskStatus.Pending, handle.status);
            Assert.IsFalse(handle.isCompleted);

            yield return null;

            Assert.AreEqual(SliceableTaskStatus.Succeeded, handle.status);
            Assert.IsTrue(handle.isCompleted);
            Assert.IsNull(handle.exception);
        }

        [UnityTest]
        public IEnumerator FaultedHandleCapturesException()
        {
            SliceableTaskQueue queue = SliceableTaskQueue.Create(0);
            LogAssert.Expect(LogType.Exception, new Regex("queue boom"));
            SliceableTaskQueue.Handle handle = queue.EnqueueWithHandle(
                () => throw new InvalidOperationException("queue boom"));

            yield return null;

            Assert.AreEqual(SliceableTaskStatus.Faulted, handle.status);
            Assert.IsTrue(handle.isCompleted);
            Assert.IsInstanceOf<InvalidOperationException>(handle.exception);
        }

        [UnityTest]
        public IEnumerator CancelDisposesEnumerator()
        {
            var disposed = false;

            IEnumerator Work()
            {
                try
                {
                    while (true)
                    {
                        yield return null;
                    }
                }
                finally
                {
                    disposed = true;
                }
            }

            SliceableTask task = SliceableTask.Start(0, Work);
            yield return null;
            task.Cancel();

            Assert.AreEqual(SliceableTaskStatus.Cancelled, task.status);
            Assert.IsTrue(task.isCompleted);

            yield return null;

            Assert.IsTrue(disposed);
            Assert.AreEqual(SliceableTaskStatus.Cancelled, task.status);
        }

        [UnityTest]
        public IEnumerator CancellationInsideCallbackIsNotOverwrittenBySuccess()
        {
            SliceableTask task = null;
            task = SliceableTask.Start(0, () =>
            {
                task.Cancel();
                return true;
            });

            yield return null;

            Assert.AreEqual(SliceableTaskStatus.Cancelled, task.status);
            Assert.IsTrue(task.isCompleted);
        }

        [UnityTest]
        public IEnumerator UngroupedTasksUseIndependentBudgets()
        {
            var firstExecutions = 0;
            var secondExecutions = 0;
            SliceableTask first = SliceableTask.Start(0, () =>
            {
                firstExecutions++;
                return false;
            });
            SliceableTask second = SliceableTask.Start(0, () =>
            {
                secondExecutions++;
                return false;
            });

            yield return null;

            Assert.AreEqual(1, firstExecutions);
            Assert.AreEqual(1, secondExecutions);

            first.Cancel();
            second.Cancel();
            yield return null;
        }

        [UnityTest]
        public IEnumerator TasksInGroupShareOneBudgetAndRotateAcrossFrames()
        {
            SliceableTaskGroup group = SliceableTaskGroup.Create("SharedBudgetTest", 0);
            var firstExecutions = 0;
            var secondExecutions = 0;
            SliceableTask first = group.Start(0, () =>
            {
                firstExecutions++;
                return false;
            });
            SliceableTask second = group.Start(0, () =>
            {
                secondExecutions++;
                return false;
            });

            yield return null;

            Assert.AreEqual(1, firstExecutions + secondExecutions);

            yield return null;

            Assert.AreEqual(1, firstExecutions);
            Assert.AreEqual(1, secondExecutions);

            first.Cancel();
            second.Cancel();
            yield return null;
        }

        [UnityTest]
        public IEnumerator DifferentGroupsUseIndependentBudgets()
        {
            SliceableTaskGroup firstGroup = SliceableTaskGroup.Create("FirstGroupTest", 0);
            SliceableTaskGroup secondGroup = SliceableTaskGroup.Create("SecondGroupTest", 0);
            var firstExecutions = 0;
            var secondExecutions = 0;
            SliceableTask first = firstGroup.Start(0, () =>
            {
                firstExecutions++;
                return false;
            });
            SliceableTask second = secondGroup.Start(0, () =>
            {
                secondExecutions++;
                return false;
            });

            yield return null;

            Assert.AreEqual(1, firstExecutions);
            Assert.AreEqual(1, secondExecutions);

            first.Cancel();
            second.Cancel();
            yield return null;
        }

        [UnityTest]
        public IEnumerator QueueAndGroupUseIndependentBudgets()
        {
            SliceableTaskQueue queue = SliceableTaskQueue.Create(0);
            SliceableTaskGroup group = SliceableTaskGroup.Create("QueuePeerTest", 0);
            var queueExecutions = 0;
            var groupExecutions = 0;
            queue.Enqueue(() => queueExecutions++);
            SliceableTask groupedTask = group.Start(0, () =>
            {
                groupExecutions++;
                return false;
            });

            yield return null;

            Assert.AreEqual(1, queueExecutions);
            Assert.AreEqual(1, groupExecutions);

            groupedTask.Cancel();
            yield return null;
        }

        [UnityTest]
        public IEnumerator GroupCanBeReusedAfterBecomingEmpty()
        {
            SliceableTaskGroup group = SliceableTaskGroup.Create("ReusableGroupTest", 0);
            var executions = 0;
            SliceableTask first = group.Start(0, () =>
            {
                executions++;
                return true;
            });

            yield return null;

            Assert.AreEqual(SliceableTaskStatus.Succeeded, first.status);
            Assert.AreEqual(0, group.activeTasks);

            SliceableTask second = group.Start(0, () =>
            {
                executions++;
                return true;
            });

            yield return null;

            Assert.AreEqual(SliceableTaskStatus.Succeeded, second.status);
            Assert.AreEqual(2, executions);
            Assert.AreEqual(0, group.activeTasks);
        }

        [Test]
        public void GroupRejectsInvalidConfiguration()
        {
            Assert.Throws<ArgumentException>(() => SliceableTaskGroup.Create("", 0.001f));
            Assert.Throws<ArgumentOutOfRangeException>(() => SliceableTaskGroup.Create("InvalidBudget", float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => SliceableTaskGroup.Create("InvalidBudget", float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => SliceableTaskGroup.Create("InvalidBudget", -0.001f));
        }

        [Test]
        public void GroupTimeBudgetSetterRejectsInvalidValues()
        {
            SliceableTaskGroup group = SliceableTaskGroup.Create("SetterValidation", 0.001f);

            Assert.Throws<ArgumentOutOfRangeException>(() => group.timeBudgetPerFrame = float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => group.timeBudgetPerFrame = float.PositiveInfinity);
            Assert.Throws<ArgumentOutOfRangeException>(() => group.timeBudgetPerFrame = -0.001f);
        }

        [Test]
        public void InvalidConfigurationDoesNotInvokeEnumeratorFactory()
        {
            var invoked = false;
            SliceableTask.EnumFunc factory = () =>
            {
                invoked = true;
                return EmptyEnumerator();
            };

            Assert.Throws<ArgumentOutOfRangeException>(() => SliceableTask.Start(float.NaN, factory));
            Assert.IsFalse(invoked);
        }

        private static IEnumerator EmptyEnumerator()
        {
            yield break;
        }
    }
}
