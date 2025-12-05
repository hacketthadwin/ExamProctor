using System.Management;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace ProctorService
{
    public class VMDetector
    {
        private readonly ILogger _logger;
        private bool _isVM = false;

        public VMDetector(ILogger logger)
        {
            _logger = logger;
        }

        public bool DetectVM()
        {
            _logger.LogInformation("Running VM detection checks...");

            bool vmDetected =
                CheckBIOS() ||
                CheckManufacturer() ||
                CheckHypervisor() ||
                CheckProcesses() ||
                CheckRegistry() ||
                CheckMAC() ||
                CheckCPUID();

            _isVM = vmDetected;

            if (_isVM)
            {
                _logger.LogWarning("VIRTUAL MACHINE DETECTED");
            }
            else
            {
                _logger.LogInformation("Running on physical hardware");
            }

            return _isVM;
        }

        private bool CheckBIOS()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");

                foreach (ManagementObject obj in searcher.Get())
                {
                    string manufacturer = obj["Manufacturer"]?.ToString()?.ToLower() ?? "";
                    string version = obj["Version"]?.ToString()?.ToLower() ?? "";

                    string[] vmIndicators = { "virtualbox", "vmware", "xen", "qemu", "kvm", "hyperv", "parallels", "virtual" };

                    foreach (var indicator in vmIndicators)
                    {
                        if (manufacturer.Contains(indicator) || version.Contains(indicator))
                        {
                            _logger.LogWarning($"VM detected via BIOS: {manufacturer} {version}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BIOS check failed");
            }

            return false;
        }

        private bool CheckManufacturer()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");

                foreach (ManagementObject obj in searcher.Get())
                {
                    string manufacturer = obj["Manufacturer"]?.ToString()?.ToLower() ?? "";
                    string model = obj["Model"]?.ToString()?.ToLower() ?? "";

                    string[] vmIndicators = {
                        "vmware", "virtualbox", "vbox", "xen", "qemu",
                        "kvm", "microsoft corporation", "parallels", "virtual"
                    };

                    foreach (var indicator in vmIndicators)
                    {
                        if (manufacturer.Contains(indicator) || model.Contains(indicator))
                        {
                            _logger.LogWarning($"VM detected via Manufacturer: {manufacturer} {model}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manufacturer check failed");
            }

            return false;
        }

        private bool CheckHypervisor()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "systeminfo.exe",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                string output = process?.StandardOutput.ReadToEnd() ?? "";

                if (output.Contains("Hyper-V") || output.Contains("hypervisor"))
                {
                    _logger.LogWarning("VM detected via Hypervisor check");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hypervisor check failed");
            }

            return false;
        }

        private bool CheckProcesses()
        {
            string[] vmProcesses = {
                "vmware", "vmtoolsd", "vboxservice", "vboxtray",
                "xenservice", "qemu-ga", "vmsrvc", "vmusrvc"
            };

            var processes = Process.GetProcesses();

            foreach (var process in processes)
            {
                try
                {
                    string procName = process.ProcessName.ToLower();

                    if (vmProcesses.Any(vm => procName.Contains(vm)))
                    {
                        _logger.LogWarning($"VM detected via process: {process.ProcessName}");
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private bool CheckRegistry()
        {
            try
            {
                var vmwareKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\VMware, Inc.\VMware Tools");
                if (vmwareKey != null)
                {
                    _logger.LogWarning("VM detected via Registry: VMware");
                    return true;
                }

                var vboxKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Oracle\VirtualBox Guest Additions");
                if (vboxKey != null)
                {
                    _logger.LogWarning("VM detected via Registry: VirtualBox");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Registry check failed");
            }

            return false;
        }

        private bool CheckMAC()
        {
            try
            {
                string[] vmMacPrefixes = {
                    "00:05:69", "00:0C:29", "00:1C:14", "00:50:56",
                    "08:00:27",
                    "00:16:3E",
                    "00:1C:42"
                };

                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True");

                foreach (ManagementObject obj in searcher.Get())
                {
                    string mac = obj["MACAddress"]?.ToString()?.ToUpper() ?? "";

                    foreach (var prefix in vmMacPrefixes)
                    {
                        if (mac.StartsWith(prefix.ToUpper()))
                        {
                            _logger.LogWarning($"VM detected via MAC address: {mac}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MAC address check failed");
            }

            return false;
        }

        private bool CheckCPUID()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");

                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = obj["Name"]?.ToString()?.ToLower() ?? "";

                    if (name.Contains("virtual") || name.Contains("qemu") || name.Contains("kvm"))
                    {
                        _logger.LogWarning($"VM detected via CPU: {name}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CPU check failed");
            }

            return false;
        }

        public bool IsVirtualMachine() => _isVM;
    }
}