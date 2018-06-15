
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using System.Net;

namespace BrandIdentifier
{
    public static class CleanupFiles
    {
        [FunctionName("CleanupFiles")]
        public static IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequest req, TraceWriter log)
        {
            log.Info("File Cleanup started.");

            string fileName = WebUtility.UrlDecode(req.Query["filename"]);

            Cleanup(fileName);

            if (fileName != null)
            {
                return (ActionResult)new OkObjectResult($"Cleanup Complete");
            }
            else
            {
                return new BadRequestObjectResult("Please pass a file name on the query string or in the request body");
            }
        }


        private static void Cleanup(string fileName)
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
}
