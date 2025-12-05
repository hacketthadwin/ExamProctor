using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace ProctorService
{
    public class FirewallManager
    {
        private readonly ILogger _logger;
        private readonly List<string> _createdRules = new();
        private HashSet<string> _currentAllowedIPs = new();

        public FirewallManager(ILogger logger)
        {
            _logger = logger;
        }

        public void EnableLockdown()
        {
            _logger.LogInformation("Enabling IP-Based Firewall Lockdown");

            try
            {
                DisableLockdown();
                _logger.LogInformation("Setting Windows Firewall to BLOCK ALL outbound traffic...");
                ExecuteNetsh("advfirewall set allprofiles firewallpolicy blockinbound,blockoutbound");
                _logger.LogInformation("Default policy changed to BLOCK");

                CreateFirewallRule(
                    "Proctor_AllowLoopback",
                    "Allow loopback traffic",
                    "out",
                    "allow",
                    protocol: "any",
                    remoteAddress: "127.0.0.1"
                );

                CreateFirewallRule(
                    "Proctor_AllowDNS_UDP",
                    "Allow DNS queries UDP",
                    "out",
                    "allow",
                    protocol: "UDP",
                    remotePort: "53"
                );

                CreateFirewallRule(
                    "Proctor_AllowDNS_TCP",
                    "Allow DNS queries TCP",
                    "out",
                    "allow",
                    protocol: "TCP",
                    remotePort: "53"
                );

                AllowWindowsEssentials();

                CreateFirewallRule(
                    "Proctor_BlockHTTP",
                    "Block all HTTP traffic (exceptions will be added)",
                    "out",
                    "block",
                    protocol: "TCP",
                    remotePort: "80"
                );

                CreateFirewallRule(
                    "Proctor_BlockHTTPS",
                    "Block all HTTPS traffic (exceptions will be added)",
                    "out",
                    "block",
                    protocol: "TCP",
                    remotePort: "443"
                );

                _logger.LogInformation("Firewall lockdown enabled");
                _logger.LogInformation("Waiting for IP resolver to whitelist Codeforces IPs...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enable lockdown");
                DisableLockdown();
                throw;
            }
        }

        public void UpdateAllowedIPs(HashSet<string> allowedIPs)
        {
            if (allowedIPs == null || allowedIPs.Count == 0)
            {
                _logger.LogWarning("No IPs provided to UpdateAllowedIPs");
                return;
            }

            _logger.LogInformation($"Updating Codeforces IP whitelist ({allowedIPs.Count} IPs)");

            try
            {
                var oldRules = _createdRules.Where(r => r.StartsWith("Proctor_CF_")).ToList();
                foreach (var ruleName in oldRules)
                {
                    ExecuteNetsh($"advfirewall firewall delete rule name=\"{ruleName}\"", ignoreErrors: true);
                    _createdRules.Remove(ruleName);
                }

                int ruleCount = 0;
                foreach (var ip in allowedIPs)
                {
                    string safeIP = ip.Replace(".", "_");

                    CreateFirewallRule(
                        $"Proctor_CF_{safeIP}_HTTP",
                        $"Allow Codeforces IP {ip} on port 80",
                        "out",
                        "allow",
                        protocol: "TCP",
                        remotePort: "80",
                        remoteAddress: ip
                    );

                    CreateFirewallRule(
                        $"Proctor_CF_{safeIP}_HTTPS",
                        $"Allow Codeforces IP {ip} on port 443",
                        "out",
                        "allow",
                        protocol: "TCP",
                        remotePort: "443",
                        remoteAddress: ip
                    );

                    ruleCount += 2;
                }

                _currentAllowedIPs = allowedIPs;
                _logger.LogInformation($"Created {ruleCount} firewall rules for {allowedIPs.Count} Codeforces IPs");
                _logger.LogInformation($"Allowed IPs: {string.Join(", ", allowedIPs.Take(5))}{(allowedIPs.Count > 5 ? "..." : "")}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update IP rules");
            }
        }

        private void AllowWindowsEssentials()
        {
            _logger.LogInformation("Allowing minimal Windows services");

            CreateFirewallRule(
                "Proctor_AllowSvchost",
                "Allow Windows system services (svchost.exe)",
                "out",
                "allow",
                protocol: "any",
                programPath: @"C:\Windows\System32\svchost.exe"
            );

            string[] ncsiIPs = { "13.107.4.52", "131.107.255.255" };
            foreach (var ip in ncsiIPs)
            {
                CreateFirewallRule(
                    $"Proctor_AllowNCSI_{ip.Replace(".", "_")}",
                    $"Allow NCSI {ip}",
                    "out",
                    "allow",
                    protocol: "any",
                    remoteAddress: ip
                );
            }

            _logger.LogInformation("Windows essentials allowed");
        }

        public void DisableLockdown()
        {
            _logger.LogInformation("Disabling firewall lockdown");

            try
            {
                ExecuteNetsh("advfirewall firewall delete rule name=\"Proctor_*\"", ignoreErrors: true);

                _logger.LogInformation("Restoring Windows Firewall to default policy...");
                ExecuteNetsh("advfirewall set allprofiles firewallpolicy blockinbound,allowoutbound");
                _logger.LogInformation("Default policy restored to ALLOW outbound");

                _createdRules.Clear();
                _currentAllowedIPs.Clear();

                _logger.LogInformation("Firewall lockdown disabled");

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to disable lockdown");
            }
        }

        public bool IsLockdownActive()
        {
            try
            {
                string output = ExecuteNetshOutput("advfirewall firewall show rule name=\"Proctor_BlockHTTPS\"");
                return output.Contains("Proctor_BlockHTTPS");
            }
            catch
            {
                return false;
            }
        }

        private void CreateFirewallRule(
            string name,
            string description,
            string direction,
            string action,
            string protocol = "any",
            string? localPort = null,
            string? remotePort = null,
            string? remoteAddress = null,
            string? programPath = null)
        {
            if (_createdRules.Contains(name))
            {
                return;
            }

            string args = $"advfirewall firewall add rule " +
                          $"name=\"{name}\" " +
                          $"description=\"{description}\" " +
                          $"dir={direction} " +
                          $"action={action} " +
                          $"protocol={protocol} " +
                          $"enable=yes";

            if (localPort != null) args += $" localport={localPort}";
            if (remotePort != null) args += $" remoteport={remotePort}";
            if (remoteAddress != null) args += $" remoteip={remoteAddress}";
            if (programPath != null) args += $" program=\"{programPath}\"";

            try
            {
                ExecuteNetsh(args);
                _createdRules.Add(name);
                _logger.LogDebug($"Created: {name}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to create rule: {name}");
                throw;
            }
        }

        private void ExecuteNetsh(string arguments, bool ignoreErrors = false)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    process?.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                if (!ignoreErrors)
                {
                    throw;
                }
                _logger.LogWarning($"(Ignored) Netsh error: {ex.Message}");
            }
        }

        private string ExecuteNetshOutput(string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using (var process = Process.Start(startInfo))
                {
                    string output = process?.StandardOutput.ReadToEnd() ?? "";
                    process?.WaitForExit();
                    return output;
                }
            }
            catch
            {
                return "";
            }
        }
    }
}