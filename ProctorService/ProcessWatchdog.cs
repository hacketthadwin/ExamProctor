using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace ProctorService
{
    public class ProcessWatchdog
    {
        private readonly ILogger _logger;
        private Timer? _watchTimer;
        private readonly HashSet<string> _whitelist;
        private readonly int _myProcessId;

        private const int ScanIntervalMs = 2000;

        public ProcessWatchdog(ILogger logger)
        {
            _logger = logger;
            _myProcessId = Process.GetCurrentProcess().Id;

            _whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System",
                "Idle",
                "csrss",
                "lsass",
                "winlogon",
                "services",
                "smss",
                "wininit",
                "svchost",
                "dwm",
                "fontdrvhost",
                "conhost",
                "sihost",

                "explorer",
                "SearchHost",
                "SearchApp",
                "SearchIndexer",
                "StartMenuExperienceHost",
                "ShellExperienceHost",
                "TextInputHost",
                "ctfmon",
                "taskhostw",
                "Taskmgr",

                "RuntimeBroker",
                "ApplicationFrameHost",
                "WmiPrvSE",
                "dllhost",
                "backgroundTaskHost",
                "backgroundTransferHost",

                "audiodg",

                "spoolsv",

                "MsMpEng",
                "SecurityHealthService",
                "SecurityHealthSystray",
                "SgrmBroker",
                "MpCmdRun",
                "NisSrv",

                "TiWorker",
                "TrustedInstaller",
                "wuauclt",
                "UsoClient",

                "dasHost",
                "RtkAudUService64",

                "nvcontainer",
                "nvdisplay.container",
                "NVIDIA Web Helper",
                "NVIDIA Share",

                "msedge",
                "msedgewebview2",
                "chrome",

                "UserOOBEBroker",
                "LockApp",
                "WinStore.App",
                "smartscreen",

                "TabTip",
                "TabTip32",

                "CredentialEnrollmentManager",

                "CompatTelRunner",
                "VSSVC",
                "msiexec",
                "consent",

                "lsaiso",
                "MemCompression",
                "Registry",
                "sppsvc",
                "wsappx",
                "SppExtComObj",

                "igfxEM",
                "igfxHK",
                "igfxTray",

                "RtkNGUI64",
                "RAVBg64",

                "dotnet",

                "WerFault",
                "wermgr",
                "ProctorAppGUI",
                "ProctorService",
                "ProctorLauncher",
                "ProctorApp",
            };
        }

        public void Start()
        {
            _logger.LogInformation("WHITELIST-ONLY Process Watchdog Started (Strict Mode)");
            _logger.LogWarning("Only whitelisted processes are allowed. All others will be terminated.");
            _watchTimer = new Timer(ScanProcesses, null, 0, ScanIntervalMs);
        }

        public void Stop()
        {
            _watchTimer?.Dispose();
            _watchTimer = null;
            _logger.LogInformation("Process watchdog stopped");
        }

        private void ScanProcesses(object? state)
        {
            try
            {
                var processes = Process.GetProcesses();

                foreach (var process in processes)
                {
                    try
                    {
                        if (process.Id == _myProcessId)
                            continue;

                        string name = process.ProcessName;

                        if (name.Equals("svchost", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (name.StartsWith("Proctor", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("Idle", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!_whitelist.Contains(name))
                        {
                            _logger.LogWarning("TERMINATING non-whitelisted process: {Name} (PID: {Pid}, Session: {Session})",
                                name, process.Id, process.SessionId);

                            try
                            {
                                process.Kill(entireProcessTree: true);
                                process.WaitForExit(1000);
                                _logger.LogInformation("Successfully terminated: {Name} (PID: {Pid})", name, process.Id);
                            }
                            catch (InvalidOperationException)
                            {
                                _logger.LogDebug("Process {Name} (PID: {Pid}) already exited", name, process.Id);
                            }
                            catch (System.ComponentModel.Win32Exception win32Ex)
                            {
                                _logger.LogDebug("Access denied killing {Name} (PID: {Pid}): {Message}",
                                    name, process.Id, win32Ex.Message);
                            }
                            catch (Exception killEx)
                            {
                                _logger.LogWarning(killEx, "Failed to kill process {Name} (PID: {Pid})", name, process.Id);
                            }
                        }
                    }
                    catch (Exception exInner)
                    {
                        _logger.LogDebug(exInner, "Error while checking process in watchdog");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in process watchdog scan");
            }
        }
    }
}