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
    private const string DomainsFileName = "Domains.txt";

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;

        var domainsFilePath = Path.Join(AppContext.BaseDirectory, DomainsFileName);
        if (!File.Exists(domainsFilePath))
        {
            _logger.LogError("{DomainsFile} file not found", DomainsFileName);
            _domains = [];
            _ips = [];
            return;
        }

        _domains = File.ReadAllLines(domainsFilePath).ToList();
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
        if (_domains.Count == 0)
            return;
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
                if (!string.IsNullOrEmpty(_ips[i]))
                    _logger.LogInformation("{Domain} IP changed from {OldIP} to {NewIP}", _domains[i], _ips[i], ip);
                _ips[i] = ip;
            }

            if (ipChanged)
                RestartServiceIfRunning("WireGuardTunnel$WireGuard");

            await Task.Delay(10 * 1000, stoppingToken);
        }
    }

    private void RestartServiceIfRunning(string serviceName)
    {
        try
        {
            using var service = new ServiceController(serviceName);
            if (service.Status != ServiceControllerStatus.Running)
                return;

            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped);

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running);
        }
        catch (Exception ex)
        {
            _logger.LogError("An error occurred when restarting service: {Message}", ex.Message);
        }
    }
}
