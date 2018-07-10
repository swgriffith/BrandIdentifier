
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

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
        //Frame refine values
        static string vidSASURL;
        static string frameRefineFuncURL;
        static string storageConnectionString;

        static bool downloadComplete = false;

        [FunctionName("GetBrandPosition")]
        public static IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "GetBrandPosition/{videoID}")]HttpRequest req,
            string videoId,
            Binder binder,
            TraceWriter log, 
            ExecutionContext context)
        {
            try
            {
                log.Info("Brand position request recieved.");
                log.Info(string.Format("videoID: {0}", videoId));

                GetSettings(context);
                log.Info("Retrieved Settings");

                //Pull the video blob SAS for later use
                vidSASURL = new StreamReader(req.Body).ReadToEnd();

                //set the TLS level used
                System.Net.ServicePointManager.SecurityProtocol = System.Net.ServicePointManager.SecurityProtocol | System.Net.SecurityProtocolType.Tls12;

                // create the http client
                var handler = new HttpClientHandler();
                handler.AllowAutoRedirect = false;
                var client = new HttpClient(handler);
                client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);

                // obtain video access token used in subsequent calls
                log.Info($"{apiUrl}/auth/{location}/Accounts/{accountId}/Videos/{videoId}/AccessToken");
                var videoAccessTokenRequestResult = client.GetAsync($"{apiUrl}/auth/{location}/Accounts/{accountId}/Videos/{videoId}/AccessToken").Result;
                var videoAccessToken = videoAccessTokenRequestResult.Content.ReadAsStringAsync().Result.Replace("\"", "");

               client.DefaultRequestHeaders.Remove("Ocp-Apim-Subscription-Key");

                // Get video details
                var videoRequestResult = client.GetAsync($"{apiUrl}/{location}/Accounts/{accountId}/Videos/{videoId}/Index?accessToken={videoAccessToken}").Result;
                var videoResult = videoRequestResult.Content.ReadAsStringAsync().Result;

                dynamic videoDetails = JsonConvert.DeserializeObject(videoResult);

                // Get video download URL
                var videoDownloadURLResult = client.GetAsync($"{apiUrl}/{location}/Accounts/{accountId}/Videos/{videoId}/SourceFile/DownloadUrl?accessToken={videoAccessToken}").Result;
                string videoDowloadURL = JsonConvert.DeserializeObject(videoDownloadURLResult.Content.ReadAsStringAsync().Result).ToString();

                //log.Info("Download Starting");
                //DownloadFile(videoDowloadURL, videoDetails.name.ToString());
                
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

                log.Info("Pulled Thumbs");

                //Invoke the matchThumbs method to run thumbs against custom vision
                List<thumb> predictions = matchThumbs(thumbs, log);
                log.Info("Made Predictions");
                
                //Write results
                foreach (thumb prediction in predictions)
                {
                    log.Info(message: $"Thumb: {prediction.thumbId} - {prediction.match} - {prediction.probability} Start: {prediction.start.ToString("HH:mm:ss.ffff")}  End: {prediction.end.ToString("HH:mm:ss.ffff")}");
                }

                startAndEndOutput startAndEndFrames = GetStartAndEndFrames(predictions, videoDetails.name.ToString(), log);
                
                string responseMsg = string.Format("StartFrame: {0}", startAndEndFrames.startTime) + "  "
                    + string.Format("EndFrame: {0}", startAndEndFrames.endTime);
                log.Info(responseMsg);

                WriteReadData("tempresults", startAndEndFrames.fileName+"_brandpos.json", JsonConvert.SerializeObject(startAndEndFrames));
                

                //Write output to HTTP as well
                return videoDetails != null
                ? (ActionResult)new OkObjectResult(JsonConvert.SerializeObject(startAndEndFrames))
                : new BadRequestObjectResult("Please pass a video Id on the query string");

            }
            catch (Exception ex)
            {
                log.Info(ex.InnerException.Message);
                return new BadRequestObjectResult(ex.InnerException.Message);
            }

        }

        private static async Task<string> WriteReadData(string containerName, string filename, string text)
        {
            // Retrieve storage account from connection string.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnectionString);

            // Create the blob client.
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            // Retrieve reference to a previously created container.
            CloudBlobContainer container = blobClient.GetContainerReference(containerName);

            // Retrieve reference to a blob named "filename"
            CloudBlockBlob blockBlob2 = container.GetBlockBlobReference(filename);

            await blockBlob2.UploadTextAsync(text);

            return "done";

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
            apiUrl = config["viApiUrl"];
            accountId = config["viAccountID"];
            location = config["viRegion"];
            apiKey = config["viAPIKey"];
            predictionKey = config["predictionKey"];
            customVizURL = config["customVizURL"];
            frameRefineFuncURL = config["frameRefineFuncURL"];
            storageConnectionString = config["storageConnectionString"];
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

        static Stream GetThumb(string thumbId, string videoId, string accessToken)
        {
            // create the http client
            var handler = new HttpClientHandler();
            handler.AllowAutoRedirect = false;
            var client = new HttpClient(handler);

            // Get thumb
            var thumbRequestResult = client.GetAsync($"{apiUrl}/{location}/Accounts/{accountId}/Videos/{videoId}/Thumbnails/{thumbId}?accessToken={accessToken}").Result;
            Stream thumbResult = thumbRequestResult.Content.ReadAsStreamAsync().Result;

            //Added the following for local testing purposes to output the thumbs to a local folder for inspection
            //WriteFile(thumbResult, thumbId);

            return thumbResult;
        }

        /// <summary>
        /// Call custom vision API and return the list of matched thumbs with predictions
        /// </summary>
        /// <param name="thumbs">List of thumb objects</param>
        /// <returns>List of matched thumb objects</returns>
        static List<thumb> matchThumbs(List<thumb> thumbs, TraceWriter log)
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

                    dynamic customVisionResults = JsonConvert.DeserializeObject(thumbResult);
                    if (customVisionResults.predictions != null)
                    {
                        foreach (var item in customVisionResults.predictions)
                        {
                            decimal probability = (decimal)item.probability;
                                if (probability >= .5m)
                                {
                                    if (item.tagName == "brandstartstop")
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

                //TODO: Need to slow downt he process to avoid CustomVision throttling. Should remove this later.
                System.Threading.Thread.Sleep(1000);
            }
            return results;

        }

        static void WriteFile(Stream file, string fileName)
        {
            using (var fileStream = File.Create(string.Format("{0}.jpg", fileName)))
            {
                file.Seek(0, SeekOrigin.Begin);
                file.CopyTo(fileStream);
            }
        }

        static startAndEndOutput GetStartAndEndFrames(List<thumb> input, string fileName, TraceWriter log)
        {
            startAndEndOutput output = new startAndEndOutput();

            ////Wait for download to complete
            //log.Info("Waiting for Download to Complete");
            //do
            //{
            //    //wait
            //} while (!downloadComplete);
            //log.Info("Download Complete");
            //output.startTime = frameRefine((input.OrderBy(a => a.start).First().start), fileName, true, log);
            //output.endTime = frameRefine((input.OrderBy(a => a.end).Last().end), fileName, false, log);
            output.startTime = input.OrderBy(a => a.start).First().start;
            output.endTime = input.OrderBy(a => a.end).Last().end;

            output.fileName = fileName;

            return output;
        }

        static DateTime frameRefine(DateTime refineFrom, string fileName, bool isStart, TraceWriter log)
        {
            try
            {
                log.Info($"refineFrom: {refineFrom.TimeOfDay.ToString("hh\\:mm\\:ss\\.fff")} fileName: {fileName} isStart: {isStart}");
                // create the http client
                var handler = new HttpClientHandler();
                handler.AllowAutoRedirect = false;
                var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromMilliseconds(300);
                // Add a new Request Message
                HttpRequestMessage requestMessage = new HttpRequestMessage();
                requestMessage.Content = new StringContent(vidSASURL, Encoding.UTF8, "test/plain");
                requestMessage.RequestUri = new Uri($"{frameRefineFuncURL}?position={refineFrom.TimeOfDay.ToString("hh\\:mm\\:ss\\.fff")}&filename={fileName}&isstart={isStart}");

                var refineRequestResult = client.SendAsync(requestMessage).Result;
                DateTime refinedTime = DateTime.Parse(refineRequestResult.Content.ReadAsStringAsync().Result.ToString());

                return refinedTime;
            }
            catch (Exception ex)
            {
                throw;
            }

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
        public string fileName { get; set; }
        public thumb startFrame { get; set; }
        public thumb endFrame { get; set; }

    }

    public class startAndEndOutput
    {
        public string fileName { get; set; }
        public DateTime startTime { get; set; }
        public DateTime endTime { get; set; }

    }

    public class AddtlStartAndEndOutput
    {
        public string fileName { get; set; }
        public string startTime { get; set; }
        public string endTime { get; set; }

    }
}
