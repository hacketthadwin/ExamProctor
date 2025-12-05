using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProctorWorker = ProctorService.ProctorWorker;
IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService() // This makes it run as Windows Service
    .ConfigureServices(services =>
    {
        services.AddHostedService<ProctorWorker>();
    })
    .Build();

await host.RunAsync();