using System;
using MelonLoader;

namespace GregMod.Backplanes
{
    internal static class Log
    {
        internal static void Info(string message) => MelonLogger.Msg(message);
        internal static void Warning(string message) => MelonLogger.Warning(message);
        internal static void Error(string message) => MelonLogger.Error(message);
        internal static void Error(string message, Exception ex) => MelonLogger.Error($"{message} {ex}");
    }
}
