using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace Company.Function
{
    public static class DurableFanOutInCSC4940
    {
        static readonly string[] urlList = {
        "https://www.datesfoundation.org/Careers",
        "https://www.cwb.org/careers",
        "https://phh.tbe.taleo.net/phh01/ats/careers/searchResults.jsp?org=SPACENEEDLE&cws=5",
        "https://unitedindians.org/about/jobs/",
        "https://mopop.org/about-mopop/get-involved/join-the-team/",
        "https://fryemuseum.org/employment/",
        "https://henryart.org/about/opportunities#page-navigation-jobs",
        "http://jobs.jobvite.com/lcm-plus-labs",
        "https://recruiting2.ultipro.com/MUS1007MMF",
        "https://mohai.org/about/#opportunities",
        "https://www.nordicmuseum.org/about/jobs",
        "https://seattleartmuseum.apploytojob.com/apply",
        "https://us59.dayforcehcm.com/CandidatePortal/en-US/pacsci",
        "https://seattleaquarium.org/careers#openings",
        "https://seattleartmuseum.applytojob.com/apply",
        "https://thechildrensmuseum.org/visit/contact/job-opportunities/",
        "https://www.virginiav.org/employment/",
        "https://wingluke.org/jobs/",
        "https://www2.appone.com/Search/Search.aspx?ServerVar=WoodlandParkZoo.appone.com&results=yes"
        };

        [FunctionName("DurableFanOutInCSC4940")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
            /*This function should set the FetchHTML function instances at the various pages they target.
            It will also provide the correct processing rule.
            It will then save the complete JSON these functions return once they finish fanning in.*/
        {
            var parallelTasks = new List<Task<string>>(); //Specifically JSON strings
            var outputs = new List<string>(); //TODO: COMPILE JSON IN HERE
log.LogInformation($"Hit orchestrator.");
            foreach (string url in urlList)
            {
                Task<string> task = context.CallActivityAsync<string>("DurableFanOutInCSC4940_FetchHTML", url);
                parallelTasks.Add(task);
log.LogInformation($"Processing URL = '{url}'.");
            }

            await Task.WhenAll(parallelTasks); //Thus we don't proceed past until all threads finish.
log.LogInformation($"All URLs complete.");


            // Replace "hello" with the name of your Durable Activity Function.
            /*
            outputs.Add(await context.CallActivityAsync<string>("DurableFanOutInCSC4940_Hello", "https://www.cwb.org/careers"));
            outputs.Add(await context.CallActivityAsync<string>("DurableFanOutInCSC4940_Hello", "PlaceholderString"));
            outputs.Add(await context.CallActivityAsync<string>("DurableFanOutInCSC4940_Hello", "PlaceholderString"));
            */
            // returns ["Hello Tokyo!", "Hello Seattle!", "Hello London!"]
            outputs.Add("This is a dummy string for Testing.");
            foreach (Task<string> jsonBlock in parallelTasks)
            {
                outputs.Add(jsonBlock.Result);
            }
            return outputs; //TODO: RETURN ACTUAL JSON HERE (AND THEN SAVE IT TO FILE)
        }

        [FunctionName("DurableFanOutInCSC4940_FetchHTML")]
        public static string FetchHTML([ActivityTrigger] string targetURL, ILogger log)
        /*This function should fetch the HTML from a target page, process it, and return JSON representing job listings.*/
        {
            //TODO: Actually write this code
            log.LogInformation($"Saying hello to {targetURL}.");
            return $"Hello {targetURL}!";
        }

        [FunctionName("DurableFanOutInCSC4940_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            /*TODO: Add TimerStart and disable this when it's no longer needed
            This function starts the "Durable Function" (that is, the program) via HTTP request.
            It is used for debugging. When not in use, it should be disabled.
            Normally this Durable Function will be started with a Timer Trigger instead.*/
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("DurableFanOutInCSC4940", null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}