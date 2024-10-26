using System.Net;
using System.ServiceProcess;
using NETWORKLIST;

namespace WireGuardDDNSMonitor;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    private readonly List<string> _domains;
    private readonly List<string> _ips;
    private readonly string _serviceName = "";
    private readonly string _adapterName = "";
    private const string ServiceNamePrefix = "WireGuardTunnel$";
    private const string DomainsFileName = "Domains.txt";

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;

        var domainsFilePath = Path.Join(AppContext.BaseDirectory, DomainsFileName);
        if (File.Exists(domainsFilePath))
        {
            _domains = File.ReadAllLines(domainsFilePath)
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .Select(domain => domain.Trim())
                .ToList();
            _ips = Enumerable.Repeat("", _domains.Count).ToList();
        }
        else
        {
            _logger.LogError("{DomainsFile} file not found", DomainsFileName);
            _domains = [];
            _ips = [];
        }

        // get all services
        var service = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName.StartsWith(ServiceNamePrefix));
        if (service == null)
        {
            _logger.LogError("No WireGuard service found");
            return;
        }

        _serviceName = service.ServiceName;
        _adapterName = _serviceName[ServiceNamePrefix.Length..];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_serviceName))
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await CheckIPChange(stoppingToken))
                    RestartServiceIfRunning();

                CheckAdapterProfile();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred");
            }

            await Task.Delay(10 * 1000, stoppingToken);
        }
    }

    private async Task<bool> CheckIPChange(CancellationToken stoppingToken)
    {
        if (_domains.Count == 0)
            return false;

        var ipChanged = false;

        for (var i = 0; i < _domains.Count; i++)
        {
            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(_domains[i], stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred when getting IP address for {Domain}", _domains[i]);
                continue;
            }

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

        return ipChanged;
    }

    private void RestartServiceIfRunning()
    {
        try
        {
            using var service = new ServiceController(_serviceName);
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

    private NetworkListManager? _networkListManager;

    private void CheckAdapterProfile()
    {
        if (_adapterName == "")
            return;

        _networkListManager ??= new NetworkListManager();
        var connectedNetworks = _networkListManager.GetNetworks(NLM_ENUM_NETWORK.NLM_ENUM_NETWORK_CONNECTED).Cast<INetwork>();
        foreach (var network in connectedNetworks)
        {
            if (network.GetDescription() != _adapterName)
                continue;

            if (network.GetCategory() != NLM_NETWORK_CATEGORY.NLM_NETWORK_CATEGORY_PRIVATE)
            {
                _logger.LogInformation("Changing network category to private for {AdapterName}", _adapterName);
                network.SetCategory(NLM_NETWORK_CATEGORY.NLM_NETWORK_CATEGORY_PRIVATE);
            }
            break;
        }
    }
}
