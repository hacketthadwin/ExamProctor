using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ProctorService
{
    public class DNSServer
    {
        private readonly ILogger _logger;
        private UdpClient? _udpServer;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly HashSet<string> _allowedDomains;
        private readonly string _upstreamDNS = "8.8.8.8";

        public DNSServer(ILogger logger)
        {
            _logger = logger;

            _allowedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "codeforces.com",
                "www.codeforces.com",
                "m1.codeforces.com",
                "m2.codeforces.com",
                "m3.codeforces.com",
                "cdn.codeforces.com",
                "static.codeforces.com",
                "codeforces.org",
                "www.codeforces.org",

                "microsoft.com",
                "windows.com",
                "msftconnecttest.com",
                "msftncsi.com",
                "dns.msftncsi.com",
                "www.msftconnecttest.com"
            };
        }

        public void Start()
        {
            try
            {
                try
                {
                    var dnscache = ServiceController.GetServices()
                        .FirstOrDefault(s => s.ServiceName == "Dnscache");

                    if (dnscache?.Status == ServiceControllerStatus.Running)
                    {
                        _logger.LogWarning("Stopping Windows DNS Client service (Dnscache)");
                        dnscache.Stop();
                        dnscache.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                        _logger.LogInformation("Dnscache stopped");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not stop Dnscache service - may cause port conflict");
                }

                _logger.LogInformation("Starting Custom DNS Server on port 53");

                _udpServer = new UdpClient(53);
                _cancellationTokenSource = new CancellationTokenSource();

                Task.Run(() => ListenForQueries(_cancellationTokenSource.Token));

                _logger.LogInformation("DNS Server started successfully");
                _logger.LogInformation($"Whitelisted domains: {_allowedDomains.Count}");
            }
            catch (SocketException sex) when (sex.ErrorCode == 10048)
            {
                _logger.LogError("Port 53 already in use! Another DNS server might be running.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start DNS server");
                throw;
            }
        }

        private async Task ListenForQueries(CancellationToken cancellationToken)
        {
            _logger.LogInformation("DNS Server listening for queries...");

            while (!cancellationToken.IsCancellationRequested && _udpServer != null)
            {
                try
                {
                    var result = await _udpServer.ReceiveAsync();
                    byte[] query = result.Buffer;
                    IPEndPoint clientEndPoint = result.RemoteEndPoint;

                    _ = Task.Run(() => ProcessQuery(query, clientEndPoint), cancellationToken);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Error receiving DNS query");
                    }
                }
            }

            _logger.LogInformation("DNS Server listener stopped");
        }

        private async Task ProcessQuery(byte[] query, IPEndPoint clientEndPoint)
        {
            try
            {
                string domain = ParseDomainFromQuery(query);

                if (string.IsNullOrEmpty(domain))
                {
                    _logger.LogWarning("Failed to parse domain from DNS query");
                    return;
                }

                _logger.LogInformation($"DNS Query: {domain} from {clientEndPoint.Address}");

                byte[] response;

                if (IsDomainAllowed(domain))
                {
                    _logger.LogInformation($"ALLOWED - Forwarding to upstream DNS");

                    response = await ForwardToUpstreamDNS(query);
                }
                else
                {
                    _logger.LogWarning($"BLOCKED - Returning NXDOMAIN for {domain}");

                    response = CreateNXDOMAINResponse(query);
                }

                if (_udpServer != null && response != null)
                {
                    await _udpServer.SendAsync(response, response.Length, clientEndPoint);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing DNS query");
            }
        }

        private string ParseDomainFromQuery(byte[] query)
        {
            try
            {
                if (query.Length < 13)
                    return string.Empty;

                int position = 12;
                var labels = new List<string>();

                while (position < query.Length)
                {
                    byte labelLength = query[position];

                    if (labelLength == 0)
                        break;

                    if (labelLength > 63)
                        break;

                    position++;

                    if (position + labelLength > query.Length)
                        break;

                    string label = Encoding.ASCII.GetString(query, position, labelLength);
                    labels.Add(label);
                    position += labelLength;
                }

                return string.Join(".", labels);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing domain from DNS query");
                return string.Empty;
            }
        }

        private bool IsDomainAllowed(string domain)
        {
            if (_allowedDomains.Contains(domain))
                return true;

            foreach (var allowedDomain in _allowedDomains)
            {
                if (domain.EndsWith("." + allowedDomain, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private async Task<byte[]> ForwardToUpstreamDNS(byte[] query)
        {
            try
            {
                using var upstreamClient = new UdpClient();
                upstreamClient.Connect(_upstreamDNS, 53);

                await upstreamClient.SendAsync(query, query.Length);

                var receiveTask = upstreamClient.ReceiveAsync();
                if (await Task.WhenAny(receiveTask, Task.Delay(5000)) == receiveTask)
                {
                    return receiveTask.Result.Buffer;
                }
                else
                {
                    _logger.LogWarning("Upstream DNS query timeout");
                    return CreateNXDOMAINResponse(query);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error forwarding to upstream DNS");
                return CreateNXDOMAINResponse(query);
            }
        }

        private byte[] CreateNXDOMAINResponse(byte[] query)
        {
            byte[] response = new byte[query.Length];
            Array.Copy(query, response, query.Length);

            response[2] = 0x81;
            response[3] = 0x83;

            return response;
        }

        public void Stop()
        {
            _logger.LogInformation("Stopping DNS Server");

            _cancellationTokenSource?.Cancel();
            _udpServer?.Close();
            _udpServer?.Dispose();
            _udpServer = null;

            try
            {
                var dnscache = ServiceController.GetServices()
                    .FirstOrDefault(s => s.ServiceName == "Dnscache");

                if (dnscache?.Status == ServiceControllerStatus.Stopped)
                {
                    _logger.LogInformation("Restarting Windows DNS Client service");
                    dnscache.Start();
                    dnscache.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not restart Dnscache service");
            }

            _logger.LogInformation("DNS Server stopped");
        }
    }
}