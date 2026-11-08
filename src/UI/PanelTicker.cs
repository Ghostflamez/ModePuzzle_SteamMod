using System;
using UnityEngine;

namespace ModePuzzle.UI
{
    /// <summary>
    /// Drives the panel's per-frame work. Lives in the main assembly because Unity cannot add a MonoBehaviour
    /// from an assembly loaded again under the same name (the hot-reloaded panel in development builds).
    /// Destroyed with the scene; then <c>gone</c> runs once.
    /// </summary>
    sealed class PanelTicker : MonoBehaviour
    {
        static PanelTicker instance;
        Action tick, gone;

        public static void Run(Action tick, Action gone)
        {
            if (instance == null) instance = new GameObject("ModePuzzle.PanelTicker").AddComponent<PanelTicker>();
            instance.tick = tick; instance.gone = gone;
        }

        public static void Stop()
        {
            if (instance == null) return;
            instance.tick = instance.gone = null;
            Destroy(instance.gameObject);
            instance = null;
        }

        void Update() => tick?.Invoke();

        void OnDestroy()
        {
            if (instance != this) return;
            instance = null;
            gone?.Invoke();
        }
    }
}
