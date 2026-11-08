using System;
using System.Diagnostics;
using System.Threading;

namespace ModePuzzle.Engine
{
    /// <summary>Single worker, immutable publications. Contains no game or Unity references.</summary>
    public sealed class SearchSession
    {
        readonly object gate = new object();
        readonly SearchConfig config;
        readonly double limit;
        volatile bool cancel;
        volatile bool running;
        SearchResult latest;
        string error;
        public bool Running => running;
        public SearchSession(SearchConfig config, double limitMs) { this.config = config; limit = limitMs; }
        public void Start()
        {
            if (running) throw new InvalidOperationException("Already running");
            running = true;
            var thread = new Thread(Run) { IsBackground = true, Name = "ModePuzzle search" };
            try { thread.Start(); } catch { running = false; throw; }
        }
        public void Stop() { cancel = true; }
        public SearchResult Poll(out string failure) { lock (gate) { failure = error; return latest; } }
        void Publish(SearchResult r) { lock (gate) latest = r; }
        void Run()
        {
            try
            {
                var clock = Stopwatch.StartNew();
                var rng = new Random(0);
                Publish(new Search(config, limit, () => clock.Elapsed.TotalMilliseconds, rng.NextDouble, Publish, () => cancel).Solve());
            }
            catch (Exception e) { lock (gate) error = e.ToString(); }
            finally { running = false; }
        }
    }
}
