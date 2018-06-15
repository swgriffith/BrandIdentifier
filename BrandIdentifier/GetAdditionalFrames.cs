
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
using System.Linq;
using System.ComponentModel;

namespace BrandIdentifier
{
    public static class GetAdditionalFrames
    {
        //CustomVision Settings
        static string predictionKey;
        static string customVizURL;
        static string downloadPath;
        static bool downloadComplete = false;

        [FunctionName("GetAdditionalFrames")]
        public static IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequest req,
            TraceWriter log,
            ExecutionContext context)
        { 
            log.Info("Call invoked to get video frames.");
            
            GetSettings(context);

            string position = WebUtility.UrlDecode(req.Query["position"]);
            string fileName = WebUtility.UrlDecode(req.Query["filename"]);
            bool isStart = Convert.ToBoolean(WebUtility.UrlDecode(req.Query["isstart"]));
            string vidurl = new StreamReader(req.Body).ReadToEnd();

            if (!File.Exists(fileName))
            {
                log.Info($"Downloading: {vidurl}");
                DownloadFile(vidurl, $"{downloadPath}\\{fileName}");
                do
                {
                } while (!downloadComplete);

                log.Info("Download complete.");
            }

            string brandSpecificTag = "brand_specific_end";
            if (isStart)
            {
                brandSpecificTag = "brand_specific_start";
            }

            string start = GetStartTime(position, isStart);
            log.Info(Directory.GetCurrentDirectory());
            var psi = new ProcessStartInfo();
            psi.FileName = @".\ffmpeg\ffmpeg.exe";
            psi.Arguments = $"-i \"{downloadPath}\\{ fileName}\" -ss {start} -frames:v 15 {fileName}_%d.jpg";
            //psi.RedirectStandardOutput = true;
            //psi.RedirectStandardError = true;
            psi.UseShellExecute = false;

            log.Info($"Args: {psi.Arguments}");
            var process = Process.Start(psi);
            //log.Info(process.StandardOutput.ReadToEnd());
            //log.Info(process.StandardError.ReadToEnd());
            //process.WaitForExit((int)TimeSpan.FromSeconds(300).TotalMilliseconds);
            process.WaitForExit();

            List<frame> frameList = new List<frame>();

            for (int i = 1; i < 15; i++)
            {
                var result = ReadFileAndAnalyze($"{fileName}_{i}.jpg");

                dynamic customVisionResults = JsonConvert.DeserializeObject(result);
                if (customVisionResults.predictions != null)
                {
                    foreach (var item in customVisionResults.predictions)
                    {
                        decimal probability = (decimal)item.probability;
                        if (probability >= .02m)
                        {
                            if (item.tagName == brandSpecificTag)
                            {
                                frameList.Add(new frame
                                {
                                    id = i,
                                    matchProb = probability
                                });
                            }

                        }

                    }
                }

                //log.Info(result);
            }

            //Assume that the initial position is best and then check results to refine
            string newTime = position;
            if (frameList.Count != 0)
            {
                var match = frameList.OrderByDescending(x => x.matchProb).First();

                if (isStart)
                {
                    newTime = GetNewTime(start, match.id - 3);
                }
                else
                {
                    newTime = GetNewTime(start, match.id + 3);

                }
            }

            //Cleaup Files
            if (!isStart)
            {
                CleanupFiles(fileName);
            }

            if (newTime != null && vidurl !=null)
            {
                return (ActionResult)new OkObjectResult(newTime);
            }
            else
            {
                return new BadRequestObjectResult("Please confirm the input position and vidurl URL params are set.");
            }
        }

        private static string GetNewTime(string startPos, int frame)
        {

            TimeSpan s = TimeSpan.Parse(startPos);
            double startFrame = (s.TotalSeconds * 25);
            double seconds = (startFrame + frame) / 25;
            TimeSpan final = TimeSpan.FromSeconds(seconds);
            return final.ToString("hh\\:mm\\:ss\\.fff");
        }

        private static string GetStartTime(string startPos ,bool isStart)
        {
            TimeSpan s = TimeSpan.Parse(startPos);

            double startFrame = (s.TotalSeconds * 25);
            if (isStart)
            {
                startFrame = startFrame - 15;
            }
            double seconds = startFrame / 25;
            TimeSpan final = TimeSpan.FromSeconds(seconds);
            return final.ToString("hh\\:mm\\:ss\\.fff");

        }

        private static void DownloadFile(string fileUri, string fileName)
        {
            WebClient myWebClient = new WebClient();
            myWebClient.DownloadFileCompleted += _downloadComplete;
            myWebClient.DownloadFileAsync(new Uri(fileUri), fileName);
        }
        private static void _downloadComplete(object sender, AsyncCompletedEventArgs e)
        {
            downloadComplete = true;
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
            downloadPath = config["downloadPath"];
        }

        static void CleanupFiles(string fileName)
        {
            //Delete the video file
            File.Delete(fileName);

            //Delete the thumbs
            string[] thumbList = Directory.GetFiles("./", "*.jpg");
            foreach (string file in thumbList)
            {
                File.Delete(file);
            }
        }
    }


    class frame
    {
        public decimal matchProb { get; set; }

        public int id { get; set; }

    }
}
