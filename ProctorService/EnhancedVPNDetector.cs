using System;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace ProctorService
{
    public class EnhancedVPNDetector
    {
        private readonly ILogger _logger;
        private Timer? _detectTimer;

        public EnhancedVPNDetector(ILogger logger)
        {
            _logger = logger;
        }

        public void Start()
        {
            _logger.LogInformation("Enhanced VPN detector started");
            _detectTimer = new Timer(DetectVPN, null, 0, 2000);
        }

        private void DetectVPN(object? state)
        {
            try
            {
                CheckNetworkAdapters();

                CheckVPNServices();

                CheckVPNProcesses();

                CheckRegistryForVPN();

                CheckRoutingTable();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VPN detection error");
            }
        }

        private void CheckNetworkAdapters()
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                string desc = ni.Description.ToLower();
                string name = ni.Name.ToLower();

                string[] vpnKeywords = {
                    "tap", "tun", "vpn", "virtual", "wireguard",
                    "openvpn", "nordvpn", "expressvpn", "protonvpn",
                    "tunnelbear", "surfshark", "cyberghost", "privateinternetaccess",
                    "pia", "ivpn", "mullvad", "windscribe"
                };

                foreach (var keyword in vpnKeywords)
                {
                    if (desc.Contains(keyword) || name.Contains(keyword))
                    {
                        _logger.LogWarning($"VPN adapter detected: {ni.Name} ({ni.Description})");

                        DisableNetworkAdapter(ni.Id);
                    }
                }
            }
        }

        private void CheckVPNServices()
        {
            string[] vpnServices = {
                "OpenVPNService", "OpenVPNServiceInteractive",
                "WireGuardTunnel", "WireGuard",
                "NordVPNService", "nordvpn-service",
                "ExpressVPNService",
                "ProtonVPNService", "ProtonVPN Service",
                "SurfsharkService",
                "CyberGhostVPNService",
                "TunnelBearMaintenance",
                "WindscribeService",
                "Mullvad VPN"
            };

            foreach (var serviceName in vpnServices)
            {
                try
                {
                    var service = new ServiceController(serviceName);

                    if (service.Status == ServiceControllerStatus.Running)
                    {
                        _logger.LogWarning($"VPN service detected: {serviceName}");

                        try
                        {
                            service.Stop();
                            _logger.LogInformation($"Stopped VPN service: {serviceName}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to stop service: {serviceName}");
                        }
                    }
                }
                catch
                {
                    // Service doesn't exist
                }
            }
        }

        private void CheckVPNProcesses()
        {
            string[] vpnProcesses = {
                "openvpn", "openvpn-gui", "wireguard",
                "nordvpn", "expressvpn", "protonvpn",
                "surfshark", "cyberghost", "tunnelbear",
                "windscribe", "mullvad", "pia-client",
                "ivpn-ui"
            };

            var processes = Process.GetProcesses();

            foreach (var process in processes)
            {
                try
                {
                    string procName = process.ProcessName.ToLower();

                    if (vpnProcesses.Any(vpn => procName.Contains(vpn)))
                    {
                        _logger.LogWarning($"VPN process detected: {process.ProcessName}");

                        try
                        {
                            process.Kill();
                            _logger.LogInformation($"Killed VPN process: {process.ProcessName}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to kill process: {process.ProcessName}");
                        }
                    }
                }
                catch { }
            }
        }

        private void CheckRegistryForVPN()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Adapters");

                if (key != null)
                {
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        if (subKeyName.ToLower().Contains("tap") || subKeyName.ToLower().Contains("tun"))
                        {
                            _logger.LogWarning($"VPN registry key detected: {subKeyName}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Registry check failed");
            }
        }

        private void CheckRoutingTable()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "route.exe",
                    Arguments = "print",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                string output = process?.StandardOutput.ReadToEnd() ?? "";

                if (output.Contains("0.0.0.0") &&
                    (output.ToLower().Contains("tun") || output.ToLower().Contains("tap")))
                {
                    _logger.LogWarning("Suspicious routing table detected (possible VPN)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Routing table check failed");
            }
        }

        private void DisableNetworkAdapter(string adapterId)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = $"interface set interface \"{adapterId}\" disable",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(startInfo)?.WaitForExit();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to disable adapter: {adapterId}");
            }
        }

        public void Stop()
        {
            _detectTimer?.Dispose();
            _logger.LogInformation("Enhanced VPN detector stopped");
        }
    }
}