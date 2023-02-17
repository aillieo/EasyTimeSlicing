// -----------------------------------------------------------------------
// <copyright file="TimeSlicingUtils.cs" company="AillieoTech">
// Copyright (c) AillieoTech. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AillieoUtils.EasyTimeSlicing
{
    using System;
    using UnityEngine;
    using UnityEngine.Assertions;

    public static class TimeSlicingUtils
    {
        private static int cachedFrameRate = int.MinValue;
        private static float cachedFrameInterval;

        public static float frameInterval
        {
            get
            {
                if (Application.targetFrameRate != cachedFrameRate)
                {
                    cachedFrameRate = Application.targetFrameRate;
                    cachedFrameInterval = 1.0f / cachedFrameRate;
                }

                return cachedFrameInterval;
            }
        }

        public static float timeSinceFrameStart
        {
            get { return Time.realtimeSinceStartup - Time.unscaledTime; }
        }

        public static float timeBudgetEstimated
        {
            get
            {
                return frameInterval - timeSinceFrameStart;
            }
        }

        public static bool TryExecute(Action action, float expectedExecutionTime)
        {
            Assert.IsNotNull(action);

            if (CheckExecuteTime(expectedExecutionTime))
            {
                action.Invoke();
                return true;
            }

            return false;
        }

        public static bool TryExecute<T>(Action<T> action, T data, float expectedExecutionTime)
        {
            Assert.IsNotNull(action);

            if (CheckExecuteTime(expectedExecutionTime))
            {
                action.Invoke(data);
                return true;
            }

            return false;
        }

        public static bool TryExecute<T>(Func<T> func, float expectedExecutionTime, out T result)
        {
            Assert.IsNotNull(func);

            if (CheckExecuteTime(expectedExecutionTime))
            {
                result = func.Invoke();
                return true;
            }

            result = default;
            return false;
        }

        public static bool TryExecute<T, TResult>(Func<T, TResult> func, T data, float expectedExecutionTime, out TResult result)
        {
            Assert.IsNotNull(func);

            if (CheckExecuteTime(expectedExecutionTime))
            {
                result = func.Invoke(data);
                return true;
            }

            result = default;
            return false;
        }

        private static bool CheckExecuteTime(float expectedExecutionTime)
        {
            if (expectedExecutionTime < 0)
            {
                throw new ArgumentException("Value should greater than 0", nameof(expectedExecutionTime));
            }

            if (expectedExecutionTime >= frameInterval)
            {
                var message = $"Too much time requested, the task will never execute: expectedExecutionTime={expectedExecutionTime} while frameInterval={frameInterval}.";
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                UnityEngine.Debug.LogError(message);
#else
                UnityEngine.Debug.LogWarning(message);
#endif
            }

            if (expectedExecutionTime > timeBudgetEstimated)
            {
                return false;
            }

            return true;
        }
    }
}
