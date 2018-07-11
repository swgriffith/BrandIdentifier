using System;
using System.Collections.Generic;
using System.Configuration;
using System.Dynamic;
using System.Net.Http;
using System.Threading.Tasks;
using BrandIdentifier2;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace BrandIdentifier
{
    public static class DurableProcessor
    {
        private static string getaddtlframes_secret;
        private static string downloadvideo_secret;
        private static string getbrandposition_secret;

        [FunctionName("DurableVideoProcessor")]
        public static async Task<FinalResult> RunOrchestrator(
            [OrchestrationTrigger] DurableOrchestrationContext context)
            {

            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();
            downloadvideo_secret = config["downloadvideo_secret"];
            getbrandposition_secret = config["getbrandposition_secret"];
            getaddtlframes_secret = config["getaddtlframes_secret"];


            // Replace "hello" with the name of your Durable Activity Function.
            var x = await context.CallActivityAsync<bool>("Durable_DownloadVideo", context.GetInput<ExtractProcessData>());
            var y = await context.CallActivityAsync<ExtractProcessData>("Durable_GetBrandPosition", context.GetInput<ExtractProcessData>());
            return await context.CallActivityAsync<FinalResult>("Durable_GetAddtlFrames", y);
            
            
        }

        [FunctionName("Durable_DownloadVideo")]
        public static async Task<bool> Durable_DownloadVideo([ActivityTrigger] ExtractProcessData requestData, TraceWriter log)
        {
            log.Info($"Triggering DownloadVideo");

            using (var client = new HttpClient())
            {
                
                Uri anotherFunctionUri = new Uri(requestData.reqUri.Replace(requestData.PathAndQuery, "/api/DownloadVideo/?code=" + downloadvideo_secret));

                var responseFromAnotherFunction = await client.PostAsJsonAsync(anotherFunctionUri, requestData);
                // process the response
                return responseFromAnotherFunction.IsSuccessStatusCode;
            }
        }

        [FunctionName("Durable_GetBrandPosition")]
        public static async Task<ExtractProcessData> Durable_GetBrandPosition([ActivityTrigger] ExtractProcessData requestData, TraceWriter log)
        {
            log.Info($"Triggering GetBrandPosition");

            using (var client = new HttpClient())
            {
                
                Uri anotherFunctionUri = new Uri(requestData.reqUri.Replace(requestData.PathAndQuery, "/api/GetBrandPosition/"+requestData.videoid+"/?code=" + getbrandposition_secret));

                var responseFromAnotherFunction = await client.PostAsJsonAsync(anotherFunctionUri, requestData);
                // process the response
                var result = await responseFromAnotherFunction.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<ExtractProcessData>(result);
            }
        }

        [FunctionName("Durable_GetAddtlFrames")]
        public static async Task<FinalResult> Durable_GetAddtlFrames([ActivityTrigger] ExtractProcessData requestData, TraceWriter log)
        {
            log.Info($"Triggering GetAddtlFrames");

            using (var client = new HttpClient())
            {
                
                Uri anotherFunctionUri = new Uri(requestData.reqUri.Replace(requestData.PathAndQuery, "/api/GetAdditionalFrames/?code=" + getaddtlframes_secret));

                var responseFromAnotherFunction = await client.PostAsJsonAsync(anotherFunctionUri, requestData);
                // process the response
                var result = await responseFromAnotherFunction.Content.ReadAsStringAsync();
                var initresult = JsonConvert.DeserializeObject<ExtractProcessData>(result);
                var finalResult = new FinalResult() { filename = initresult.filename, endtime = initresult.endTime, starttime = initresult.startTime };
                return finalResult;
               
            }
        }


        [FunctionName("VideoProcess_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")]HttpRequestMessage req,
            [OrchestrationClient]DurableOrchestrationClient starter,
            TraceWriter log)
        {
            var requestData = await req.Content.ReadAsAsync<ExtractProcessData>();

            requestData.reqUri = req.RequestUri.AbsoluteUri.ToString();
            requestData.PathAndQuery = req.RequestUri.PathAndQuery;
            // Function input comes from the request content.

            string instanceId = await starter.StartNewAsync("DurableVideoProcessor", requestData);

            log.Info($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }

       
    }

    public class FinalResult
    {
        public string filename { get; set; }

        public string starttime { get; set; }

        public string endtime { get; set; }
    }
}