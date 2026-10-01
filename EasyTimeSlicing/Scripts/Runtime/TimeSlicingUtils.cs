// -----------------------------------------------------------------------
// <copyright file="TimeSlicingUtils.cs" company="AillieoTech">
// Copyright (c) AillieoTech. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AillieoUtils.EasyTimeSlicing
{
    using System;
    using UnityEngine;
    using UnityEngine.Rendering;

    /// <summary>
    /// Provides utility methods for time-sliced execution of tasks.
    /// </summary>
    public static class TimeSlicingUtils
    {
        /// <summary>
        /// Gets the time interval between frames based on <see cref="Application.targetFrameRate"/>.
        /// </summary>
        public static float frameInterval
        {
            get
            {
                var effectiveFrameRate = OnDemandRendering.effectiveRenderFrameRate;
                if (effectiveFrameRate > 0)
                {
                    effectiveFrameRate *= Mathf.Max(1, OnDemandRendering.renderFrameInterval);
                }

                if (effectiveFrameRate <= 0)
                {
                    var refreshRate = GetRefreshRate();
                    if (QualitySettings.vSyncCount > 0 && refreshRate > 0)
                    {
                        effectiveFrameRate = refreshRate / QualitySettings.vSyncCount;
                    }
                    else if (Application.targetFrameRate > 0)
                    {
                        effectiveFrameRate = Application.targetFrameRate;
                    }
                    else if (refreshRate > 0)
                    {
                        effectiveFrameRate = refreshRate;
                    }
                    else
                    {
                        effectiveFrameRate = 60;
                    }
                }

                return 1.0f / effectiveFrameRate;
            }
        }

        /// <summary>
        /// Gets time elapsed since the start of the current frame.
        /// </summary>
        public static float timeSinceFrameStart
        {
            get { return Time.realtimeSinceStartup - Time.unscaledTime; }
        }

        /// <summary>
        /// Gets the estimated remaining time available in the current frame for executing a task.
        /// </summary>
        public static float timeBudgetEstimated
        {
            get
            {
                return frameInterval - timeSinceFrameStart;
            }
        }

        /// <summary>
        /// Try to execute an <see cref="Action"/> within the given expected execution time.
        /// </summary>
        /// <param name="action">The <see cref="Action"/> to execute.</param>
        /// <param name="expectedExecutionTime">The expected execution time in seconds.</param>
        /// <returns>Whether the execution was executed.</returns>
        public static bool TryExecute(Action action, float expectedExecutionTime)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (CheckExecuteTime(expectedExecutionTime))
            {
                action.Invoke();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Try to execute an <see cref="Action{T}"/> within the given expected execution time.
        /// </summary>
        /// <typeparam name="T">The type of data to pass to the <see cref="Action{T}"/>.</typeparam>
        /// <param name="action">The <see cref="Action{T}"/> to execute.</param>
        /// <param name="data">The data to pass to the <see cref="Action{T}"/>.</param>
        /// <param name="expectedExecutionTime">The expected execution time in seconds.</param>
        /// <returns>Whether the execution was executed.</returns>
        public static bool TryExecute<T>(Action<T> action, T data, float expectedExecutionTime)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (CheckExecuteTime(expectedExecutionTime))
            {
                action.Invoke(data);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Try to execute a <see cref="Func{TResult}"/> within the given expected execution time.
        /// </summary>
        /// <typeparam name="TResult">The return type of the <see cref="Func{TResult}"/>.</typeparam>
        /// <param name="func">The <see cref="Func{TResult}"/> to execute.</param>
        /// <param name="expectedExecutionTime">The expected execution time in seconds.</param>
        /// <param name="result">The result of the <see cref="Func{TResult}"/>.</param>
        /// <returns>Whether the execution was executed.</returns>
        public static bool TryExecute<TResult>(Func<TResult> func, float expectedExecutionTime, out TResult result)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            if (CheckExecuteTime(expectedExecutionTime))
            {
                result = func.Invoke();
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>
        /// Try to execute a <see cref="Func{T, TResult}"/> within the given expected execution time.
        /// </summary>
        /// <typeparam name="T">The type of data to pass to the <see cref="Func{T, TResult}"/>.</typeparam>
        /// <typeparam name="TResult">The return type of the <see cref="Func{TResult}"/>.</typeparam>
        /// <param name="func">The <see cref="Func{T, TResult}"/> to execute.</param>
        /// <param name="data">The data to pass to the <see cref="Func{T, TResult}"/>.</param>
        /// <param name="expectedExecutionTime">The expected execution time in seconds.</param>
        /// <param name="result">The result of the <see cref="Func{TResult}"/>.</param>
        /// <returns>Whether the execution was executed.</returns>
        public static bool TryExecute<T, TResult>(Func<T, TResult> func, T data, float expectedExecutionTime, out TResult result)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

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
            if (float.IsNaN(expectedExecutionTime) || float.IsInfinity(expectedExecutionTime) || expectedExecutionTime < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedExecutionTime), expectedExecutionTime, "Expected execution time must be a finite, non-negative value.");
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

        private static int GetRefreshRate()
        {
#if UNITY_2022_2_OR_NEWER
            return (int)Math.Round(Screen.currentResolution.refreshRateRatio.value);
#else
            return Screen.currentResolution.refreshRate;
#endif
        }
    }
}
