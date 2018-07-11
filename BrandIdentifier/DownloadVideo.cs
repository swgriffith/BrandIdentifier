
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using System.Net;
using System;
using Microsoft.Extensions.Configuration;
using System.ComponentModel;
using System.Net.Http;
using System.Threading.Tasks;

namespace BrandIdentifier
{
    public static class DownloadVideo
    {
        static string downloadPath;
        static bool downloadComplete = false;

        [FunctionName("DownloadVideo")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log, ExecutionContext context)
        {
            log.Info("Video File Download started.");
            GetSettings(context);

            var o = await req.Content.ReadAsAsync<ExtractProcessData>();

            DownloadFile(o.url, o.filename, log);

            if (downloadComplete)
            {
                return (ActionResult)new OkObjectResult($"Download Complete");
            } 
            else
            {
                return new BadRequestObjectResult("Please pass a name on the query string or in the request body");
            }
        }

        /// <summary>
        /// Loads all of the settings needed for the api calls to Video Indexer and Custom Vision
        /// </summary>
        static void GetSettings(ExecutionContext context)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            downloadPath = config["downloadPath"];
        }

        private static void DownloadFile(string fileUri, string fileName, TraceWriter log)
        {
            WebClient myWebClient = new WebClient();
            log.Info("Writing to: " + fileName);
            myWebClient.DownloadFile(new Uri(fileUri), Path.Combine(downloadPath, fileName));
            downloadComplete = true;
            //myWebClient.DownloadFileCompleted += _downloadComplete;
        }

        private static void _downloadComplete(object sender, AsyncCompletedEventArgs e)
        {
            downloadComplete = true;
        }
    }
}
