using System.Net;
using EasyAuthLocalEmulator.Auth;
using EasyAuthLocalEmulator.Configuration;
using EasyAuthLocalEmulator.Proxy;

namespace EasyAuthLocalEmulator.Hosting;

public sealed class EmulatorHost
{
    public async Task RunAsync(
        EmulatorOptions options,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        await using WebApplication application = Build(options);
        await application.StartAsync(cancellationToken);

        await standardOutput.WriteLineAsync($"Upstream:  {options.Upstream}");
        await standardOutput.WriteLineAsync($"Proxy:     {options.ProxyOrigin}");
        await standardOutput.WriteLineAsync($"Login:     {options.LoginUrl}");
        await standardOutput.WriteLineAsync(
            $"Profile:   {options.SelectedProfileName ?? "interactive"}");
        await standardOutput.WriteLineAsync(
            $"Mode:      {(options.NoUi ? "no-ui (shared process identity)" : "interactive")}");
        await standardOutput.WriteLineAsync(
            "Warning: open the Proxy URL; using the upstream URL bypasses authentication.");

        if (options.OpenBrowser &&
            !BrowserLauncher.TryOpen(options.ProxyOrigin, out string? browserError))
        {
            await standardError.WriteLineAsync(
                $"warning: Could not open the default browser: {browserError}");
        }

        try
        {
            await application.WaitForShutdownAsync(cancellationToken);
        }
        finally
        {
            using CancellationTokenSource stopTimeout = new(TimeSpan.FromSeconds(5));
            await application.StopAsync(stopTimeout.Token);
        }
    }

    public static WebApplication Build(EmulatorOptions options, TimeProvider? timeProvider = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(EmulatorHost).Assembly.FullName
        });

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.Listen(IPAddress.Loopback, options.Port);
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(consoleOptions =>
        {
            consoleOptions.SingleLine = true;
            consoleOptions.TimestampFormat = "HH:mm:ss ";
        });

        TimeProvider effectiveTimeProvider = timeProvider ?? TimeProvider.System;
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<TimeProvider>(effectiveTimeProvider);
        builder.Services.AddSingleton(serviceProvider =>
            new InMemorySessionStore(
                serviceProvider.GetRequiredService<TimeProvider>(),
                options.SessionLifetime));
        builder.Services.AddSingleton<LocalAuthenticationService>();
        builder.Services.AddSingleton<PrincipalBuilder>();
        builder.Services.AddSingleton<RedirectUriValidator>();
        builder.Services.AddSingleton(IdentityProviderRegistry.Default);
        builder.Services.AddHostedService<SessionCleanupService>();
        builder.Services.AddRazorPages();
        builder.Services.AddEasyAuthProxy();

        WebApplication application = builder.Build();
        application.UseExceptionHandler(errorApplication =>
        {
            errorApplication.Run(async context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(
                    "The local authentication emulator could not complete the request.");
            });
        });
        application.UseRouting();
        application.UseMiddleware<AuthResponseSecurityMiddleware>();
        application.MapRazorPages();
        application.MapEasyAuthEndpoints();
        application.MapEasyAuthProxy();
        return application;
    }
}
