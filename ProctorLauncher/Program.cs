using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;

namespace ProctorLauncher
{
    class Program
    {
        private const string SERVICE_NAME = "ProctorService";
        private const string GUI_EXE_NAME = "ProctorAppGUI.exe";

        static void Main(string[] args)
        {
            Console.Title = "Exam Proctoring System Launcher";
            Console.ForegroundColor = ConsoleColor.Cyan;

            ShowBanner();

            try
            {
                if (!IsAdministrator())
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("  Administrator privileges required!");
                    Console.WriteLine(" Requesting admin rights...\n");
                    Thread.Sleep(1000);
                    RestartAsAdmin();
                    return;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" Running with administrator privileges\n");

                SetupService();

                EnsureServiceRunning();

                LaunchGUI();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n Setup complete!");
                Console.WriteLine(" Exam interface is now ready.\n");
                Console.WriteLine(" This window will close in 3 seconds...");

                Thread.Sleep(3000);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n ERROR: {ex.Message}\n");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                Environment.Exit(1);
            }
        }

        static void ShowBanner()
        {
            Console.WriteLine("╔════════════════════════════════════════════════╗");
            Console.WriteLine("║                                                ║");
            Console.WriteLine("║        🎓 EXAM PROCTORING SYSTEM 🎓           ║");
            Console.WriteLine("║              Automated Launcher                ║");
            Console.WriteLine("║                                                ║");
            Console.WriteLine("╚════════════════════════════════════════════════╝");
            Console.WriteLine();
        }

        static bool IsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        static void RestartAsAdmin()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath == null)
                {
                    throw new Exception("Could not determine executable path");
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas" 
                };

                Process.Start(startInfo);
                Environment.Exit(0);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n Admin rights are required to run this application.");
                Console.WriteLine("Please run as Administrator.\n");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n Failed to restart as admin: {ex.Message}");
                Console.WriteLine("\n Press any key to exit...");
                Console.ReadKey();
                Environment.Exit(1);
            }
        }

        static void SetupService()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(" Step 1/3: Checking service status...");

            try
            {
                using var sc = new ServiceController(SERVICE_NAME);
                var status = sc.Status;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"   Service found - Status: {status}");
            }
            catch (InvalidOperationException)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("     Service not installed");
                Console.WriteLine("\n Installing ProctorService...");
                InstallService();
            }
        }

        static void InstallService()
        {
            try
            {
                string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                string serviceExePath = Path.Combine(currentDir, "Service", "ProctorService.exe");


                if (!File.Exists(serviceExePath))
                {
                    serviceExePath = Path.Combine(currentDir, "ProctorService.exe");
                }

                if (!File.Exists(serviceExePath))
                {

                    string parentDir = Directory.GetParent(currentDir)?.FullName ?? currentDir;
                    serviceExePath = Path.Combine(parentDir, "Service", "ProctorService.exe");
                }

                if (!File.Exists(serviceExePath))
                {
                    throw new FileNotFoundException(
                        "ProctorService.exe not found!\n\n" +
                        "Expected locations:\n" +
                        $"  1. {Path.Combine(currentDir, "Service", "ProctorService.exe")}\n" +
                        $"  2. {Path.Combine(currentDir, "ProctorService.exe")}\n\n" +
                        "Please ensure the Service folder is in the same directory as the launcher."
                    );
                }

                Console.WriteLine($"\n    Service location: {serviceExePath}");
                Console.WriteLine("    Installing...");

                var installProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = $"create {SERVICE_NAME} binPath= \"{serviceExePath}\" start= demand",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                installProcess.Start();
                string output = installProcess.StandardOutput.ReadToEnd();
                string error = installProcess.StandardError.ReadToEnd();
                installProcess.WaitForExit();

                if (installProcess.ExitCode == 0 || output.Contains("SUCCESS"))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("    Service installed successfully!");
                }
                else if (error.Contains("exists") || output.Contains("exists"))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("    Service already exists");
                }
                else
                {
                    throw new Exception($"Installation failed:\n{output}\n{error}");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n   Installation failed: {ex.Message}");
                throw;
            }
        }

        static void EnsureServiceRunning()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n Step 2/3: Starting service...");

            try
            {
                using var sc = new ServiceController(SERVICE_NAME);

                if (sc.Status == ServiceControllerStatus.Running)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("    Service is already running");
                    return;
                }

                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    Console.WriteLine($"    Current status: {sc.Status}");
                    Console.WriteLine("    Starting service...");

                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("   Service started successfully!");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"     Service status: {sc.Status}");
                    Console.WriteLine("    Waiting for service to be ready...");

                    Thread.Sleep(2000);
                    sc.Refresh();

                    if (sc.Status != ServiceControllerStatus.Running)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    }
                }
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("    Service took too long to start");
                Console.WriteLine("    The service might still be starting in the background");
                throw;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"   Failed to start service: {ex.Message}");
                throw;
            }
        }

        static void LaunchGUI()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n Step 3/3: Launching exam interface...");

            try
            {
                string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                string guiPath = Path.Combine(currentDir, GUI_EXE_NAME);

                if (!File.Exists(guiPath))
                {
                    // Try parent directory
                    string parentDir = Directory.GetParent(currentDir)?.FullName ?? currentDir;
                    guiPath = Path.Combine(parentDir, GUI_EXE_NAME);
                }

                if (!File.Exists(guiPath))
                {
                    throw new FileNotFoundException(
                        $"ProctorAppGUI.exe not found!\n\n" +
                        $"Expected location: {Path.Combine(currentDir, GUI_EXE_NAME)}\n\n" +
                        "Please ensure ProctorAppGUI.exe is in the same folder as the launcher."
                    );
                }

                Console.WriteLine($"    GUI location: {guiPath}");
                Console.WriteLine("    Starting...");

                var guiProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = guiPath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(guiPath)
                });

                if (guiProcess != null)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("    Exam interface launched successfully!");
                }
                else
                {
                    throw new Exception("Failed to start GUI process");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"   Failed to launch GUI: {ex.Message}");
                throw;
            }
        }
    }
}