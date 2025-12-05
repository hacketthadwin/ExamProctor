using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ProctorService
{
    public class ProctorWorker : BackgroundService
    {
        private readonly ILogger<ProctorWorker> _logger;
        private IPCServer? _ipcServer;
        private FirewallManager? _firewallManager;
        private ProcessWatchdog? _processWatchdog;
        private EnhancedVPNDetector? _vpnDetector;
        private VMDetector? _vmDetector;
        private DynamicIPResolver? _ipResolver;

        public ProctorWorker(ILogger<ProctorWorker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("=======================================");
            _logger.LogInformation("    ProctorService v4.0 Final");
            _logger.LogInformation("    IP-Based Codeforces Filtering");
            _logger.LogInformation("=======================================");
            _logger.LogInformation("Starting at: {time}", DateTimeOffset.Now);

            try
            {
                _ipcServer = new IPCServer(_logger);
                _ipcServer.OnCommandReceived += HandleCommand;
                _ipcServer.Start();
                _logger.LogInformation("IPC Server started and listening");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FATAL: Failed to start IPC Server");
                throw;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("Initializing system components...");

                    _vmDetector = new VMDetector(_logger);
                    bool isVM = _vmDetector.DetectVM();
                    if (isVM)
                    {
                        _logger.LogWarning("RUNNING IN VIRTUAL MACHINE");
                        _logger.LogWarning("Exam integrity may be compromised!");
                    }

                    _firewallManager = new FirewallManager(_logger);
                    _processWatchdog = new ProcessWatchdog(_logger);
                    _vpnDetector = new EnhancedVPNDetector(_logger);

                    _ipResolver = new DynamicIPResolver(_logger, _firewallManager);

                    _logger.LogInformation("All system components initialized");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error initializing components");
                }
            }, stoppingToken);

            _logger.LogInformation("ProctorService is ready and listening");
            _logger.LogInformation("Waiting for commands...\n");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }

        private void HandleCommand(string command)
        {
            _logger.LogInformation($"Received command: {command}");

            switch (command?.ToUpper())
            {
                case "ENTER":
                    _logger.LogInformation("ENTERING EXAM MODE");
                    try
                    {
                        _firewallManager?.EnableLockdown();
                        _logger.LogInformation("Firewall enabled");

                        _ipResolver?.Start();
                        _logger.LogInformation("IP resolver started");

                        _processWatchdog?.Start();
                        _logger.LogInformation("Process watchdog started");

                        _vpnDetector?.Start();
                        _logger.LogInformation("VPN detection started");

                        _logger.LogInformation("");
                        _logger.LogInformation("EXAM MODE ACTIVE");
                        _logger.LogInformation("Only Codeforces.com accessible");
                        _logger.LogInformation("Firewall: Active");
                        _logger.LogInformation("Dynamic IP Updates: Every 3 min");
                        _logger.LogInformation("Process Watchdog: Monitoring");
                        _logger.LogInformation("VPN Detection: Active");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to enable exam mode");
                        HandleCommand("EXIT");
                    }
                    break;

                case "EXIT":
                    _logger.LogInformation("EXITING EXAM MODE");
                    try
                    {
                        _vpnDetector?.Stop();
                        _logger.LogInformation("VPN detection stopped");

                        _processWatchdog?.Stop();
                        _logger.LogInformation("Process watchdog stopped");

                        _ipResolver?.Stop();
                        _logger.LogInformation("IP resolver stopped");

                        _firewallManager?.DisableLockdown();
                        _logger.LogInformation("Firewall disabled");

                        _logger.LogInformation("");
                        _logger.LogInformation("EXAM MODE DISABLED - System restored");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during exit");
                    }
                    break;

                case "STATUS":
                    _logger.LogInformation("System Status Check");
                    _logger.LogInformation("-------------------------------------");

                    bool vmStatus = _vmDetector?.IsVirtualMachine() ?? false;
                    bool firewallActive = _firewallManager?.IsLockdownActive() ?? false;

                    _logger.LogInformation($"VM Status: {(vmStatus ? "Virtual Machine" : "Physical Hardware")}");
                    _logger.LogInformation($"Firewall: {(firewallActive ? "Active (Lockdown)" : "Inactive")}");
                    _logger.LogInformation($"IP Resolver: {(_ipResolver != null ? "Initialized" : "Not initialized")}");
                    _logger.LogInformation($"Process Watchdog: {(_processWatchdog != null ? "Ready" : "Not ready")}");
                    _logger.LogInformation($"VPN Detector: {(_vpnDetector != null ? "Ready" : "Not ready")}");

                    break;

                case "REFRESH":
                    _logger.LogInformation("Manual IP refresh requested");
                    _logger.LogInformation("Next automatic refresh in ~3 minutes");
                    break;

                default:
                    _logger.LogWarning($"Unknown command: {command}");
                    _logger.LogInformation("Valid commands: ENTER, EXIT, STATUS, REFRESH");
                    break;
            }

        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("ProctorService stopping - cleanup in progress");

            try
            {
                _vpnDetector?.Stop();
                _processWatchdog?.Stop();
                _ipResolver?.Stop();
                _firewallManager?.DisableLockdown();
                _ipcServer?.Stop();

                _logger.LogInformation("All components stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during service stop");
            }

            _logger.LogInformation("ProctorService stopped successfully");
            return base.StopAsync(cancellationToken);
        }
    }
}