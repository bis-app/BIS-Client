using Microsoft.Extensions.Configuration;

namespace BISClient.Configuration;

internal static class AppConfiguration
{
    public static IConfigurationRoot Load()
    {
        var basePath = AppContext.BaseDirectory;

        return new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }

    public static AppSettings Bind(IConfiguration configuration)
    {
        var settings = configuration.GetSection(AppSettings.SectionName).Get<AppSettings>()
                       ?? new AppSettings();
        settings.Normalize();
        return settings;
    }
}
