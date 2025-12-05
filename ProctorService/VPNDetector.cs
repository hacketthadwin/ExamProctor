using System;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace ProctorService
{
    public class VPNDetector
    {
        private readonly ILogger _logger;
        private Timer? _detectTimer;

        public VPNDetector(ILogger logger)
        {
            _logger = logger;
        }

        public void Start()
        {
            _logger.LogInformation("VPN detector started");
            _detectTimer = new Timer(DetectVPN, null, 0, 2000);
        }

        private void DetectVPN(object? state)
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    string description = ni.Description.ToLower();

                    if (description.Contains("tap") ||
                        description.Contains("tun") ||
                        description.Contains("vpn"))
                    {
                        _logger.LogWarning("Detected VPN adapter: {name}", ni.Description);
                    }
                }

                string[] vpnServices = {
                    "OpenVPNService", "WireGuardTunnel",
                    "NordVPNService", "ExpressVPNService"
                };

                foreach (var serviceName in vpnServices)
                {
                    try
                    {
                        var service = new ServiceController(serviceName);
                        if (service.Status == ServiceControllerStatus.Running)
                        {
                            _logger.LogWarning("Detected running VPN service: {service}", serviceName);
                        }
                    }
                    catch
                    {
                        // Service doesn't exist
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VPN detection error");
            }
        }

        public void Stop()
        {
            _detectTimer?.Dispose();
            _logger.LogInformation("VPN detector stopped");
        }
    }
}