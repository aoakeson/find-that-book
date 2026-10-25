using FindThatBook.Core.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FindThatBook.Infrastructure.Explanations;

public static class ExplanationRegistration
{
    /// <summary>
    /// The facts-only explain writer over the shared <c>IStructuredLlm</c> (registered by <c>AddAi</c>, which also owns the
    /// "AI:Explain" timeout and token options). With no key the call returns NotConfigured and every item gets the template.
    /// </summary>
    public static IServiceCollection AddExplanationWriter(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IExplanationWriter, LlmExplanationWriter>();
        return services;
    }
}
