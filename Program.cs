using Serilog;
using WireGuardDDNSMonitor;

var builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "WireGuard DDNS Monitor";
    })
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHostedService<Worker>();
    })
    .UseSerilog((context, config) => config
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.File(
            Path.Join(AppContext.BaseDirectory, "WireGuardDDNSMonitor_.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileTimeLimit: TimeSpan.FromDays(7)
        )
    );

var host = builder.Build();

host.Run();
