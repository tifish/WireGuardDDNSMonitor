using WireGuardDDNSMonitor;

var builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "WireGuard DDNS Monitor";
    })
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHostedService<Worker>();
    });

var host = builder.Build();

host.Run();
