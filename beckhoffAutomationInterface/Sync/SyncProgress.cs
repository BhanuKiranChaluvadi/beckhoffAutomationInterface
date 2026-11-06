using System;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Progress of a long PLC object sync, as plain lines (log files and CI, so no '\r' bar):
    ///   2026-11-06 14:49:12:   sync  30%  methods 410/980  1:15 used, about 2:55 left
    /// A line comes at each further 10 %, or after 30 s without a line. The time left is
    /// linear from the rate so far, so it is an estimate.
    /// </summary>
    class SyncProgress
    {
        static readonly TimeSpan MaxQuiet = TimeSpan.FromSeconds(30);

        readonly int _total;
        readonly Func<DateTime> _now;
        readonly Action<string> _write;
        readonly DateTime _start;
        DateTime _lastLine;
        int _lastTenth;
        int _done;

        public SyncProgress(int total, Action<string> write = null, Func<DateTime> now = null)
        {
            _total = Math.Max(total, 0);
            _now = now ?? (() => DateTime.Now);
            _write = write ?? Console.WriteLine;
            _start = _lastLine = _now();
        }

        /// <summary>One unit of work is done. step names the phase; phaseDone/phaseTotal count it.</summary>
        public void Step(string step, int phaseDone, int phaseTotal)
        {
            _done = Math.Min(_done + 1, _total);
            if (_total == 0)
                return;
            DateTime now = _now();
            int tenth = _done * 10 / _total;
            if (tenth <= _lastTenth && now - _lastLine < MaxQuiet)
                return;
            _lastTenth = Math.Max(_lastTenth, tenth);
            _lastLine = now;
            _write(Line(_done, _total, step, phaseDone, phaseTotal, now - _start, now));
        }

        internal static string Line(int done, int total, string step, int phaseDone, int phaseTotal, TimeSpan used, DateTime now)
        {
            int percent = total == 0 ? 100 : done * 100 / total;
            string left = done == 0 || done >= total
                ? ""
                : ", about " + Duration(TimeSpan.FromTicks(used.Ticks * (total - done) / done)) + " left";
            return string.Format("{0}:   sync {1,3}%  {2} {3}/{4}  {5} used{6}",
                now.ToString("yyyy-MM-dd HH:mm:ss"), percent, step, phaseDone, phaseTotal, Duration(used), left);
        }

        internal static string Duration(TimeSpan t) =>
            t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                              : string.Format("{0}:{1:00}", (int)t.TotalMinutes, t.Seconds);
    }
}
