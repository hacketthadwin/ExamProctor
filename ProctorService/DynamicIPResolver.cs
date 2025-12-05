using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace ProctorService
{
    public class DynamicIPResolver
    {
        private readonly ILogger _logger;
        private Timer? _refreshTimer;
        private HashSet<string> _allowedIPs = new();
        private readonly FirewallManager _firewallManager;

        public DynamicIPResolver(ILogger logger, FirewallManager firewallManager)
        {
            _logger = logger;
            _firewallManager = firewallManager;
        }

        public void Start()
        {
            _logger.LogInformation("Starting dynamic IP resolver");
            _logger.LogInformation("Refresh interval: 3 minutes");

            RefreshIPs(null);

            _refreshTimer = new Timer(RefreshIPs, null, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3));

            _logger.LogInformation("Dynamic IP resolver started");
        }

        private void RefreshIPs(object? state)
        {
            try
            {
                _logger.LogInformation("Resolving Codeforces IP addresses...");

                var newIPs = new HashSet<string>();

                string[] domains = {
                    "codeforces.com",
                    "www.codeforces.com",
                    "m1.codeforces.com",
                    "m2.codeforces.com",
                    "m3.codeforces.com",
                    "static.codeforces.com",
                    "cdn.codeforces.com",
                    "codeforces.org",
                    "www.codeforces.org",
                    "static.codeforces.org",
                    "cloudflare.com",
                    "www.cloudflare.com",
                    "cdnjs.cloudflare.com",
                    "challenges.cloudflare.com",
                    "ajax.cloudflare.com"
                };

                foreach (var domain in domains)
                {
                    var ips = ResolveHostname(domain);
                    newIPs.UnionWith(ips);
                }

                if (newIPs.Count == 0)
                {
                    _logger.LogError("Failed to resolve ANY Codeforces IPs!");
                    _logger.LogWarning("Keeping existing firewall rules (if any)");
                    return;
                }

                if (_allowedIPs.Count > 0 && _allowedIPs.SetEquals(newIPs))
                {
                    _logger.LogInformation($"IP addresses unchanged ({newIPs.Count} IPs)");
                }
                else
                {
                    _logger.LogInformation($"IP addresses changed!");
                    _logger.LogInformation($"Old count: {_allowedIPs.Count}");
                    _logger.LogInformation($"New count: {newIPs.Count}");

                    _allowedIPs = newIPs;

                    _firewallManager.UpdateAllowedIPs(_allowedIPs);

                    _logger.LogInformation("Firewall rules updated with new IPs");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing IPs");
            }
        }

        private HashSet<string> ResolveHostname(string hostname)
        {
            var ips = new HashSet<string>();

            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(hostname);

                foreach (var addr in addresses)
                {
                    if (addr.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ip = addr.ToString();
                        ips.Add(ip);
                        _logger.LogInformation($"{hostname} -> {ip}");
                    }
                }

                if (ips.Count == 0)
                {
                    _logger.LogWarning($"No IPv4 addresses found for {hostname}");
                }
            }
            catch (SocketException sex)
            {
                _logger.LogWarning($"DNS resolution failed for {hostname}: {sex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resolving {hostname}");
            }

            return ips;
        }

        public void Stop()
        {
            _logger.LogInformation("Stopping dynamic IP resolver");

            _refreshTimer?.Dispose();
            _refreshTimer = null;

            _logger.LogInformation("Dynamic IP resolver stopped");
        }
    }
}