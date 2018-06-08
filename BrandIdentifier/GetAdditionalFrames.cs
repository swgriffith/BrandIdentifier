
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using System.Diagnostics;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace BrandIdentifier
{
    public static class GetAdditionalFrames
    {
        //CustomVision Settings
        static string predictionKey;
        static string customVizURL;

        [FunctionName("GetAdditionalFrames")]
        public static IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequest req,
            TraceWriter log,
            ExecutionContext context)
        {
            log.Info("Call invoked to get video frames.");

            GetSettings(context);

            string possition = WebUtility.UrlDecode(req.Query["possition"]);
            string fileName = WebUtility.UrlDecode(req.Query["filename"]);
            string vidurl = new StreamReader(req.Body).ReadToEnd();

            log.Info(Directory.GetCurrentDirectory());
            var psi = new ProcessStartInfo();
            psi.FileName = @".\ffmpeg\ffmpeg.exe";
            psi.Arguments = $"-i \"{vidurl}\" -ss {possition} -frames:v 15 {fileName}_%d.jpg";
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;

            log.Info($"Args: {psi.Arguments}");
            var process = Process.Start(psi);
            log.Info(process.StandardOutput.ReadToEnd());
            log.Info(process.StandardError.ReadToEnd());
            process.WaitForExit((int)TimeSpan.FromSeconds(60).TotalMilliseconds);

            List<frame> frameList = new List<frame>();

            for (int i = 1; i < 15; i++)
            {
                ReadFileAndAnalyze($"{fileName}_{i}.jpg");
                //frame fr = new frame(
            }

            if (possition != null && vidurl !=null)
            {
                return (ActionResult)new OkObjectResult($"possition={possition} vidurl={vidurl}");
            }
            else
            {
                return new BadRequestObjectResult("Please confirm the input possition and vidurl URL params are set.");
            }
        }

        private static string ReadFileAndAnalyze(string fileName)
        {
            // create the http client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Prediction-Key", predictionKey);
            // Get custom vision prediction
            HttpResponseMessage response;

            using (var content = new StreamContent(new FileStream(fileName, FileMode.Open, FileAccess.Read)))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                response = client.PostAsync(customVizURL, content).Result;
                var thumbResult = response.Content.ReadAsStringAsync().Result;

                return thumbResult;
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
            predictionKey = config["predictionKey"];
            customVizURL = config["customVizURL"];
        }
    }


    class frame
    {
        public string fileName { get; set; }
        public string timestamp { get; set; }
        public string framenum { get; set; }
        public string matchTag { get; set; }
        public string matchProb { get; set; }

    }
}
