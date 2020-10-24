﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Impostor.Api.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Hosting;

namespace Impostor.Server.Plugins
{
    public static class PluginLoader
    {
        public static IHostBuilder UsePluginLoader(this IHostBuilder builder, PluginConfig config)
        {
            var assemblyInfos = new List<IAssemblyInformation>();
            var context = AssemblyLoadContext.Default;

            // Add the plugins and libraries.
            var pluginPaths = new List<string>(config.Paths);
            var libraryPaths = new List<string>(config.LibraryPaths);
            
            var rootFolder = AppContext.BaseDirectory;

            pluginPaths.Add(Path.Combine(rootFolder, "plugins"));
            libraryPaths.Add(Path.Combine(rootFolder, "libraries"));

            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude("*.dll");
            matcher.AddExclude("Impostor.Api.dll");

            RegisterAssemblies(pluginPaths, matcher, assemblyInfos, true);
            RegisterAssemblies(libraryPaths, matcher, assemblyInfos, false);

            // Register the resolver to the current context.
            // TODO: Move this to a new context so we can unload/reload plugins.
            context.Resolving += (loadContext, name) =>
            {
                var info = assemblyInfos.FirstOrDefault(a => a.AssemblyName.Name == name.Name);

                return info?.Load(loadContext);
            };

            // TODO: Catch uncaught exceptions.
            var assemblies = assemblyInfos
                .Where(a => a.IsPlugin)
                .Select(a => context.LoadFromAssemblyName(a.AssemblyName))
                .ToList();

            // Find all plugins.
            var plugins = new List<PluginInformation>();

            foreach (var assembly in assemblies)
            {
                // Find plugin startup.
                var pluginStartup = assembly
                    .GetTypes()
                    .Where(t => typeof(IPluginStartup).IsAssignableFrom(t) && t.IsClass)
                    .ToList();

                if (pluginStartup.Count > 1)
                {
                    throw new PluginLoaderException("A plugin may only define zero or one IPluginStartup implementation.");
                }

                // Find plugin.
                var plugin = assembly
                    .GetTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t)
                                && t.IsClass
                                && !t.IsAbstract
                                && t.GetCustomAttribute<ImpostorPluginAttribute>() != null)
                    .ToList();

                if (plugin.Count != 1)
                {
                    throw new PluginLoaderException("A plugin must define exactly one IPlugin or PluginBase implementation.");
                }

                // Save plugin.
                plugins.Add(new PluginInformation(
                    pluginStartup
                        .Select(Activator.CreateInstance)
                        .Cast<IPluginStartup>()
                        .FirstOrDefault(),
                    plugin.First()));
            }

            foreach (var plugin in plugins.Where(plugin => plugin.Startup != null))
            {
                plugin.Startup.ConfigureHost(builder);
            }

            builder.ConfigureServices(services =>
            {
                services.AddHostedService(provider => ActivatorUtilities.CreateInstance<PluginLoaderService>(provider, plugins));

                foreach (var plugin in plugins.Where(plugin => plugin.Startup != null))
                {
                    plugin.Startup.ConfigureServices(services);
                }
            });

            return builder;
        }

        private static void RegisterAssemblies(
            IEnumerable<string> paths,
            Matcher matcher,
            ICollection<IAssemblyInformation> assemblyInfos,
            bool isPlugin)
        {
            foreach (var path in paths.SelectMany(matcher.GetResultsInFullPath))
            {
                AssemblyName assemblyName;

                try
                {
                    assemblyName = AssemblyName.GetAssemblyName(path);
                }
                catch (BadImageFormatException)
                {
                    continue;
                }

                assemblyInfos.Add(new AssemblyInformation(assemblyName, path, isPlugin));
            }
        }
    }
}
