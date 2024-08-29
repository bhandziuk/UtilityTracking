using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UtilityTracking
{
    public static class StartupExtensions
    {
        private static readonly System.Reflection.MethodInfo OptionsConfigurationServiceCollectionExtensions_Configure =
            typeof(OptionsConfigurationServiceCollectionExtensions)
            .GetMethod(
                nameof(OptionsConfigurationServiceCollectionExtensions.Configure),
                1,
                [typeof(IServiceCollection), typeof(IConfiguration)]
            )!;

        public static void ConfigureRequiredSettings(this IServiceCollection services, IConfiguration configuration, params Type[] settingsImplementationTypes)
        {
            foreach (var implementation in settingsImplementationTypes)
            {
                //var name = implementation.Name[..^"Settings".Length];
                IConfigurationSection section = configuration.GetRequiredSection(implementation.Name);
                var instance = section.Get(implementation)!;
                var service = implementation.GetInterfaces().Where(o => o.BaseType == implementation).SingleOrDefault() ?? implementation;
                services.AddSingleton(service, instance);
            }
        }

        public static void ConfigureOptions(this IServiceCollection services, IConfiguration configuration, params Type[] optionsTypes)
        {
            foreach (var type in optionsTypes)
            {
                var name = type.Name[..^"Options".Length];
                IConfigurationSection section = configuration.GetSection(name);
                var configure = OptionsConfigurationServiceCollectionExtensions_Configure.MakeGenericMethod(type);
                configure.Invoke(null, parameters: [services, section]);
            }
        }
    }
}
