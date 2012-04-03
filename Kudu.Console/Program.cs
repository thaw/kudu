﻿using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using Kudu.Core;
using Kudu.Core.Deployment;
using Kudu.Core.Infrastructure;
using Kudu.Core.SourceControl.Git;
using Kudu.Core.Tracing;

namespace Kudu.Console
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                System.Console.WriteLine("Usage: kudu.exe {appRoot} {wapTargets}");
                return 1;
            }

            System.Environment.SetEnvironmentVariable("GIT_DIR", null, System.EnvironmentVariableTarget.Process);

            var appRoot = args[0];
            var wapTargets = args[1];
            string nugetCachePath = null;

            IEnvironment env = GetEnvironment(appRoot, nugetCachePath);

            // Setup the trace
            string tracePath = Path.Combine(env.ApplicationRootPath, Constants.TracePath, Constants.TraceFile);
            var traceFactory = new TracerFactory(() => new Tracer(tracePath));

            // Calculate the lock path
            string lockPath = Path.Combine(env.ApplicationRootPath, Constants.LockPath);
            string deploymentLockPath = Path.Combine(lockPath, Constants.DeploymentLockFile);
            var deploymentLock = new LockFile(traceFactory, deploymentLockPath);

            var fs = new FileSystem();
            var buildPropertyProvider = new BuildPropertyProvider(wapTargets);
            var builderFactory = new SiteBuilderFactory(buildPropertyProvider, env);
            var serverRepository = new GitDeploymentRepository(env.DeploymentRepositoryPath, traceFactory);

            var logger = new ConsoleLogger();
            var deploymentManager = new DeploymentManager(serverRepository, 
                                                          builderFactory, 
                                                          env, 
                                                          fs, 
                                                          traceFactory, 
                                                          deploymentLock,
                                                          logger);

            try
            {
                deploymentManager.Deploy();
            }
            catch(System.Exception ex)
            {
                System.Console.Error.WriteLine(ex.Message);

                throw;
            }

            if (logger.HasErrors)
            {
                return 1;
            }

            return 0;
        }

        private static IEnvironment GetEnvironment(string root, string nugetCachePath)
        {
            string deployPath = Path.Combine(root, Constants.WebRoot);
            string deployCachePath = Path.Combine(root, Constants.DeploymentCachePath);
            string deploymentRepositoryPath = Path.Combine(root, Constants.RepositoryPath);
            string tempPath = Path.GetTempPath();
            string deploymentTempPath = Path.Combine(tempPath, Constants.RepositoryPath);

            return new Environment(root,
                                   tempPath,
                                   () => deploymentRepositoryPath,
                                   () => null,
                                   deployPath,
                                   deployCachePath,
                                   nugetCachePath);
        }

        private class BuildPropertyProvider : IBuildPropertyProvider
        {
            private readonly string _extensionsPath;

            public BuildPropertyProvider(string extensionsPath)
            {
                _extensionsPath = extensionsPath;
            }

            public IDictionary<string, string> GetProperties()
            {
                return new Dictionary<string, string> {
                    { "MSBuildExtensionsPath32", _extensionsPath }
                };
            }
        }

        private class ConsoleLogger : ILogger
        {
            public ILogger Log(string value, LogEntryType type)
            {
                if (type == LogEntryType.Error)
                {
                    HasErrors = true;

                    System.Console.Error.WriteLine(value);
                }
                else
                {
                    System.Console.WriteLine(value);
                }

                return NullLogger.Instance;
            }

            public bool HasErrors { get; set; }
        }
    }
}
