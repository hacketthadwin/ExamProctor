using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace ProctorService
{
    public class NetworkConfigurator
    {
        private readonly ILogger _logger;
        private string? _originalDNS;
        private string? _adapterName;

        public NetworkConfigurator(ILogger logger)
        {
            _logger = logger;
        }

        public bool SetCustomDNS()
        {
            _logger.LogInformation("Configuring system to use custom DNS (127.0.0.1)");

            try
            {
                var adapters = GetActiveNetworkAdapters();
                if (adapters.Count == 0)
                {
                    _logger.LogError("No active network adapters found");
                    return false;
                }

                _logger.LogInformation($"Found {adapters.Count} active adapter(s)");

                bool anySuccess = false;

                foreach (var adapter in adapters)
                {
                    _logger.LogInformation($"Configuring adapter: {adapter}");

                    if (_adapterName == null)
                    {
                        _adapterName = adapter;
                        _originalDNS = GetCurrentDNS(adapter);
                        _logger.LogInformation($"Original DNS backed up: {_originalDNS ?? "DHCP"}");
                    }

                    bool success = SetDNSServers(adapter, new[] { "127.0.0.1" });

                    if (success)
                    {
                        _logger.LogInformation($"DNS configured on: {adapter}");
                        anySuccess = true;
                    }
                    else
                    {
                        _logger.LogWarning($"Failed to configure DNS on: {adapter}");
                    }
                }

                if (anySuccess)
                {
                    FlushDNSCache();
                    _logger.LogInformation("System DNS configured to use 127.0.0.1");
                }

                return anySuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure DNS");
                return false;
            }
        }

        public bool RestoreOriginalDNS()
        {
            _logger.LogInformation("Restoring original DNS settings");

            try
            {
                var adapters = GetActiveNetworkAdapters();
                bool anySuccess = false;

                foreach (var adapter in adapters)
                {
                    _logger.LogInformation($"Restoring DNS on: {adapter}");

                    bool success;

                    if (adapter == _adapterName && !string.IsNullOrEmpty(_originalDNS))
                    {
                        var dnsServers = _originalDNS.Split(',');
                        success = SetDNSServers(adapter, dnsServers);
                    }
                    else
                    {
                        success = SetDNSToDHCP(adapter);
                    }

                    if (success)
                    {
                        _logger.LogInformation($"DNS restored on: {adapter}");
                        anySuccess = true;
                    }
                }

                if (anySuccess)
                {
                    FlushDNSCache();
                    _logger.LogInformation("Original DNS settings restored");
                }

                return anySuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore DNS");
                return false;
            }
        }

        private System.Collections.Generic.List<string> GetActiveNetworkAdapters()
        {
            var adapters = new System.Collections.Generic.List<string>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True");

                foreach (ManagementObject obj in searcher.Get())
                {
                    var description = obj["Description"]?.ToString();
                    var ipAddresses = obj["IPAddress"] as string[];

                    if (description != null && ipAddresses != null && ipAddresses.Length > 0)
                    {
                        adapters.Add(description);
                        _logger.LogDebug($"Found adapter: {description} with IP: {ipAddresses[0]}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active adapters");
            }

            return adapters;
        }

        private string? GetCurrentDNS(string adapterName)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE Description = '{adapterName}' AND IPEnabled = True");

                foreach (ManagementObject obj in searcher.Get())
                {
                    var dnsServers = obj["DNSServerSearchOrder"] as string[];
                    if (dnsServers != null && dnsServers.Length > 0)
                    {
                        return string.Join(",", dnsServers);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current DNS");
            }

            return null;
        }

        private bool SetDNSServers(string adapterName, string[] dnsServers)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE Description = '{adapterName}' AND IPEnabled = True");

                foreach (ManagementObject obj in searcher.Get())
                {
                    var result = obj.InvokeMethod("SetDNSServerSearchOrder", new object[] { dnsServers });
                    var returnValue = Convert.ToUInt32(result);

                    if (returnValue == 0)
                    {
                        return true;
                    }
                    else
                    {
                        _logger.LogError($"SetDNSServerSearchOrder returned error code: {returnValue}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting DNS servers");
            }

            return false;
        }

        private bool SetDNSToDHCP(string adapterName)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE Description = '{adapterName}' AND IPEnabled = True");

                foreach (ManagementObject obj in searcher.Get())
                {
                    var result = obj.InvokeMethod("SetDNSServerSearchOrder", new object[] { null });
                    var returnValue = Convert.ToUInt32(result);

                    return returnValue == 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting DNS to DHCP");
            }

            return false;
        }

        private void FlushDNSCache()
        {
            try
            {
                _logger.LogInformation("Flushing DNS cache");

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "ipconfig",
                    Arguments = "/flushdns",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                });

                process?.WaitForExit(5000);

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = "-Command \"Clear-DnsClientCache\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    })?.WaitForExit(5000);
                }
                catch { }

                Thread.Sleep(1000);

                _logger.LogInformation("DNS cache flushed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush DNS cache");
            }
        }
    }
}