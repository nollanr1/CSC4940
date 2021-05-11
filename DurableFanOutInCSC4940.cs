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
using HtmlAgilityPack; //This allows for the relatively easy parsing of the imported HTML.
using Newtonsoft.Json; //This protects my JSON from potential injection attacks... Hopefully.

namespace Company.Function
{
    public static class DurableFanOutInCSC4940
    {
        static readonly string[] urlList = {
        "https://phh.tbe.taleo.net/phh01/ats/careers/searchResults.jsp?org=SPACENEEDLE&cws=5",
        "https://www.nordicmuseum.org/about/jobs",
        "https://seattleaquarium.org/careers#openings"/*,
        "https://unitedindians.org/about/jobs/",
        "https://www.gatesfoundation.org/about/careers",
        //"https://www.cwb.org/careers", //TODO: MAKE THIS WEBSITE PLAY NICE (STRETCH GOAL) - THROWS 400 ERROR
        "https://mopop.org/about-mopop/get-involved/join-the-team/",
        "https://fryemuseum.org/employment/",
        "https://henryart.org/about/opportunities#page-navigation-jobs",
        "http://jobs.jobvite.com/lcm-plus-labs", //Well, I can't do anything with this one because there's no jobs posted for it right now.
        //"https://recruiting2.ultipro.com/MUS1007MMF", ////TODO: MAKE THIS WEBSITE PLAY NICE (STRETCH GOAL) - CANNOT ESTABLISH SSL CONNECTION (Very long error description.)
        "https://mohai.org/about/#opportunities",
        "https://seattleartmuseum.applytojob.com/apply",
        "https://us59.dayforcehcm.com/CandidatePortal/en-US/pacsci",
        "https://seattleartmuseum.applytojob.com/apply",
        "https://thechildrensmuseum.org/visit/contact/job-opportunities/",
        "https://www.virginiav.org/employment/",
        "https://www.wingluke.org/jobs/",
        "https://www2.appone.com/Search/Search.aspx?ServerVar=WoodlandParkZoo.appone.com&results=yes" //TODO: Bring back the other strings too.
        */
        };
        static readonly string containerName = "scrapeddata";
        static readonly string blobName = "raw-output";

        public class Details
        {
            public string applink = null;
            public string salary = null;
            public string title = null;
            public string closebydate = null;
            public string description = null;
            public string othernotes = null;
            public string emailcontact = null;
        }
        public class JobListing
        {
            public string host = null;
            public List<Details> details = new List<Details>(); 
        }

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

            List<JobListing> summatedJobList = new List<JobListing>();
            //I've found it's best to practice these queries with https://dotnetfiddle.net/fKeTAp
            summatedJobList.AddRange(TaleoNetProcessor(parallelTasks[0].Result, log)); //Here's hoping this is the correct way to pass the logger.
            summatedJobList.AddRange(NordicMuseumProcessor(parallelTasks[1].Result, log));
            summatedJobList.AddRange(SeattleAquariumProcessor(parallelTasks[2].Result, log));
            //TODO: Add the rest of the jobs! Also, see if I can do this async.
            outputs.Add("{\"hosts\": "+ JsonConvert.SerializeObject(summatedJobList) + "}");


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
            
            return outputs; //TODO: Swap this return value for something more lightweight, since I've uploaded needed data?
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

        [FunctionName("TaleoNetProcessor")]
        public static List<JobListing> TaleoNetProcessor([ActivityTrigger] string incomingHTML, ILogger log)
        {
            List<JobListing> taleoJobList = new List<JobListing>();
            var htmlDoc = new HtmlDocument();
            try {
                htmlDoc.LoadHtml(incomingHTML);
                var query = "//table[@id='cws-search-results']";
                var node = htmlDoc.DocumentNode.SelectSingleNode(query);
                node.FirstChild.Remove();
                node.FirstChild.Remove();
                HtmlNodeCollection childNodes = node.ChildNodes;
                foreach(var offpsringNode in childNodes) {
                    HtmlNodeCollection tableRow = offpsringNode.ChildNodes;
                    if(tableRow.Count >= 7) { //The way the incoming table is structured, the data I want is on columns 1, 3, 5, 7
                    JobListing taleoJob = new JobListing();
                    taleoJob.details.Add(new Details()); //Ideally, I'd figure out how to have these add automatically... TODO: Make a proper constructor.
                    taleoJob.details[0].title = tableRow[1].InnerText;
                    taleoJob.details[0].applink = tableRow[1].FirstChild.FirstChild.Attributes["href"].Value; //Woo, much dereferencing, much wow.
                    taleoJob.details[0].closebydate = tableRow[3].InnerText;
                    taleoJob.details[0].salary = tableRow[5].InnerText;
                    taleoJob.host = tableRow[7].InnerText;
                    taleoJobList.Add(taleoJob);
                    }
                }
                return taleoJobList;
            }
            catch (System.Exception e) {
                JobListing errorMessage = new JobListing();
                errorMessage.host = "Processing Error";
                errorMessage.details.Add(new Details());
                errorMessage.details[0].applink = urlList[0];
                errorMessage.details[0].title = "Error Details";
                errorMessage.details[0].description = e.ToString();
                taleoJobList.Add(errorMessage);
                return taleoJobList;
            }
        }

        [FunctionName("NordicMuseumProcessor")]
        public static List<JobListing> NordicMuseumProcessor([ActivityTrigger] string incomingHTML, ILogger log)
        {
            List<JobListing> nordicJobList = new List<JobListing>();
            JobListing nordicJobs = new JobListing();
            nordicJobs.host = "National Nordic Museum";
            var htmlDoc = new HtmlDocument();
            try {
                htmlDoc.LoadHtml(incomingHTML);
                var query = "//div[@class='node__content']/div/div/div/div";
                var htmlBody = htmlDoc.DocumentNode.SelectSingleNode(query);
                var htmlChildren= htmlBody.ChildNodes;

                var loopControl = 8;
                for (var i =loopControl; i<=htmlChildren.Count; i+=loopControl) {
                    var currentArr = ((i/loopControl) - 1);
                    nordicJobs.details.Add(new Details());
                    nordicJobs.details[currentArr].title = htmlChildren[i-5].InnerHtml;
                    nordicJobs.details[currentArr].description = htmlChildren[i-3].InnerHtml;
                    nordicJobs.details[currentArr].applink = htmlChildren[i-1].ChildNodes[1].Attributes["href"].Value;
                    nordicJobs.details[currentArr].salary = htmlChildren[i-1].ChildNodes[2].InnerHtml.Remove(0, 2); //There's a ". " at the start of this string, which we'll discard.
                    nordicJobs.details[currentArr].closebydate = htmlChildren[i-1].ChildNodes[10].InnerHtml.Trim();
                    nordicJobs.details[currentArr].emailcontact = htmlChildren[i-1].ChildNodes[6].Attributes["href"].Value;
                    nordicJobs.details[currentArr].othernotes = (htmlChildren[i-1].ChildNodes[5].InnerHtml.TrimStart() +
                                                                htmlChildren[i-1].ChildNodes[6].InnerHtml +
                                                                htmlChildren[i-1].ChildNodes[7].InnerHtml);
                }
                nordicJobList.Add(nordicJobs);
            }
            catch (System.Exception e) {
                JobListing errorMessage = new JobListing();
                errorMessage.host = "Processing Error";
                errorMessage.details.Add(new Details());
                errorMessage.details[0].applink = urlList[1];
                errorMessage.details[0].title = "Error Details";
                errorMessage.details[0].description = e.ToString();
                nordicJobList.Add(errorMessage);
            }

            return nordicJobList;
        }

        [FunctionName("SeattleAquariumProcessor")]
        public static List<JobListing> SeattleAquariumProcessor([ActivityTrigger] string incomingHTML, ILogger log)
        {
            List<JobListing> aquariumJobList = new List<JobListing>();
            JobListing aquariumJobs = new JobListing();
            aquariumJobs.host = "Seattle Aquarium";
            var htmlDoc = new HtmlDocument();
            try {
                htmlDoc.LoadHtml(incomingHTML);
                var query = "//div[@class='field-sections-basic-page']//div[@class='container']/div[@class='row']//div[@class='clearfix']/div";
                var jobNodes = htmlDoc.DocumentNode.SelectNodes(query); //Before the jobs, there's six other nodes with this exact pattern that I don't want.

                var i = 0;
                foreach (HtmlNode node in jobNodes) {
                    if(i > 5) { //There's six other items matching this description exactly. They always come first so I'll filter them this way.
                        aquariumJobs.details.Add(new Details());
                        HtmlNodeCollection subNodes = node.ChildNodes;
                        aquariumJobs.details[i-6].title = subNodes[1].InnerText;
                        HtmlNode reverseIterator = subNodes[3].LastChild;
                        reverseIterator = reverseIterator.PreviousSibling;
                        aquariumJobs.details[i-6].closebydate = reverseIterator.InnerText + " | "; //This is the date the job posting expires.
                        reverseIterator = reverseIterator.PreviousSibling;
                        reverseIterator = reverseIterator.PreviousSibling;
                        aquariumJobs.details[i-6].closebydate = aquariumJobs.details[i-6].closebydate + reverseIterator.InnerText; //This is the date the job posting needs to be filled by.
                        reverseIterator = reverseIterator.PreviousSibling;
                        reverseIterator = reverseIterator.PreviousSibling;
                        aquariumJobs.details[i-6].othernotes = reverseIterator.InnerText;
                        aquariumJobs.details[i-6].applink = reverseIterator.ChildNodes[1].Attributes["href"].Value;//I don't like using the absolute call but checking for nulls is causing a crash.
                        HtmlNode forwardIterator = subNodes[3].FirstChild;
                        string detailsString = "";
                        while((forwardIterator != reverseIterator) && (forwardIterator != reverseIterator.NextSibling)) {
                            detailsString += forwardIterator.InnerText;
                            forwardIterator = forwardIterator.NextSibling; //I'm including the whitespace because why not.
                        }
                        detailsString = detailsString.Replace("&nbsp;", " ");
                        detailsString = detailsString.Replace("&amp;", "&");
                        aquariumJobs.details[i-6].description = detailsString;
                    }
                    i++;
                }

                aquariumJobList.Add(aquariumJobs);
                return aquariumJobList;
            }
            catch (System.Exception e) {
                JobListing errorMessage = new JobListing();
                errorMessage.host = "Processing Error";
                errorMessage.details.Add(new Details());
                errorMessage.details[0].applink = urlList[2];
                errorMessage.details[0].title = "Error Details";
                errorMessage.details[0].description = e.ToString();
                aquariumJobList.Add(errorMessage);
                return aquariumJobList;
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