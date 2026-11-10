using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Timers;
using NLog;

namespace VRCX
{
    /// <summary>
    ///     Polls for VRChat and SteamVR once a second and raises events when either starts or exits.
    /// </summary>
    internal class ProcessMonitor
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly Dictionary<string, MonitoredProcess> monitoredProcesses = new(StringComparer.OrdinalIgnoreCase);
        private readonly Timer monitorProcessTimer = new() { Interval = 1000 };

        static ProcessMonitor()
        {
            Instance = new ProcessMonitor();
            Instance.ProcessStarted += Program.AppApiInstance.OnProcessStateChanged;
            Instance.ProcessExited += Program.AppApiInstance.OnProcessStateChanged;
        }

        public ProcessMonitor()
        {
            monitorProcessTimer.Elapsed += MonitorProcessTimer_Elapsed;
        }

        public static ProcessMonitor Instance { get; private set; }

        /// <summary>
        ///     Raised when a monitored process is started. MonitoredProcess.Process is the started process.
        /// </summary>
        public event Action<MonitoredProcess> ProcessStarted;

        /// <summary>
        ///     Raised when a monitored process is exited. MonitoredProcess.Process is null by then.
        /// </summary>
        public event Action<MonitoredProcess> ProcessExited;

        public void Init()
        {
            foreach (var name in new[] { "vrchat", "vrserver" })
                monitoredProcesses[name] = new MonitoredProcess(name);
            monitorProcessTimer.Start();
        }

        public void Exit()
        {
            monitorProcessTimer.Stop();
            foreach (var monitoredProcess in monitoredProcesses.Values)
                monitoredProcess.ProcessExited();
        }

        private void MonitorProcessTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // Only enumerate processes when something is not running; a running process is tracked by PID.
            Process[] processes = null;

            foreach (var monitoredProcess in monitoredProcesses.Values)
            {
                if (monitoredProcess.IsRunning)
                {
                    // WinApi.HasProcessExited also works for elevated processes, unlike Process.HasExited.
                    var pid = monitoredProcess.Process.Id;
                    if (!WinApi.HasProcessExited(pid))
                        continue;

                    monitoredProcess.ProcessExited();
                    ProcessExited?.Invoke(monitoredProcess);
                    logger.Info($"Monitored process {monitoredProcess.ProcessName} (PID: {pid}) exited.");
                    continue;
                }

                processes ??= Process.GetProcesses();
                // Skip processes that are listed but already exiting, so we don't flap between started and exited.
                var process = Array.Find(processes, p =>
                    monitoredProcess.HasName(p.ProcessName) && !WinApi.HasProcessExited(p.Id));
                if (process == null)
                    continue;

                monitoredProcess.ProcessStarted(process);
                ProcessStarted?.Invoke(monitoredProcess);
                logger.Info($"Monitored process {monitoredProcess.ProcessName} (PID: {process.Id}) started.");
            }

            // Dispose every enumerated Process except the ones now held by a MonitoredProcess.
            if (processes != null)
                foreach (var process in processes)
                    if (!monitoredProcesses.Values.Any(m => m.Process == process))
                        process.Dispose();
        }

        /// <summary>
        ///     Checks if a process is currently being monitored and if it is running.
        /// </summary>
        /// <param name="processName">The name of the process to check for.</param>
        /// <returns>Whether the given process is monitored and currently running.</returns>
        public bool IsProcessRunning(string processName)
        {
            return monitoredProcesses.TryGetValue(processName, out var process) && process.IsRunning;
        }
    }

    public class MonitoredProcess
    {
        public MonitoredProcess(string processName)
        {
            ProcessName = processName;
        }

        public Process Process { get; private set; }
        public string ProcessName { get; }
        public bool IsRunning => Process != null;

        public bool HasName(string processName)
        {
            return ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase);
        }

        public void ProcessExited()
        {
            Process?.Dispose();
            Process = null;
        }

        public void ProcessStarted(Process process)
        {
            Process = process;
        }
    }
}
