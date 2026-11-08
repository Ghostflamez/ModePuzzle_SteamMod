using ModePuzzle.Game;
using UnityEngine;

namespace ModePuzzle.UI
{
    /// <summary>
    /// While the optimizer window is open, checks the focused machine twice a second and rebuilds the window
    /// when its bay is expanded or a module is added, removed, moved, rotated or flipped. Destroys itself
    /// when the window is gone.
    /// </summary>
    sealed class PanelWatcher : MonoBehaviour
    {
        const float IntervalSeconds = 0.5f;

        static PanelWatcher instance;
        float next;
        int machineId;
        string signature;

        /// <summary>Starts (or retargets) watching after the window was built for this machine.</summary>
        public static void Watch(int machineUniqueId)
        {
            if (instance == null)
            {
                var go = new GameObject("ModePuzzle.PanelWatcher");
                instance = go.AddComponent<PanelWatcher>();
            }
            instance.machineId = machineUniqueId;
            instance.signature = LiveReader.Signature(machineUniqueId);
            instance.next = Time.unscaledTime + IntervalSeconds;
        }

        public static void Stop()
        {
            if (instance != null) Destroy(instance.gameObject);
            instance = null;
        }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + IntervalSeconds;
            if (!OptimizerPanel.IsOpen) { Stop(); return; }
            string now;
            try { now = LiveReader.Signature(machineId); }
            catch (System.Exception) { return; }
            if (now == signature) return;
            signature = now;
            OptimizerPanel.Refresh();
        }
    }
}
