using System;
using System.Collections.Generic;

namespace GregMod.Backplanes
{
    /// <summary>
    /// Re-entrancy guard keyed by NATIVE object pointers.
    ///
    /// v1.x guarded with a HashSet&lt;object&gt; using reference equality on
    /// BOXED Il2Cpp wrappers: every field read boxes a new managed wrapper, so
    /// Contains() never hit and Server.Awake/Start postfixes recursed into
    /// their own Configure path until the process died with 0xC00000FD
    /// (stack overflow) on saves containing modded servers.
    ///
    /// Native pointers are stable per Unity object, so this guard actually holds.
    /// Includes a global depth cap as a last-resort circuit breaker.
    /// </summary>
    internal static class RepairGuard
    {
        private static readonly HashSet<IntPtr> ActiveRepairs = new HashSet<IntPtr>();
        private static int _depth;
        private const int MaxDepth = 8;
        private static readonly object Sync = new object();

        internal static bool TryEnter(IntPtr nativePtr)
        {
            lock (Sync)
            {
                if (_depth >= MaxDepth)
                    return false;
                if (nativePtr != IntPtr.Zero && !ActiveRepairs.Add(nativePtr))
                    return false;
                _depth++;
                return true;
            }
        }

        internal static void Exit(IntPtr nativePtr)
        {
            lock (Sync)
            {
                if (nativePtr != IntPtr.Zero)
                    ActiveRepairs.Remove(nativePtr);
                if (_depth > 0)
                    _depth--;
            }
        }
    }
}
