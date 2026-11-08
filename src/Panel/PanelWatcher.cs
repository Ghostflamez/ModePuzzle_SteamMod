using ModePuzzle.Game;
using UnityEngine;

namespace ModePuzzle.UI
{
    /// <summary>
    /// While the optimizer window is open, checks the focused machine twice a second and rebuilds the window
    /// when its bay is expanded or a module is added, removed, moved, rotated or flipped. Stops when the window
    /// is gone. Ticked by <see cref="PanelTicker"/>.
    /// </summary>
    static class PanelWatcher
    {
        const float IntervalSeconds = OptimizerTuning.WatchSeconds;
        static bool active;
        static float next, nextPoll;
        static int machineId;
        static string signature;

        /// <summary>Starts (or retargets) watching after the window was built for this machine.</summary>
        public static void Watch(int machineUniqueId)
        {
            if (!active) { active = true; PanelTicker.Run(Tick, Gone); }
            machineId = machineUniqueId;
            signature = LiveReader.Signature(machineUniqueId);
            next = Time.unscaledTime + IntervalSeconds;
        }

        public static void Stop()
        {
            if (!active) return;
            active = false;
            PanelTicker.Stop();
        }

        static void Gone()
        {
            if (!active) return;
            active = false;
            OptimizerPanel.WindowGone();
        }

        static void Tick()
        {
            if (Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + OptimizerTuning.PollSeconds;
                if (!OptimizerPanel.IsOpen) { OptimizerPanel.WindowGone(); Stop(); return; }
                OptimizerPanel.PollSearch();
            }
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + IntervalSeconds;
            if (!OptimizerPanel.IsOpen) { Stop(); return; }
            string now;
            try { now = LiveReader.Signature(machineId); }
            catch (System.Exception e) { ModePuzzleMod.Log?.Warning("Watcher: signature failed: " + e); OptimizerPanel.MachineChanged(); return; }
            bool machineChanged = now != signature;
            if (!machineChanged && !OptimizerPanel.SnapshotChanged()) return;
            ModePuzzleMod.Log?.Log(machineChanged ? $"Machine {machineId} changed; panel refreshed" : $"Candidates of machine {machineId} changed");
            signature = now;
            OptimizerPanel.MachineChanged();
        }
    }
}
