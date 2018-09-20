using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.SignalRService;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace ServerlessTrivia
{
    public static class Functions
    {
        private const string containerName = "orlando";

        [FunctionName(nameof(FlightsOrchestrator))]
        public static async Task FlightsOrchestrator(
            [OrchestrationTrigger] DurableOrchestrationContext context,
            ILogger logger)
        {
            const int maxIterations = 480;
            var iteration = context.GetInput<int>();
            if (iteration >= maxIterations)
            {
                iteration = 0;
            }

            await context.CallActivityAsync(nameof(GetAndSendData), new IterationInput
            {
                Iteration = iteration,
                DataFileName = $"{containerName}/{iteration}.json"
            });

            var waitUntil = context.CurrentUtcDateTime.AddMilliseconds(1000);
            await context.CreateTimer(waitUntil, CancellationToken.None);

            if (iteration < maxIterations) 
            {
                context.ContinueAsNew(iteration + 1);
            }
        }

        [FunctionName(nameof(GetAndSendData))]
        public static async Task GetAndSendData(
            [ActivityTrigger] DurableActivityContext context,
            [SignalR(HubName = "flights")] IAsyncCollector<SignalRMessage> signalRHub,
            ILogger logger)
        {
            var input = context.GetInput<IterationInput>();

            var storageAccount = CloudStorageAccount.Parse(
                Environment.GetEnvironmentVariable("AzureWebJobsStorage", EnvironmentVariableTarget.Process));
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(containerName);
            var blob = container.GetBlobReference($"{input.Iteration}.json");
            using (var blobStream = await blob.OpenReadAsync())
            using (var streamReader = new StreamReader(blobStream))
            {
                var jsonTextReader = new JsonTextReader(streamReader);
                var data = JsonSerializer.CreateDefault().Deserialize<Data>(jsonTextReader);

                var flightData = new
                {
                    time = data.Time,
                    flights = data.States
                        .Select(f => new
                        {
                            id = f[0],
                            callsign = f[1],
                            longitude = f[5],
                            latitude = f[6],
                            altitude = f[7],
                            heading = f[10]
                        })
                };

                await signalRHub.AddAsync(new SignalRMessage
                {
                    Target = "newFlightData",
                    Arguments = new[] { flightData }
                });
            }
        }

        [FunctionName(nameof(HttpStartSingle))]
        public static async Task<HttpResponseMessage> HttpStartSingle(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestMessage req,
            [OrchestrationClient] DurableOrchestrationClient starter,
            TraceWriter log)
        {
            const string instanceId = "1";
            // Check if an instance with the specified ID already exists.
            var existingInstance = await starter.GetStatusAsync(instanceId);
            if (existingInstance == null)
            {
                await starter.StartNewAsync(nameof(FlightsOrchestrator), instanceId);
                log.Info($"Started orchestration with ID = '{instanceId}'.");
                return starter.CreateCheckStatusResponse(req, instanceId);
            }
            else
            {
                // An instance with the specified ID exists, don't create one.
                return req.CreateErrorResponse(
                    HttpStatusCode.Conflict,
                    $"An instance with ID '{instanceId}' already exists.");
            }
        }

        [FunctionName(nameof(SignalRInfo))]
        public static IActionResult SignalRInfo(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")]HttpRequestMessage req,
            [SignalRConnectionInfo(HubName = "flights")]SignalRConnectionInfo info,
            ILogger logger)
        {
            return info != null
                ? (ActionResult)new OkObjectResult(info)
                : new NotFoundObjectResult("Failed to load SignalR Info.");
        }
    }

    public class IterationInput
    {
        public int Iteration { get; set; }
        public string DataFileName { get; set; }
    }

    public class Data
    {
        public int Time { get; set; }
        public List<List<object>> States { get; set; } = new List<List<object>>();
    }
}