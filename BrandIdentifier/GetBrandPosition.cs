
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;

namespace BrandIdentifier2
{
    public static class GetBrandPosition
    {
        //Video Indexer Settings
        static string apiUrl;
        static string accountId;
        static string location;
        static string apiKey;
        //CustomVision Settings
        static string predictionKey;
        static string customVizURL;

        [FunctionName("GetBrandPosition")]
        public static IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequest req, TraceWriter log)
        {
            try
            {

           log.Info("C# HTTP trigger function processed a request.");

            string videoId = req.Query["videoID"];

            //string requestBody = new StreamReader(req.Body).ReadToEnd();
            //dynamic data = JsonConvert.DeserializeObject(requestBody);
            //name = name ?? data?.name;

            //return name != null
            //    ? (ActionResult)new OkObjectResult($"Hello, {name}")
            //    : new BadRequestObjectResult("Please pass a name on the query string or in the request body");

            //Load settings
            GetSettings();

            //TODO: MOve to an input param
            //var videoId = "0267c3c749";

            //set the TLS level used
            System.Net.ServicePointManager.SecurityProtocol = System.Net.ServicePointManager.SecurityProtocol | System.Net.SecurityProtocolType.Tls12;

            // create the http client
            var handler = new HttpClientHandler();
            handler.AllowAutoRedirect = false;
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);

            // obtain video access token used in subsequent calls
            var videoAccessTokenRequestResult = client.GetAsync($"{apiUrl}/auth/{location}/Accounts/{accountId}/Videos/{videoId}/AccessToken").Result;
            var videoAccessToken = videoAccessTokenRequestResult.Content.ReadAsStringAsync().Result.Replace("\"", "");

            client.DefaultRequestHeaders.Remove("Ocp-Apim-Subscription-Key");

            // Get video details
            var videoRequestResult = client.GetAsync($"{apiUrl}/{location}/Accounts/{accountId}/Videos/{videoId}/Index?accessToken={videoAccessToken}").Result;
            var videoResult = videoRequestResult.Content.ReadAsStringAsync().Result;

            dynamic videoDetails = JsonConvert.DeserializeObject(videoResult);

            //Iterate through the results to extract the key frames and thumbnail images to build a list of the thumbs to be analyzed
            List<thumb> thumbs = new List<thumb>();
            foreach (var item in videoDetails.videos)
            {
                foreach (var shot in item.insights.shots)
                {
                    foreach (var keyFrame in shot.keyFrames)
                    {
                        foreach (var instance in keyFrame.instances)
                        {
                                //Console.WriteLine(string.Format("{0} : {1} : {2} : {3} : {4}", shot.id, keyFrame.id, instance.thumbnailId, instance.start, instance.end));
                                thumbs.Add(new thumb
                                {
                                    thumbId = instance.thumbnailId,
                                    image = GetThumb(instance.thumbnailId.ToString(), videoId, videoAccessToken),
                                    start = (DateTime)instance.start,
                                end = (DateTime)instance.end
                            });
                        }

                    }
                }
            }

            //Invoke the matchThumbs method to run thumbs against custom vision
            List<thumb> predictions = matchThumbs(thumbs);

            //Write results
            foreach (var prediction in predictions)
            {
                log.Info(string.Format("Thumb: {0} - {1} - {2} Start: {3}  End: {4}", prediction.thumbId, prediction.match, prediction.probability,
                     prediction.start.ToString("HH:mm:ss.ffff"), prediction.end.ToString("HH:mm:ss.ffff")));
            }

            startAndEnd startAndEndFrames = GetStartAndEndFrames(predictions);

            string responseMsg = string.Format("StartFrame: {0} at {1}", startAndEndFrames.startFrame.thumbId, startAndEndFrames.startFrame.start.ToString("HH:mm:ss.ffff")) + "  "
                + string.Format("EndFrame: {0} at {1}", startAndEndFrames.endFrame.thumbId, startAndEndFrames.endFrame.end.ToString("HH:mm:ss.ffff"));
            log.Info(responseMsg);

            return videoDetails != null
                ? (ActionResult)new OkObjectResult(responseMsg)
                : new BadRequestObjectResult("Please pass a video Id on the query string");

            }
            catch (Exception ex)
            {
                log.Info(ex.InnerException.Message);
                return new BadRequestObjectResult(ex.InnerException.Message);
            }

        }

        /// <summary>
        /// Loads all of the settings needed for the api calls to Video Indexer and Custom Vision
        /// </summary>
        static void GetSettings()
        {
            apiUrl = Environment.GetEnvironmentVariable("apiUrl");
            accountId = Environment.GetEnvironmentVariable("viAccountID");
            location = Environment.GetEnvironmentVariable("viRegion");
            apiKey = Environment.GetEnvironmentVariable("viAPIKey");
            predictionKey = Environment.GetEnvironmentVariable("predictionKey");
            customVizURL = Environment.GetEnvironmentVariable("customVizURL");
        }

        static Stream GetThumb(string thumbId, string videoId, string accessToken)
        {
            // create the http client
            var handler = new HttpClientHandler();
            handler.AllowAutoRedirect = false;
            var client = new HttpClient(handler);

            // Get thumb
            var thumbRequestResult = client.GetAsync($"{apiUrl}/{location}/Accounts/{accountId}/Videos/{videoId}/Thumbnails/{thumbId}?accessToken={accessToken}").Result;
            Stream thumbResult = thumbRequestResult.Content.ReadAsStreamAsync().Result;

            return thumbResult;
        }

        /// <summary>
        /// Call custom vision API and return the list of matched thumbs with predictions
        /// </summary>
        /// <param name="thumbs">List of thumb objects</param>
        /// <returns>List of matched thumb objects</returns>
        static List<thumb> matchThumbs(List<thumb> thumbs)
        {
            List<thumb> results = new List<thumb>();

            // create the http client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Prediction-Key", predictionKey);

            foreach (var thumb in thumbs)
            {
                // Get custom vision prediction
                HttpResponseMessage response;
                thumb.image.Position = 0;
                using (var content = new StreamContent(thumb.image))
                {
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    response = client.PostAsync(customVizURL, content).Result;
                    var thumbResult = response.Content.ReadAsStringAsync().Result;
                    CultureInfo provider = CultureInfo.InstalledUICulture;
                    string timeFormat = "HH:mm:ss.ffffff";

                    dynamic customVisionResults = JsonConvert.DeserializeObject(thumbResult);
                    if (customVisionResults.predictions != null)
                    {
                        foreach (var item in customVisionResults.predictions)
                        {
                            decimal probability = (decimal)item.probability;
                                if (probability >= .02m)
                                {
                                    if (item.tagName !="blankscreen")
                                    {
                                        results.Add(new thumb
                                        {
                                            thumbId = thumb.thumbId,
                                            probability = probability,
                                            match = item.tagName,
                                            start = thumb.start,
                                            end = thumb.end
                                        });
                                    }
                                    
                                }

                        }
                    }
                }

                //TODO: Need to slow downt he process to avoid CustomeVision throttling. Should remove this later.
                System.Threading.Thread.Sleep(1000);
            }
            return results;

        }

        static startAndEnd GetStartAndEndFrames(List<thumb> input)
        {
            startAndEnd output = new startAndEnd();

            output.startFrame = input.OrderBy(a => a.start).First();
            output.endFrame = input.OrderBy(a => a.end).Last();

            return output;
        }
    }

    class thumb
    {
        public string thumbId { get; set; }
        public Stream image { get; set; }
        public string match { get; set; }
        public decimal probability { get; set; }
        public DateTime start { get; set; }
        public DateTime end { get; set; }
    }

    class startAndEnd
    {
        public thumb startFrame { get; set; }
        public thumb endFrame { get; set; }

    }
}
