// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Common;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Delegation;
using Yarp.ReverseProxy.Forwarder;

namespace Yarp.ReverseProxy;

public partial class HttpSysDelegationTests
{
    [HttpSysDelegationFact]
    public async Task RequestDelegated()
    {
        IForwarderErrorFeature proxyError = null;
        Exception unhandledError = null;
        var expectedRepsone = "Hello World!";
        var queueName = nameof(HttpSysDelegationTests) + Random.Shared.Next().ToString("x8");

        var test = new HttpSysTestEnvironment(
            destinationServices => { },
            destinationHttpSysOptions => destinationHttpSysOptions.RequestQueueName = queueName,
            destinationApp => destinationApp.Run(context => context.Response.WriteAsync(expectedRepsone)),
            proxyServices => { },
            proxyBuilder => { },
            proxyApp =>
            {
                proxyApp.Use(async (context, next) =>
                {
                    try
                    {
                        await next();
                        proxyError = context.Features.Get<IForwarderErrorFeature>();
                    }
                    catch (Exception ex)
                    {
                        unhandledError = ex;
                        throw;
                    }
                });
            },
            proxyPipeline =>
            {
                proxyPipeline.UseHttpSysDelegation();
            },
            (cluster, route) =>
            {
                var destination = new DestinationConfig()
                {
                    Address = cluster.Destinations.First().Value.Address,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { DelegationExtensions.HttpSysDelegationQueueMetadataKey, queueName },
                    },
                };

                cluster = new ClusterConfig
                {
                    ClusterId = cluster.ClusterId,
                    Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "destination1",  destination },
                    },
                };

                return (cluster, route);
            });

        await test.Invoke(async proxyUri =>
        {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetStringAsync(proxyUri);

            Assert.Null(proxyError);
            Assert.Null(unhandledError);
            Assert.Equal(expectedRepsone, response);
        });
    }

    private class HttpSysDelegationFactAttribute : FactAttribute
    {
        public HttpSysDelegationFactAttribute()
        {
            // Htty.sys delegation was added to Windows in the 21H2 release but back ported through RS5 (1809)
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 1809))
            {
                Skip = "Http.sys tests are only supported on Windows versions >= 10.0.1809";
            }
        }
    }
}
#endif
