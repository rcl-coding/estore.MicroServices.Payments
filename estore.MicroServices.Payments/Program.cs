#nullable disable
using estore.MicroServices.Payments.BackgroundWorker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RCL.WebHook.DatabaseContext;

IConfiguration configuration = null;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration(builder =>
    {
        builder.AddJsonFile("local.settings.json", true, true);
        builder.AddEnvironmentVariables();
        configuration = builder.Build();
    })
    .ConfigureServices(services =>
    {
        services.AddDbContext<WebHookDbContext>(options => options.UseSqlServer(Environment.GetEnvironmentVariable("ConnectionStrings:Database")));
        services.AddHostedService<QueuedHostedService>();
        services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
    })
    .Build();

host.Run();