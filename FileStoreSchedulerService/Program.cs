using FileStoreSchedulerService;
using FileStoreSchedulerService.Models;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<HostOptions>(options =>
{
    options.ServicesStartConcurrently = false;
    options.ServicesStopConcurrently = false;
});
builder.Services.AddWindowsService();

builder.Configuration.AddJsonFile("AppConfig.json", optional: false, reloadOnChange: true);
builder.Services.Configure<SchedulerOptions>(builder.Configuration.GetSection("AppDefinition"));
builder.Services.AddHostedService<Worker>();

IHost host = builder.Build();
host.Run();
