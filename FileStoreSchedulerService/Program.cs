using FileStoreSchedulerService;
using FileStoreSchedulerService.Models;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appconfigs.json", optional: false, reloadOnChange: true);
builder.Services.Configure<SchedulerOptions>(builder.Configuration);
builder.Services.AddHostedService<Worker>();

IHost host = builder.Build();
host.Run();
