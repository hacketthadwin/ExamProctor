using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO.Pipes;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ProctorAppGUI
{
    public partial class Form1 : Form
    {
        private bool _examMode = false;
        private Stopwatch _examTimer = new Stopwatch();
        private System.Windows.Forms.Timer _uiTimer;

        public Form1()
        {
            InitializeComponent();
            SetupUI();

            _uiTimer = new System.Windows.Forms.Timer();
            _uiTimer.Interval = 1000;
            _uiTimer.Tick += UpdateUI;
            _uiTimer.Start();
        }

        private void SetupUI()
        {
            this.Text = "Exam Proctoring System";
            this.Size = new Size(500, 400);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(240, 240, 240);

            var titleLabel = new Label
            {
                Text = "🎓 EXAM PROCTORING SYSTEM",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                AutoSize = true,
                Location = new Point(80, 30)
            };
            this.Controls.Add(titleLabel);

            var statusPanel = new Panel
            {
                Location = new Point(50, 90),
                Size = new Size(400, 150),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(statusPanel);

            lblStatus = new Label
            {
                Text = "Status: ⚪ Inactive",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.Gray,
                AutoSize = true,
                Location = new Point(20, 20)
            };
            statusPanel.Controls.Add(lblStatus);

            lblTimer = new Label
            {
                Text = "Time Elapsed: 00:00:00",
                Font = new Font("Segoe UI", 11),
                ForeColor = Color.Black,
                AutoSize = true,
                Location = new Point(20, 50)
            };
            statusPanel.Controls.Add(lblTimer);

            lblInfo = new Label
            {
                Text = "Click 'Start Exam' to begin proctored session",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.DimGray,
                Location = new Point(20, 90),
                Size = new Size(360, 40)
            };
            statusPanel.Controls.Add(lblInfo);

            btnToggle = new Button
            {
                Text = "START EXAM MODE",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                Size = new Size(300, 60),
                Location = new Point(100, 270),
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnToggle.FlatAppearance.BorderSize = 0;
            btnToggle.Click += BtnToggle_Click;
            this.Controls.Add(btnToggle);

            var footerLabel = new Label
            {
                Text = "⚠️ Ensure ProctorService is running",
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.Gray,
                AutoSize = true,
                Location = new Point(150, 350)
            };
            this.Controls.Add(footerLabel);
        }

        private Label lblStatus;
        private Label lblTimer;
        private Label lblInfo;
        private Button btnToggle;

        private async void BtnToggle_Click(object sender, EventArgs e)
        {
            btnToggle.Enabled = false;

            if (!_examMode)
            {
                var result = MessageBox.Show(
                    "You are about to enter EXAM MODE.\n\n" +
                    "The following restrictions will apply:\n" +
                    "• Internet limited to Codeforces.com only\n" +
                    "• VPN connections will be blocked\n" +
                    "• Unauthorized applications will be terminated\n" +
                    "• VM detection active\n\n" +
                    "Do you want to continue?",
                    "Confirm Exam Mode",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {

                    bool success = await Task.Run(() => SendCommand("ENTER"));

                    if (success)
                    {
                        _examMode = true;
                        _examTimer.Start();

                        lblStatus.Text = "Status: 🟢 EXAM MODE ACTIVE";
                        lblStatus.ForeColor = Color.Green;
                        lblInfo.Text = "✓ Firewall enabled\n✓ Only Codeforces accessible\n✓ VPN detection active";
                        btnToggle.Text = "EXIT EXAM MODE";
                        btnToggle.BackColor = Color.FromArgb(220, 53, 69);

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://codeforces.com",
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        MessageBox.Show(
                            "Failed to start exam mode! The ProctorService may not be running or accessible.",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
            }
            else
            {
                var result = MessageBox.Show(
                    "Are you sure you want to EXIT exam mode?\n\n" +
                    "This will end your proctored session.",
                    "Confirm Exit",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    await Task.Run(() => SendCommand("EXIT"));

                    _examMode = false;
                    _examTimer.Stop();
                    _examTimer.Reset();

                    lblStatus.Text = "Status: ⚪ Inactive";
                    lblStatus.ForeColor = Color.Gray;
                    lblInfo.Text = "Exam mode ended. All restrictions removed.";
                    lblTimer.Text = "Time Elapsed: 00:00:00";
                    btnToggle.Text = "START EXAM MODE";
                    btnToggle.BackColor = Color.FromArgb(0, 122, 204);

                    MessageBox.Show(
                        "Exam mode has been successfully deactivated.\n\n" +
                        "All restrictions have been removed.",
                        "Exam Ended",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }

            btnToggle.Enabled = true;
        }

        private void UpdateUI(object sender, EventArgs e)
        {
            if (_examMode && _examTimer.IsRunning)
            {
                var elapsed = _examTimer.Elapsed;
                lblTimer.Text = $"Time Elapsed: {elapsed.Hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
            }
        }


        private bool SendCommand(string command)
        {
            const string CommandPipeName = "ProctorPipe";
            const string ResponsePipeName = "ProctorPipe_Response";
            const int TimeoutMs = 20000; 

            try
            {
                using (var cmdPipe = new NamedPipeClientStream(".", CommandPipeName, PipeDirection.Out))
                {
                    try
                    {
                        cmdPipe.Connect(TimeoutMs);
                    }
                    catch (TimeoutException)
                    {
                        this.Invoke((MethodInvoker)delegate
                        {
                            MessageBox.Show(
                                "IPC Connection Timeout (Command Pipe)\n\n" +
                                "The ProctorService is not responding to connection requests.\n\n" +
                                "Possible causes:\n" +
                                "• Service is not running\n" +
                                "• Service is still starting up\n" +
                                "• Service crashed or is unresponsive\n\n" +
                                "Try: sc query ProctorService",
                                "Service Unreachable",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        });
                        return false;
                    }

                    using (var writer = new StreamWriter(cmdPipe) { AutoFlush = true })
                    {
                        writer.WriteLine(command);
                    }
                }

                using (var responsePipe = new NamedPipeClientStream(".", ResponsePipeName, PipeDirection.In))
                {
                    try
                    {
                        responsePipe.Connect(TimeoutMs);
                    }
                    catch (TimeoutException)
                    {
                        this.Invoke((MethodInvoker)delegate
                        {
                            MessageBox.Show(
                                "IPC Connection Timeout (Response Pipe)\n\n" +
                                "The ProctorService received your command but didn't respond in time.\n\n" +
                                "Possible causes:\n" +
                                "• Service is processing slowly\n" +
                                "• System resources are exhausted\n" +
                                "• Service encountered an error\n\n" +
                                "Try: sc stop ProctorService && sc start ProctorService",
                                "Service Response Timeout",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        });
                        return false;
                    }

                    using (var reader = new StreamReader(responsePipe))
                    {
                        string response = reader.ReadLine() ?? "";
                        return response == "OK";
                    }
                }
            }
            catch (FileNotFoundException)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    MessageBox.Show(
                        "ProctorService Pipe Not Found\n\n" +
                        "The IPC pipes do not exist. This means:\n" +
                        "• Service is not running\n" +
                        "• Service failed to initialize\n\n" +
                        "Please ensure ProctorService is running:\n" +
                        "1. Open Services (services.msc)\n" +
                        "2. Find 'ProctorService'\n" +
                        "3. Right-click and select 'Start'",
                        "Service Not Running",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                });
                return false;
            }
            catch (Exception ex)
            {
                string errorMsg = $"IPC Error: {ex.GetType().Name}\n{ex.Message}";
                this.Invoke((MethodInvoker)delegate
                {
                    MessageBox.Show(errorMsg, "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
                return false;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_examMode)
            {
                var result = MessageBox.Show(
                    "Exam mode is still active!\n\n" +
                    "Are you sure you want to exit? All restrictions will be removed automatically.",
                    "Warning",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                Task.Run(() => SendCommand("EXIT")).Wait();
            }

            base.OnFormClosing(e);
        }
    }
}