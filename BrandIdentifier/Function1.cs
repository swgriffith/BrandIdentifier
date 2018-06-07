
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using System.Text;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage;
using System;

namespace BrandIdentifier
{
    public static class Function1 
    {
        //Video Indexer Settings
        static string apiUrl;
        static string accountId;
        static string location;
        static string apiKey;
        //CustomVision Settings
        static string predictionKey;
        static string customVizURL;
        //Output Storage Account
        static string storageConnectionString;

        [FunctionName("Function1")]
        public static IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "Function1/{name}")]HttpRequest req,
            string name,
            Binder binder,
            TraceWriter log, 
            ExecutionContext context)
        {
            log.Info("C# HTTP trigger function processed a request.");
            
            GetSettings(context);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format("apiUrl: {0}", apiUrl));
            sb.AppendLine(string.Format("accountId: {0}", accountId));
            sb.AppendLine(string.Format("location: {0}", location));
            sb.AppendLine(string.Format("apiKey: {0}", apiKey));
            sb.AppendLine(string.Format("predictionKey: {0}", predictionKey));
            sb.AppendLine(string.Format("customVizURL: {0}", customVizURL));

            string fileName = "working";

            var attributes = new Attribute[]
            {
                new BlobAttribute(blobPath: $"results/{fileName}"),
                new StorageAccountAttribute("storageConnectionString")
            };


            string requestBody = new StreamReader(req.Body).ReadToEnd();


            using (var writer = binder.BindAsync<TextWriter>(attributes).Result)
            {
                writer.Write(sb.ToString());
            }

            //StreamWriter sw = new StreamWriter(writer);
            //{
            //    sw.Write(sb.ToString());
            //    sw.Flush();
            //}

            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name = name ?? data?.name;

            return name != null
                ? (ActionResult)new OkObjectResult(sb.ToString())
                : new BadRequestObjectResult("Please pass a name on the query string or in the request body");
        }

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
            storageConnectionString = config["storageConnectionString"];
        }

        //static void writeOutputBlob(string text)
        //{
        //    CloudStorageAccount storageAccount;
        //    if (CloudStorageAccount.TryParse(storageConnectionString, out storageAccount))
        //    {
        //        CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();

        //        cloudBlobContainer = cloudBlobClient.GetContainerReference("results");
        //        cloudBlobContainer.CreateAsync().Result;

        //        CloudBlobContainer container = cloudBlobClient.GetContainerReference("results");
        //        container.CreateIfNotExists();

        //        CloudBlockBlob blob = container.GetBlockBlobReference("newfolder/newTextfile.txt");
        //        blob.UploadText("any_content_you_want");
        //    }
            
        //}
    }

}
