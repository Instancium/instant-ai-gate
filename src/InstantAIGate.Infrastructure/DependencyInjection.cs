using Microsoft.Extensions.DependencyInjection;
using System;

namespace InstantAIGate.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInstantAIGateInfrastructure(this IServiceCollection services)
        {
            return services.RegisterCoreServices();
        }

        private static IServiceCollection RegisterCoreServices(this IServiceCollection services)
        {
  
            return services;
        }
    }
}