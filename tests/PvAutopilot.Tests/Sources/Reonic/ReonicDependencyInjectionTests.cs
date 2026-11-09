using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Http;
using PvAutopilot.Core.Sources;
using PvAutopilot.Sources.Reonic;
using PvAutopilot.Sources.Reonic.Api;

namespace PvAutopilot.Tests.Sources.Reonic;

public class ReonicDependencyInjectionTests
{
    private static (ServiceProvider Provider, CapturingHandler Handler) Build(
        Dictionary<string, string?> settings, bool withApiKey = true)
    {
        if (withApiKey)
            settings.TryAdd("Reonic:ApiKey", "rnc_v3_test");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var handler = new CapturingHandler();
        var services = new ServiceCollection();
        services.AddReonic(configuration);
        services.AddHttpClient<ReonicApiClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        return (services.BuildServiceProvider(), handler);
    }

    [Fact]
    public async Task Client_uses_base_url_api_key_and_no_cache_header()
    {
        var (provider, handler) = Build(new()
        {
            ["Reonic:BaseUrl"] = "https://reonic.example/rest/v3/",
            ["Reonic:ApiKey"] = "rnc_v3_test",
        });
        using var _ = provider;

        await provider.GetRequiredService<ReonicApiClient>().GetProjectAsync("p1", default);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://reonic.example/rest/v3/residentialProjects/p1", request.RequestUri!.ToString());
        Assert.Equal("rnc_v3_test", Assert.Single(request.Headers.GetValues("X-Authorization")));
        Assert.Equal("no-cache", Assert.Single(request.Headers.GetValues("Reonic-Cache-Control")));
    }

    [Fact]
    public async Task Default_base_url_and_cache_bypass_can_be_disabled()
    {
        var (provider, handler) = Build(new() { ["Reonic:BypassCache"] = "false" });
        using var _ = provider;

        await provider.GetRequiredService<ReonicApiClient>().GetProjectAsync("p1", default);

        var request = Assert.Single(handler.Requests);
        Assert.StartsWith("https://api.reonic.com/rest/v3/", request.RequestUri!.ToString());
        Assert.False(request.Headers.Contains("Reonic-Cache-Control"));
    }

    [Fact]
    public async Task Rate_limit_is_retried_by_backoff_handler()
    {
        var (provider, handler) = Build(new() { ["Reonic:ApiKey"] = "rnc_v3_test" });
        using var _ = provider;
        handler.Responses.Enqueue(HttpStatusCode.TooManyRequests);
        handler.Responses.Enqueue(HttpStatusCode.NotFound);

        // Erster Retry wartet 2 s – akzeptabel für einen Test, der die echte Pipeline prüft.
        var project = await provider.GetRequiredService<ReonicApiClient>().GetProjectAsync("p1", default);

        Assert.Null(project);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void Planning_data_source_is_registered()
    {
        var (provider, _) = Build([]);
        using var __ = provider;
        using var scope = provider.CreateScope();

        Assert.IsType<ReonicPlanningDataSource>(scope.ServiceProvider.GetRequiredService<IPlanningDataSource>());
    }

    [Fact]
    public void Options_are_bound()
    {
        var (provider, _) = Build(new() { ["Reonic:ChecklistStage"] = "installation" });
        using var __ = provider;

        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ReonicOptions>>().Value;

        Assert.Equal("installation", options.ChecklistStage);
        Assert.True(options.BypassCache);
    }

    [Fact]
    public void Missing_api_key_fails_validation()
    {
        var (provider, _) = Build([], withApiKey: false);
        using var __ = provider;

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ReonicOptions>>().Value);
        Assert.Contains("Reonic:ApiKey", error.Message);
    }

    [Fact]
    public void Base_url_without_trailing_slash_fails_validation()
    {
        var (provider, _) = Build(new() { ["Reonic:BaseUrl"] = "https://reonic.example/rest/v3" });
        using var __ = provider;

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ReonicOptions>>().Value);
    }

    [Fact]
    public async Task Component_versions_may_come_from_reonic_cache()
    {
        var (provider, handler) = Build([]);
        using var _ = provider;

        await provider.GetRequiredService<ReonicApiClient>().GetComponentVersionAsync("c1", "v1", default);
        await provider.GetRequiredService<ReonicApiClient>().GetChecklistAsync("p1", "offer", default);

        Assert.False(handler.Requests[0].Headers.Contains("Reonic-Cache-Control"));
        Assert.Equal("no-cache", Assert.Single(handler.Requests[1].Headers.GetValues("Reonic-Cache-Control")));
    }

    [Fact]
    public void Http_timeout_covers_all_backoff_waits()
    {
        var (provider, _) = Build([]);
        using var _ = provider;

        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ReonicApiClient));

        Assert.True(http.Timeout > ExponentialBackoffHandler.MaxTotalDelay);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public Queue<HttpStatusCode> Responses { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var status = Responses.TryDequeue(out var s) ? s : HttpStatusCode.NotFound;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
