#region Statements

using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

#endregion

namespace Mirage.Libuv2kNG
{
    public class Libuv2kNGLogger
    {
        internal static LogType LogType = LogType.Warning;

        /// <summary>
        ///     Log messages to unity editor only.
        /// </summary>
        /// <param name="message">The message we want to log.</param>
        /// <param name="logType">The type of message we want to log.</param>
        [Conditional("UNITY_EDITOR")]
        public static void Log(string message, LogType logType = LogType.Log)
        {
            bool log = (int)LogType <= (int)logType && logType == LogType;

            if (!log) return;

            switch (logType)
            {
                case LogType.Log:
                    Debug.Log($"<color=green> {message} </color>");
                    break;
                case LogType.Warning:
                    Debug.LogWarning($"<color=orange> {message} </color>");
                    break;
                case LogType.Error:
                    Debug.LogError($"<color=red> {message} </color>");
                    break;
                default:
                    Debug.LogException(new Exception($"<color=red> {message} </color>"));
                    break;

            }
        }
    }
}
