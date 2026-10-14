using Microsoft.Extensions.DependencyInjection;
using RE.Core.Abstractions;
using RE.Core.Services;
using RE.Core.StaticAnalysis;
using RE.Infrastructure.AI;
using RE.Infrastructure.Persistence;

namespace RE.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<KnowledgeStore>();
        services.AddSingleton<IKnowledgeStore>(sp => sp.GetRequiredService<KnowledgeStore>());
        services.AddSingleton<OllamaClient>();
        services.AddSingleton<AntigravityLanguageServerClient>();
        services.AddSingleton(sp => AntigravityServerManager.Instance);
        services.AddSingleton<McpClientManager>();
        services.AddSingleton<IdaMcpService>();
        services.AddSingleton<BurpMcpService>();
        services.AddTransient<IDirectTriageSubprocessAnalyzer, DirectTriageSubprocessAnalyzer>();
        services.AddTransient<ITriagePipeline, TriageService>();
        services.AddTransient<TriageService>();
        services.AddTransient<ToolExecutionService>();
        services.AddTransient<RoutingService>();
        return services;
    }

    public static IServiceProvider UseTriageOrchestrator(this IServiceProvider provider)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        TriageOrchestrator.Configure(() => provider.GetRequiredService<ITriagePipeline>());
#pragma warning restore CS0618
        return provider;
    }
}