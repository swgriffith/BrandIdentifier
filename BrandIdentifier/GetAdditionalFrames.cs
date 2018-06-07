
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

namespace BrandIdentifier
{
    public static class GetAdditionalFrames
    {
        [FunctionName("GetAdditionalFrames")]
        public static IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequest req,
            TraceWriter log)
        {
            log.Info("Call invoked to get video frames.");


            string possition = WebUtility.UrlDecode(req.Query["possition"]);
            string vidurl = WebUtility.UrlDecode(req.Query["vidurl"]);

            log.Info(Directory.GetCurrentDirectory());
            var psi = new ProcessStartInfo();
            psi.FileName = @".\ffmpeg\ffmpeg.exe";
            psi.Arguments = $"-i \"{vidurl}\" -ss {possition} -frames:v 1 test.png";
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;

            log.Info($"Args: {psi.Arguments}");
            var process = Process.Start(psi);
            process.WaitForExit((int)TimeSpan.FromSeconds(60).TotalMilliseconds);

            if (possition != null && vidurl !=null)
            {
                return (ActionResult)new OkObjectResult($"possition={possition} vidurl={vidurl}");
            }
            else
            {
                return new BadRequestObjectResult("Please confirm the input possition and vidurl URL params are set.");
            }
        }
    }
}
