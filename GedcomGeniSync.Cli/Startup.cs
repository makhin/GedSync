using System;
using System.Collections.Generic;
using GedcomGeniSync.Cli.Services;
using GedcomGeniSync.Models;
using GedcomGeniSync.Services;
using GedcomGeniSync.Services.Interfaces;
using GedcomGeniSync.Services.Photo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GedcomGeniSync.Cli;

public class Startup
{
    private readonly IReadOnlyCollection<ServiceDescriptor> _baseServices;

    public Startup()
    {
        var services = new ServiceCollection();
        ConfigureBaseServices(services);
        _baseServices = services.ToList().AsReadOnly();
    }

    public AsyncServiceScope CreateScope(bool verbose, Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();

        foreach (var descriptor in _baseServices)
        {
            ((IList<ServiceDescriptor>)services).Add(descriptor);
        }

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSimpleConsole(options =>
            {
                options.TimestampFormat = "HH:mm:ss ";
                options.SingleLine = true;
            });
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });

        configureServices?.Invoke(services);

        var provider = services.BuildServiceProvider();
        return provider.CreateAsyncScope();
    }

    private static void ConfigureBaseServices(IServiceCollection services)
    {
        services.AddSingleton<IConfigurationLoader, ConfigurationLoader>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<INameVariantsService, NameVariantsService>();
        services.AddSingleton<IGedcomLoader, GedcomLoader>();
        services.AddSingleton<PhotoConfig>();
        services.AddSingleton<IPhotoDownloadService>(sp =>
            new PhotoDownloadService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILogger<PhotoDownloadService>>()));
        services.AddSingleton<IPhotoCacheService>(sp =>
            new PhotoCacheService(
                sp.GetRequiredService<PhotoConfig>(),
                sp.GetRequiredService<IPhotoDownloadService>(),
                sp.GetRequiredService<ILogger<PhotoCacheService>>()));
        services.AddSingleton<IPhotoHashService, PhotoHashService>();
        services.AddSingleton<IPhotoCompareService, PhotoCompareService>();

        services.AddHttpClient("GeniApi", client =>
        {
            client.BaseAddress = new Uri("https://www.geni.com/api");
        });

        services.AddHttpClient("PhotoDownload", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
    }
}
