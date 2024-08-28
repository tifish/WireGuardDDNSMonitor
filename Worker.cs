using System.Net;
using System.ServiceProcess;

namespace WireGuardDDNSMonitor;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    private readonly List<string> _domains;
    private readonly List<string> _ips;
    private readonly string _serviceName = "";
    private const string ServiceNamePrefix = "WireGuardTunnel$";

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;

        _domains = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Domains.txt")).ToList();
        _ips = Enumerable.Repeat("", _domains.Count).ToList();

        // get all services
        var service = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName.StartsWith(ServiceNamePrefix));
        if (service == null)
        {
            _logger.LogError("No WireGuard service found");
            return;
        }

        _serviceName = service.ServiceName;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_serviceName))
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            var ipChanged = false;

            for (var i = 0; i < _domains.Count; i++)
            {
                var addresses = await Dns.GetHostAddressesAsync(_domains[i], stoppingToken);
                var ip = addresses.FirstOrDefault()?.ToString();
                if (ip == null)
                    continue;

                if (ip == _ips[i])
                    continue;

                ipChanged = true;
                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("[{time}]{domain} IP changed from {oldIP} to {newIP}",
                        DateTimeOffset.Now, _domains[i], _ips[i], ip);
                _ips[i] = ip;
            }

            if (ipChanged)
                RestartService("WireGuardTunnel$WireGuard");

            await Task.Delay(10 * 1000, stoppingToken);
        }
    }

    private void RestartService(string serviceName)
    {
        try
        {
            using var service = new ServiceController(serviceName);
            if (service.Status == ServiceControllerStatus.Running)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped);
            }

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running);
        }
        catch (Exception ex)
        {
            _logger.LogError("An error occurred when restarting service: {message}", ex.Message);
        }
    }
}
