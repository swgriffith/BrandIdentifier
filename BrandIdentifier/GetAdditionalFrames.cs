
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
using BrandIdentifier2;

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
            
            bool isStart = Convert.ToBoolean(WebUtility.UrlDecode(req.Query["isstart"]));
            string json = new StreamReader(req.Body).ReadToEnd();

            var o = JsonConvert.DeserializeObject<AddtlStartAndEndOutput>(json);
            
            string filePath = Path.Combine(downloadPath, o.fileName);
            log.Info(filePath);
            if (!File.Exists(filePath))
            {
                return new BadRequestObjectResult("File not found on server. Please call the Download Function.");
                //log.Info($"Downloading: {vidurl}");
                //DownloadFile(vidurl, $"{downloadPath}\\{fileName}");
                //do
                //{
                //} while (!downloadComplete);

                //log.Info("Download complete.");
            }


            string newStartTime = ProcessFiles(o.fileName, o.startTime, true, log);
            string newEndTime = ProcessFiles(o.fileName, o.endTime, false, log);

            o.startTime = DateTime.Parse(newStartTime).TimeOfDay.ToString();
            o.endTime = DateTime.Parse(newEndTime).TimeOfDay.ToString();

            return (ActionResult)new OkObjectResult(o);
            
        }

        private static string ProcessFiles(string fileName, string position, bool isStart, TraceWriter log)
        {
            string brandSpecificTag = "brand_specific_end";
            if (isStart)
            {
                brandSpecificTag = "brand_specific_start";
            }

            string start = GetStartTime(position, isStart);
            log.Info(Directory.GetCurrentDirectory());
            var psi = new ProcessStartInfo();
            psi.FileName = Path.Combine(downloadPath, "ffmpeg", "ffmpeg.exe");
            
            log.Info(psi.FileName);
            psi.Arguments = $"-i \"{downloadPath}\\{ fileName}\" -ss {start} -frames:v 15 \"{downloadPath}\\{fileName}_%d.jpg\"";
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
                string outputFile = Path.Combine(downloadPath, fileName + "_" + i + ".jpg");
                var result = ReadFileAndAnalyze(outputFile);

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
            return newTime;
        }

        private static string GetNewTime(string startPos, int frame)
        {

            TimeSpan s = DateTime.Parse(startPos).TimeOfDay;
            double startFrame = (s.TotalSeconds * 25);
            double seconds = (startFrame + frame) / 25;
            TimeSpan final = TimeSpan.FromSeconds(seconds);
            return final.ToString("hh\\:mm\\:ss\\.fff");
        }

        private static string GetStartTime(string startPos ,bool isStart)
        {
            TimeSpan s = DateTime.Parse(startPos).TimeOfDay;

            double startFrame = (s.TotalSeconds * 25);
            if (isStart)
            {
                startFrame = startFrame - 15;
            }
            double seconds = startFrame / 25;
            TimeSpan final = TimeSpan.FromSeconds(seconds);
            return final.ToString("hh\\:mm\\:ss\\.fff");

        }

        //private static void DownloadFile(string fileUri, string fileName)
        //{
        //    WebClient myWebClient = new WebClient();
        //    myWebClient.DownloadFileCompleted += _downloadComplete;
        //    myWebClient.DownloadFileAsync(new Uri(fileUri), fileName);
        //}
        //private static void _downloadComplete(object sender, AsyncCompletedEventArgs e)
        //{
        //    downloadComplete = true;
        //}

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
            string vidFile = Path.Combine(downloadPath, fileName);
            //Delete the video file
            File.Delete(vidFile);

            //Delete the thumbs
            string[] thumbList = Directory.GetFiles(downloadPath, "*.jpg");
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
