using System.Collections.Generic;
using System.Net; //For webrequests, since system.net.http doesn't seem to cover that
using System.Net.Http;
using System.IO; //Allows for stream objects
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Azure.Storage.Blobs;

namespace Company.Function
{
    public static class DurableFanOutInCSC4940
    {
        static readonly string[] urlList = {
        "https://www.gatesfoundation.org/about/careers",
        //"https://www.cwb.org/careers", //TODO: MAKE THIS WEBSITE PLAY NICE (STRETCH GOAL) - THROWS 400 ERROR
        "https://phh.tbe.taleo.net/phh01/ats/careers/searchResults.jsp?org=SPACENEEDLE&cws=5",
        "https://unitedindians.org/about/jobs/",
        "https://mopop.org/about-mopop/get-involved/join-the-team/",
        "https://fryemuseum.org/employment/",
        "https://henryart.org/about/opportunities#page-navigation-jobs",
        "http://jobs.jobvite.com/lcm-plus-labs",
        //"https://recruiting2.ultipro.com/MUS1007MMF", ////TODO: MAKE THIS WEBSITE PLAY NICE (STRETCH GOAL) - CANNOT ESTABLISH SSL CONNECTION (Very long error description.)
        "https://mohai.org/about/#opportunities",
        "https://www.nordicmuseum.org/about/jobs",
        "https://seattleartmuseum.applytojob.com/apply",
        "https://us59.dayforcehcm.com/CandidatePortal/en-US/pacsci",
        "https://seattleaquarium.org/careers#openings",
        "https://seattleartmuseum.applytojob.com/apply",
        "https://thechildrensmuseum.org/visit/contact/job-opportunities/",
        "https://www.virginiav.org/employment/",
        "https://wingluke.org/jobs/",
        "https://www2.appone.com/Search/Search.aspx?ServerVar=WoodlandParkZoo.appone.com&results=yes" //TODO: Bring back the other strings too.
        };
        static readonly string containerName = "scrapeddata";
        static readonly string blobName = "raw-output";

        [FunctionName("DurableFanOutInCSC4940")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
            /*This function should set the FetchHTML function instances at the various pages they target.
            It will also provide the correct processing rule.
            It will then save the complete JSON these functions return once they finish fanning in.*/
        {
            var parallelTasks = new List<Task<string>>(); //Yes, each string will be an entire page of HTML
            var outputs = new List<string>(); //'outputs' is the ultimate return value, what will be saved to the Blob
            foreach (string url in urlList)
            {
                Task<string> task = context.CallActivityAsync<string>("DurableFanOutInCSC4940_FetchHTML", url);
                parallelTasks.Add(task);
            }

            await Task.WhenAll(parallelTasks); //Thus we don't proceed past until all threads finish.

            //Now we'll prepare the blob client.
            //I used to have this during the async part...
            //But I believe it was firing on each thread. Which I don't want.
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory()) //Hopefully that doesn't mess something ELSE up.
                .AddJsonFile("appsettings.json");
            var configuration = builder.Build();
            var connectionString = configuration.GetConnectionString("StorageAccount");
            BlobContainerClient container = new BlobContainerClient(connectionString, containerName);
            container.CreateIfNotExists(); //It SHOULD exist, but just in case it doesn't...
            BlobClient blobClient = container.GetBlobClient(blobName);

            outputs.Add("This is a dummy string for Testing.");
            foreach (Task<string> jsonBlock in parallelTasks)
            {
                //TODO: Invoke the JSON EXTRACTOR HERE to process the HTML instead of just adding the raw HTML.
                outputs.Add(jsonBlock.Result);
            }
            //TODO: Collate the individual JSON chunks into one big (safely escaped) string.
            /******************IMPORTANT: ******************************
            I can probably accomplish these two things with Json.NET https://www.newtonsoft.com/json
            Planned structure: create a list of "Job" objects,
            Then use Json.net to convert that list into a collection of correctly formatted strings.
            I should be able to mash those strings together without much trouble,
            which I can then upload as a nice, relatively pretty blob.
            *******************************************************************/

            /*******************
            This next code turns the strings into streams.
            If there's some sort of StringStream class, I couldn't find documentation for it.
            ********************/
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(outputs[0]);
            writer.Flush();
            stream.Position = 0;
            //***END string-to-stream conversion code***
            blobClient.Upload(stream, true).ToString();
            
            return outputs; //TODO: RETURN ACTUAL JSON HERE (AND THEN SAVE IT TO FILE)
        }

        [FunctionName("DurableFanOutInCSC4940_FetchHTML")]
        public static string FetchHTML([ActivityTrigger] string targetURL, ILogger log)
        /*This function should fetch the HTML from a target page, process it, and return JSON representing job listings.*/
        {
            //TODO: remove debug LOG statements
            log.LogInformation($"Saying hello to {targetURL}.");
            WebRequest urlGetter;
            urlGetter = WebRequest.Create(targetURL);
            Stream htmlStream;
            StreamReader readStream;
            System.Text.Encoding encode = System.Text.Encoding.GetEncoding("utf-8");
            try {
                htmlStream = urlGetter.GetResponse().GetResponseStream(); //Not doing this async because everything depends on the response, so it's blocking anyways.
                readStream = new StreamReader(htmlStream, encode);
                System.String readString;
                readString = readStream.ReadToEnd(); //NOTE: Storing this all in one string puts a strict 2GB limit on a single page that can be loaded. Hey, technology progresses...
                readStream.Close();
                htmlStream.Close();
                return $"MASS TEXT OF {targetURL}: {readString}!";
                /*This should return to the orchestrator, which in turn calls processing functions.
                If I have this function call functions, then they wait - and get charged for waiting!
                Microsoft cites this as a common antipattern.
                However, the orchestrator is halted (and thus not charged) while it waits, because Microsoft specfically built for this pattern.
                However, I DO get charged as a separate invocation every time the orchestrator is called back... I wonder how that works out.*/
            }
            catch (System.Exception e) {
                //readStream.Close();
                //htmlStream.Close(); Hopefully these auto-close on program death... TODO: Make these close more reliably.
                return $"Encountered an error while processing {targetURL} (Error ID: {e}).";
            }
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