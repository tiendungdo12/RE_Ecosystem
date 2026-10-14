using Microsoft.Extensions.DependencyInjection;
using RE.Application.UseCases;

namespace RE.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<ConversationSearchService>();
        services.AddSingleton<ModelSelectionService>();
        services.AddSingleton<UsageSummaryService>();
        return services;
    }
}