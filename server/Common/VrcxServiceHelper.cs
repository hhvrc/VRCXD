using VRCX.Common.Services;
using StackExchange.Redis;
using VRCX.Common.Services.Images;
using VRCX.Common.Services.Presence;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OpenShock.Internal.Common.ExceptionHandling;
using OpenTelemetry.Metrics;
using VRCX.Common.Authentication;
using VRCX.Common.Features.Friends;
using VRCX.Common.Features.Notifications;
using VRCX.Common.Live.Topics;
using VRCX.Common.Authentication.AuthenticationHandlers;
using VRCX.Common.HealthChecks;
using VRCX.Common.Options;
using VRCX.Common.Problems;
using VRCX.Common.Services.Automation;
using VRCX.Common.Features.Charts;
using VRCX.Common.Features.Feed;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.Services.Live;
using VRCX.Common.Websocket;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;
using JsonOptions = VRCX.Common.JsonSerialization.JsonOptions;

namespace VRCX.Common;

public static class VrcxServiceHelper
{
    /// <summary>Cap on a single dependency probe.</summary>
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);

    public static IServiceCollection AddVrcxDB(this IServiceCollection services, DatabaseOptions options)
    {
        // <---- Postgres EF Core ---->
        // One pooled factory, and the scoped context for request handlers is drawn from it: a
        // second options registration would map the Postgres enums twice onto the same Npgsql type
        // mapping source ("Multiple enum definitions registered for CLR type ...").
        services.AddSingleton<PresenceFlushInterceptor>();
        services.AddSingleton<LiveChangeInterceptor>();
        services.AddSingleton(sp => new FriendWriter.CommitHook(new Lazy<FriendWriter>(sp.GetRequiredService<FriendWriter>)));
        services.AddSingleton(sp => new NotificationWriter.CommitHook(new Lazy<NotificationWriter>(sp.GetRequiredService<NotificationWriter>)));
        services.AddSingleton(sp => new Lazy<IPresenceCache>(sp.GetRequiredService<IPresenceCache>));
        services.AddPooledDbContextFactory<VrcxContext>((sp, builder) =>
        {
            VrcxContext.ConfigureOptionsBuilder(builder, options.Conn, options.Debug);
            // The friend writer's hook after the presence flush: it announces changes that rows read from Redis show.
            builder.AddInterceptors(sp.GetRequiredService<PresenceFlushInterceptor>(), sp.GetRequiredService<LiveChangeInterceptor>(), sp.GetRequiredService<FriendWriter.CommitHook>(),
                sp.GetRequiredService<NotificationWriter.CommitHook>());
        });
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContext());

        // Readiness for the database we just registered. Deliberately its own connection rather than
        // the pooled EF context: this must answer "is Postgres up", not "is EF's pool warm".
        services.AddHealthChecks()
            .AddNpgSql(
                options.Conn,
                name: HealthCheckNames.Database,
                tags: [HealthCheckTags.Ready],
                timeout: HealthCheckTimeout)
            .AddCheck<AccountCollectionHealthCheck>(HealthCheckNames.Accounts, tags: [HealthCheckTags.Ready], timeout: HealthCheckTimeout);

        return services;
    }

    /// <summary>Redis: where things that change constantly live. Everything here can be rebuilt from Postgres.</summary>
    public static IServiceCollection AddVrcxRedis(this IServiceCollection services, RedisOptions options)
    {
        var configuration = ConfigurationOptions.Parse(options.Conn);
        // Start up and keep retrying while Redis is away: Postgres still has the truth.
        configuration.AbortOnConnectFail = false;
        services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(configuration));
        services.AddSingleton<PresenceEventReader>();
        services.AddSingleton<RedisPresenceCache>();
        services.AddSingleton<IPresenceCache>(sp => sp.GetRequiredService<RedisPresenceCache>());
        services.AddSingleton<IPendingPresenceQueue, RedisPendingPresenceQueue>();
        services.AddSingleton<IGameClientState, RedisGameClientState>();
        services.AddHealthChecks().AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(), name: "redis", tags: [HealthCheckTags.Ready], timeout: HealthCheckTimeout);
        return services;
    }

    /// <summary>
    /// Register all VRCX services for PRODUCTION use: exception handling, the API token
    /// authentication scheme, controllers + JSON, API versioning + OpenAPI, CORS, metrics,
    /// and the VRChat-facing services (sessions, store, sync, pipeline ingestion).
    /// </summary>
    public static IServiceCollection AddVrcxServices(this IServiceCollection services,
        Action<AuthenticationBuilder>? configureAuth = null, Action<MeterProviderBuilder>? configureMetrics = null)
    {
        // <---- ASP.NET ---->
        // OpenShockExceptionHandler (OpenShock.Internal.AspNet) takes JsonSerializerOptions via ctor injection.
        services.AddSingleton(JsonOptions.Default);
        services.AddExceptionHandler<OpenShockExceptionHandler>();

        services.TryAddSingleton(TimeProvider.System);

        var authBuilder = services
            .AddAuthentication(opt =>
            {
                opt.DefaultScheme = VrcxAuthSchemes.ApiToken;
                opt.DefaultAuthenticateScheme = VrcxAuthSchemes.ApiToken;
                opt.DefaultChallengeScheme = VrcxAuthSchemes.ApiToken;
            })
            .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthentication>(VrcxAuthSchemes.ApiToken, _ => { });

        configureAuth?.Invoke(authBuilder);

        services.AddAuthorization();

        services.ConfigureHttpJsonOptions(opt => JsonOptions.ConfigureDefault(opt.SerializerOptions));
        services.AddControllers().AddJsonOptions(opt => JsonOptions.ConfigureDefault(opt.JsonSerializerOptions));

        var apiVersioningBuilder = services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            // All routes are versioned via the /{version:apiVersion}/ segment
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        }).AddMvc();

        apiVersioningBuilder.AddApiExplorer(setup =>
        {
            setup.GroupNameFormat = "VVV";
            setup.SubstituteApiVersionInUrl = true;
            setup.DefaultApiVersion = new ApiVersion(1, 0);
            setup.AssumeDefaultVersionWhenUnspecified = true;
        });

        apiVersioningBuilder.AddOpenApi(options =>
        {
            var version = options.Description.ApiVersion.ToString();
            var isDeprecated = options.Description.IsDeprecated;

            options.Document.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "VRCX.API";
                document.Info.Version = version;
                if (isDeprecated)
                {
                    document.Info.Description = (document.Info.Description ?? "") + " This API version has been deprecated.";
                }

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
                {
                    [VrcxAuthSchemes.ApiToken] = new OpenApiSecurityScheme
                    {
                        Name = Constants.AuthConstants.ApiTokenHeaderName,
                        Description = "Enter the server's API token (VRCX:Auth:Token)",
                        In = ParameterLocation.Header,
                        Type = SecuritySchemeType.ApiKey,
                        Scheme = VrcxAuthSchemes.ApiToken
                    }
                };

                return Task.CompletedTask;
            });

            options.Document.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;

            // XML doc comments are picked up by Microsoft.AspNetCore.OpenApi's source generator
            // (GenerateDocumentationFile is on for every project).
        });

        // generic ASP.NET stuff
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddWebEncoders();
        services.AddProblemDetails();

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder.SetIsOriginAllowed(_ => true);
                builder.AllowAnyHeader();
                builder.AllowCredentials();
                builder.AllowAnyMethod();
                builder.SetPreflightMaxAge(TimeSpan.FromHours(24));
            });
        });

        // This needs to be at this position, earlier will break validation error responses
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var problemDetails = new ValidationProblem(context.ModelState);
                return problemDetails.ToObjectResult(context.HttpContext);
            };
        });

        // OpenTelemetry
        services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(VRCX.Common.Services.Live.GameLogMetrics.MeterName)
                    .AddRuntimeInstrumentation()
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddPrometheusExporter();

                configureMetrics?.Invoke(metrics);
            });

        // <---- VRCX Services ---->
        services.AddSingleton<IVrchatClientFactory, VrchatClientFactory>();
        services.AddSingleton<SavedCredentialStore>();
        services.AddSingleton<IAccountSessionManager, AccountSessionManager>();
        services.AddSingleton<CurrentUserCache>();
        services.AddSingleton<IAvatarImageResolver, AvatarImageResolver>();
        services.AddSingleton<VrchatStore>();
        services.AddSingleton<PresenceDebouncer>();
        services.AddSingleton<IFeedService, FeedService>();
        services.AddSingleton<AvatarFeedCleanup>();
        services.AddSingleton<IChartsService, ChartsService>();
        services.AddSingleton<MutualGraphService>();
        services.AddSingleton<IPipelineIngestor, PipelineIngestor>();
        services.AddSingleton<IAccountSyncService, AccountSyncService>();
        services.AddSingleton<ILiveEventBus, LiveEventBus>();
        services.AddHttpClient(YouTubeLookup.ClientName, c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<YouTubeLookup>();
        services.AddHttpClient(VrchatStatus.ClientName, c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<VrchatStatus>();
        services.AddSingleton<IGameLogIngestor, GameLogIngestor>();
        services.AddSingleton<IpcIngestor>();
        services.AddSingleton<LiveSocketHandler>();
        // Live topics (docs/live-topics.md): the hub, and one singleton per topic.
        services.AddSingleton<TopicHub>();
        services.AddSingleton<FriendsTopic>();
        services.AddSingleton<ILiveTopic>(sp => sp.GetRequiredService<FriendsTopic>());
        services.AddSingleton<NotificationsTopic>();
        services.AddSingleton<ILiveTopic>(sp => sp.GetRequiredService<NotificationsTopic>());
        services.AddSingleton<NotificationWriter>();
        services.AddSingleton<FriendWriter>();
        services.AddSingleton<GameClientRegistry>();
        services.AddSingleton<VRCX.Common.Services.Sync.OwnAvatarsFetchLog>();
        services.AddSingleton<DesktopClients>();
        services.AddSingleton<CrashRelaunch>();
        services.AddSingleton<AgentSettingsPush>();
        services.AddSingleton<RegistryBackups>();
        services.AddSingleton<DebugLoggingCheck>();
        services.AddSingleton<AutoCacheSweep>();
        services.AddSingleton<MediaSaver>();
        services.AddSingleton<DiscordPresenceService>();
        services.AddSingleton<VRCX.Common.Services.Live.Photon.PhotonService>();
        services.AddHostedService(sp => sp.GetRequiredService<DiscordPresenceService>());
        services.AddSingleton<InstanceQueues>();
        services.AddSingleton<AccountAutomation>();
        services.AddSingleton<IImageFetcher, VrchatImageFetcher>();
        services.AddSingleton<ImageCache>();

        // Read models over what is stored, per request on the request's context: the endpoints and
        // the MCP tools share them, so neither carries query logic of its own.
        services.AddScoped<VRCX.Common.Features.Accounts.AccountQueries>();
        services.AddSingleton<VRCX.Common.Features.Accounts.AccountSessionRunner>();
        services.AddSingleton<VRCX.Common.Features.Notifications.NotificationRefreshGate>();
        services.AddScoped<VRCX.Common.Features.Notifications.NotificationQueries>();
        services.AddScoped<VRCX.Common.Features.Notifications.NotificationActions>();
        services.AddScoped<VRCX.Common.Features.Moderation.ModerationQueries>();
        services.AddScoped<VRCX.Common.Features.Moderation.ModerationActions>();
        services.AddScoped<VRCX.Common.Features.Locations.LocationQueries>();
        services.AddScoped<FriendQueries>();
        services.AddScoped<FriendActions>();
        services.AddScoped<VRCX.Common.Features.GameLog.GameLogQueries>();
        services.AddScoped<VRCX.Common.Features.GameLog.GameLogActions>();
        services.AddScoped<VRCX.Common.Features.GameLog.PreviousInstanceQueries>();
        services.AddScoped<VRCX.Common.Features.Users.UserQueries>();
        services.AddScoped<VRCX.Common.Features.Content.ContentQueries>();
        services.AddScoped<VRCX.Common.Features.Groups.GroupQueries>();
        services.AddScoped<VRCX.Common.Features.Search.SearchStore>();
        services.AddScoped<VRCX.Common.Features.Users.UserNotes>();
        services.AddScoped<VRCX.Common.Features.Gallery.PrintFavorites>();
        services.AddScoped<VRCX.Common.Features.Desktop.AccountDesktop>();
        services.AddScoped<VRCX.Common.Features.Worlds.AccountWorlds>();
        services.AddScoped<VRCX.Common.Features.Avatars.OwnAvatars>();
        services.AddScoped<VRCX.Common.Features.Instances.InstanceQueries>();
        services.AddSingleton<VrchatReadCache>();
        services.AddSingleton<VRCX.Common.Features.Content.ContentRefresh>();
        services.AddScoped<VRCX.Common.Features.Users.UserSearch>();
        services.AddScoped<VRCX.Common.Features.Database.DatabaseQueries>();
        services.AddScoped<VRCX.Common.Features.Favorites.FavoriteQueries>();
        services.AddScoped<VRCX.Common.Features.Favorites.FavoriteActions>();
        services.AddScoped<VRCX.Common.Features.UserActivity.UserActivityQueries>();
        services.AddScoped<VRCX.Common.Features.QuickSearch.QuickSearchQueries>();
        services.AddScoped<VRCX.Common.Features.Avatars.AvatarHistory>();
        services.AddScoped<VRCX.Common.Features.Memos.MemoStore>();

        return services;
    }

    private static void TryAddSingleton<T>(this IServiceCollection services, T instance) where T : class
    {
        if (services.All(d => d.ServiceType != typeof(T))) services.AddSingleton(instance);
    }
}
