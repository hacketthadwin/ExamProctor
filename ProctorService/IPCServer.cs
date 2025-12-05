using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ProctorService
{
    public class IPCServer
    {
        private readonly ILogger _logger;
        private Thread? _commandListenerThread;
        private bool _isRunning;
        private const string COMMAND_PIPE_NAME = "ProctorPipe";
        private const string RESPONSE_PIPE_NAME = "ProctorPipe_Response";

        public event Action<string>? OnCommandReceived;

        public IPCServer(ILogger logger)
        {
            _logger = logger;
        }

        public void Start()
        {
            _isRunning = true;

            _commandListenerThread = new Thread(ListenForCommands)
            {
                IsBackground = true,
                Name = "ProctorIPC-CommandListener"
            };
            _commandListenerThread.Start();

            _logger.LogInformation("IPC Server started - listening on pipes: {CommandPipe} / {ResponsePipe}",
                COMMAND_PIPE_NAME, RESPONSE_PIPE_NAME);
        }

        private void ListenForCommands()
        {
            while (_isRunning)
            {
                NamedPipeServerStream? commandPipe = null;

                try
                {
                    var pipeSecurity = CreatePipeSecurity();

                    _logger.LogInformation("Creating Command Pipe (Input)...");

                    commandPipe = NamedPipeServerStreamAcl.Create(
                        COMMAND_PIPE_NAME,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Message,
                        PipeOptions.None,
                        inBufferSize: 4096,
                        outBufferSize: 4096,
                        pipeSecurity
                    );

                    _logger.LogInformation("Waiting for client connection on {CommandPipe}...", COMMAND_PIPE_NAME);
                    commandPipe.WaitForConnection();
                    _logger.LogInformation("Client connected to command pipe!");

                    string? command = null;

                    using (var reader = new StreamReader(commandPipe, Encoding.UTF8))
                    {
                        command = reader.ReadLine();
                    }

                    _logger.LogInformation("Client disconnected from command pipe after writing.");

                    if (!string.IsNullOrEmpty(command))
                    {
                        string commandToHandle = command;
                        _logger.LogInformation("Dispatching command: {Command}", commandToHandle);

                        Task.Run(() =>
                        {
                            string response = "OK";
                            try
                            {
                                OnCommandReceived?.Invoke(commandToHandle);
                                _logger.LogInformation("Command processed: {Command}", commandToHandle);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to execute command: {Command}", commandToHandle);
                                response = "ERROR";
                            }

                            SendResponse(response);
                        });
                    }
                    else
                    {
                        _logger.LogWarning("Received empty command. Sending ERROR response.");
                        SendResponse("ERROR");
                    }
                }
                catch (IOException ioEx)
                {
                    _logger.LogError(ioEx, "IO Error in IPC Command Listener. Pipe may have been broken/closed.");
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    _logger.LogError(uaEx, "Access Denied - check pipe permissions (Running as LocalSystem?).");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "IPC Server error: {ErrorMessage}", ex.Message);
                }
                finally
                {
                    try
                    {
                        commandPipe?.Dispose();
                    }
                    catch { }

                    if (_isRunning)
                    {
                        Thread.Sleep(500);
                    }
                }
            }

            _logger.LogInformation("IPC Server command listener thread exiting");
        }

        private void SendResponse(string response)
        {
            NamedPipeServerStream? responsePipe = null;

            try
            {
                var pipeSecurity = CreatePipeSecurity();

                _logger.LogInformation("Creating Response Pipe (Output)...");

                responsePipe = NamedPipeServerStreamAcl.Create(
                    RESPONSE_PIPE_NAME,
                    PipeDirection.Out,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Message,
                    PipeOptions.None,
                    inBufferSize: 4096,
                    outBufferSize: 4096,
                    pipeSecurity
                );

                _logger.LogInformation("Waiting for client connection on {ResponsePipe}...", RESPONSE_PIPE_NAME);

                responsePipe.WaitForConnection();
                _logger.LogInformation("Client connected to response pipe!");

                using (var writer = new StreamWriter(responsePipe, Encoding.UTF8) { AutoFlush = true })
                {
                    writer.WriteLine(response);
                    _logger.LogInformation("Sent response: {Response}", response);
                }

                responsePipe.Flush();

                _logger.LogInformation("Client disconnected from response pipe");
            }
            catch (IOException ioEx)
            {
                _logger.LogError(ioEx, "IO Error sending response. Client may have timed out or closed connection.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending response: {ErrorMessage}", ex.Message);
            }
            finally
            {
                try
                {
                    responsePipe?.Dispose();
                }
                catch { }
            }
        }

        private PipeSecurity CreatePipeSecurity()
        {
            var pipeSecurity = new PipeSecurity();

            var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var everyoneRule = new PipeAccessRule(
                everyoneSid,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow
            );
            pipeSecurity.AddAccessRule(everyoneRule);

            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var systemRule = new PipeAccessRule(
                systemSid,
                PipeAccessRights.FullControl,
                AccessControlType.Allow
            );
            pipeSecurity.AddAccessRule(systemRule);

            var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var adminsRule = new PipeAccessRule(
                adminsSid,
                PipeAccessRights.FullControl,
                AccessControlType.Allow
            );
            pipeSecurity.AddAccessRule(adminsRule);

            return pipeSecurity;
        }

        public void Stop()
        {
            _isRunning = false;
            _logger.LogInformation("IPC Server stopped");

            try
            {
                if (_commandListenerThread != null && _commandListenerThread.IsAlive)
                {
                    _commandListenerThread.Join(1000);
                }
            }
            catch { }
        }
    }
}