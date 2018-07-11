
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace BrandIdentifier
{
    public static class UploadVideoFunc
    {
        [FunctionName("UploadVideo")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "post")]HttpRequestMessage req,
            [OrchestrationClient]DurableOrchestrationClient starter,
            TraceWriter log,
            Microsoft.Azure.WebJobs.ExecutionContext context)
        {
            
            // Function input comes from the request content.
            ProcessRequest requestData = await req.Content.ReadAsAsync<ProcessRequest>();

            //Get the config settings and add them to the requestData object
            requestData.config = GetSettings(context);            

            // Starting a new orchestrator with request data
            string instanceId = await starter.StartNewAsync("UploadVideo_Orchestrator", requestData);

            log.Info($"Started orchestration with ID = '{instanceId}'.");

            var response = starter.CreateCheckStatusResponse(req, instanceId);

            // I specify a response interval so the Logic App doesn't check the status
            // until after 10 seconds has passed. If work will be longer you can change this 
            // value as needed.
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(10));
            return response;
        }

        [FunctionName("UploadVideo_Orchestrator")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            var outputs = new List<string>();

            // In this case my orchestrator is only calling a single function - UploadVideo_DoWork
            outputs.Add(await context.CallActivityAsync<string>("UploadVideo_DoWork", context.GetInput<ProcessRequest>()));

            return outputs;
        }

        [FunctionName("UploadVideo_DoWork")]
        public static string DoWork([ActivityTrigger] ProcessRequest requestData, TraceWriter log)
        {
            log.Info($"Initiating Upload of {requestData.name}.");
            
            // create the http client
            var handler = new HttpClientHandler();
            handler.AllowAutoRedirect = false;
            var client = new HttpClient(handler);

            // Submit Upload
            string url = $"{requestData.config.apiUrl}/{requestData.config.location}/Accounts/{requestData.config.accountId}/Videos?accessToken={requestData.accessToken}&name={requestData.name}&videoUrl={WebUtility.UrlEncode(requestData.fileURL)}";
            var videoUploadRequestResult = client.PostAsync(url, null).Result;

            var responseData = videoUploadRequestResult.Content.ReadAsStringAsync().Result;
            dynamic videoDetails = JsonConvert.DeserializeObject(responseData);

            output vidData = new output();
            vidData.videoId = videoDetails.id;

            return vidData.ToString();
        }



        /// <summary>
        /// Loads all of the settings needed for the api calls to Video Indexer and Custom Vision
        /// </summary>
        static ConfigData GetSettings(Microsoft.Azure.WebJobs.ExecutionContext context)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            ConfigData settings = new ConfigData();
            settings.apiUrl = config["viApiUrl"];
            settings.accountId = config["viAccountID"];
            settings.location = config["viRegion"];
            settings.apiKey = config["viAPIKey"];

            return settings;
        }

        public class ProcessRequest
        {
            public string data { get; set; }
            public string accessToken { get; set; }
            public string name { get; set; }
            public string externalid { get; set; }
            public string fileURL { get; set; }
            public ConfigData config { get; set; }
        }

        public class ConfigData
        {
            public string apiUrl { get; set; }
            public string accountId { get; set; }
            public string location { get; set; }
            public string apiKey { get; set; }
        }

        public class output
        {
            public string videoId { get; set; }
        }


    }
}
