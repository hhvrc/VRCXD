using Microsoft.AspNetCore.DataProtection;
using OpenShock.Internal.Common.Utils;
using VRCX.Common.Options;

namespace VRCX.Common.Extensions;

// Each Register* reads one configuration section (VRCX:<Name>, i.e. VRCX__<NAME>__<KEY> as
// environment variables), validates it, registers it as a singleton and hands it back so
// Program.cs can pass it on to the service registrations that need it at build time.
public static class ConfigurationExtensions
{
    public static DatabaseOptions RegisterDatabaseOptions(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetRequiredSection("VRCX:DB").Get<DatabaseOptions>();
        if (options is null)
            throw new InvalidOperationException("Missing or invalid configuration for VRCX:DB.");

        if (string.IsNullOrEmpty(options.Conn)) throw new InvalidOperationException("Missing or invalid connection string (VRCX:DB:Conn).");

        builder.Services.AddSingleton(options);
        return options;
    }

    public static RedisOptions RegisterRedisOptions(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetRequiredSection("VRCX:Redis").Get<RedisOptions>();
        if (options is null || string.IsNullOrEmpty(options.Conn))
            throw new InvalidOperationException("Missing or invalid configuration for VRCX:Redis");

        builder.Services.AddSingleton(options);
        return options;
    }

    public static ImageOptions RegisterImageOptions(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection("VRCX:Images").Get<ImageOptions>() ?? new ImageOptions();
        builder.Services.AddSingleton(options);
        return options;
    }

    public static VrchatOptions RegisterVrchatOptions(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection("VRCX:Vrchat").Get<VrchatOptions>() ?? new VrchatOptions();
        builder.Services.AddSingleton(options);
        return options;
    }

    public static KeyOptions RegisterKeyOptions(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection("VRCX:Keys").Get<KeyOptions>() ?? new KeyOptions();
        builder.Services.AddDataProtection()
            .SetApplicationName("VRCX")
            .PersistKeysToFileSystem(Directory.CreateDirectory(Path.GetFullPath(options.Dir)));
        builder.Services.AddSingleton(options);
        return options;
    }

    public static ErrorReportingOptions RegisterErrorReportingOptions(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection("VRCX:ErrorReporting").Get<ErrorReportingOptions>() ?? new ErrorReportingOptions();
        builder.Services.AddSingleton(options);
        return options;
    }

    public static AuthOptions RegisterAuthOptions(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetRequiredSection("VRCX:Auth").Get<AuthOptions>();
        if (options is null || string.IsNullOrWhiteSpace(options.Token))
            throw new InvalidOperationException("Missing API token (VRCX:Auth:Token). Every API caller has to present it.");
        if (options.Token.Length < 16)
            throw new InvalidOperationException("VRCX:Auth:Token must be at least 16 characters.");

        builder.Services.AddSingleton(options);
        return options;
    }

    public static MetricsOptions RegisterMetricsOptions(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection("VRCX:Metrics").Get<MetricsOptions>();

        options = new MetricsOptions
        {
            AllowedNetworks = options?.AllowedNetworks.Count is > 0 ? options.AllowedNetworks : TrustedProxiesFetcher.PrivateNetworks
        };

        builder.Services.AddSingleton(options);
        return options;
    }
}
