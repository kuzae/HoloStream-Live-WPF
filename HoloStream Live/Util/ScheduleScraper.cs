using PuppeteerSharp;
using HtmlAgilityPack;
using Holodex.NET;
using System.Diagnostics;

namespace HoloStream_Live.Services
{
    public class ScheduleService
    {
        private readonly HolodexClient _holodexClient;

        public ScheduleService(string holodexApiKey)
        {
            _holodexClient = new HolodexClient(holodexApiKey);
        }

        public async Task<List<StreamItem>> GetScheduleAsync(string organization = null, string scheduleUrl = null, string newHtmlUrl = null)
        {
            if (!string.IsNullOrEmpty(organization))
            {
                return await GetScheduleFromHolodexAsync(organization);
            }
            else if (!string.IsNullOrEmpty(scheduleUrl))
            {
                return await ScrapeScheduleAsync(scheduleUrl);
            }
            else if (!string.IsNullOrEmpty(newHtmlUrl))
            {
                return await ScrapeNewStructureAsync(newHtmlUrl);
            }
            else
            {
                throw new ArgumentException("Either organization, scheduleUrl, or newHtmlUrl must be provided.");
            }
        }

        private async Task<List<StreamItem>> GetScheduleFromHolodexAsync(string organization)
        {
            List<StreamItem> streamItems = new();
            try
            {
                Debug.WriteLine($"Fetching live and upcoming videos for {organization}...");
                var asdf = await _holodexClient.GetVideos(
                    organization: "Hololive",
                    limit: 1 // Fetch a single video
                );

                if (asdf.Any())
                {
                    Debug.WriteLine($"Title: {asdf.First().Title}");
                }
                else
                {
                    Debug.WriteLine("No videos returned.");
                }

                // Fetch live and upcoming videos using Holodex.NET
                var videos = await _holodexClient.GetVideos(
                    organization: organization,
                    maxUpcomingHours: 72,
                    limit: 50
                );

                if (videos == null || !videos.Any())
                {
                    Debug.WriteLine("No videos returned from the API.");
                    return streamItems;
                }

                var serializedPayload = Newtonsoft.Json.JsonConvert.SerializeObject(videos, Newtonsoft.Json.Formatting.Indented);
                Debug.WriteLine("Full Payload:");
                Debug.WriteLine(serializedPayload);
                // Log each video returned in the payload
                Debug.WriteLine($"Fetched {videos.Count()} videos:");


            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching videos: {ex.Message}");
            }

            return streamItems;
        }

        private async Task<List<StreamItem>> ScrapeScheduleAsync(string url)
        {
            ScheduleScraper scraper = new();
            return await scraper.ScrapeStreamsAsync(url);
        }

        private async Task<List<StreamItem>> ScrapeNewStructureAsync(string url)
        {
            List<StreamItem> streamItems = new();

            try
            {
                Debug.WriteLine("Using PuppeteerSharp for scraping...");
                var browserFetcher = new BrowserFetcher();
                await browserFetcher.DownloadAsync();

                using var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true });
                using var page = await browser.NewPageAsync();

                Debug.WriteLine($"Navigating to {url}...");
                await page.GoToAsync(url);

                await page.WaitForSelectorAsync(".home");
                Debug.WriteLine("Content loaded.");

                string htmlContent = await page.GetContentAsync();

                HtmlDocument doc = new();
                doc.LoadHtml(htmlContent);

                // Scrape live videos
                var liveNowNodes = doc.DocumentNode.SelectNodes("//div[contains(text(), 'Live now')]/../../div[contains(@class, 'row')]/div[contains(@class, 'col')]");
                if (liveNowNodes != null)
                {
                    foreach (var node in liveNowNodes)
                    {
                        streamItems.Add(ParseNewStreamItem(node, "Live"));
                    }
                }

                // Scrape upcoming videos
                var upcomingNodes = doc.DocumentNode.SelectNodes("//div[contains(text(), 'Upcoming Streams')]/../../div[contains(@class, 'row')]/div[contains(@class, 'col')]");
                if (upcomingNodes != null)
                {
                    foreach (var node in upcomingNodes)
                    {
                        streamItems.Add(ParseNewStreamItem(node, "Upcoming"));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error scraping new structure: {ex.Message}");
            }

            return streamItems;
        }

        private StreamItem ParseNewStreamItem(HtmlNode node, string status)
        {
            string title = node.SelectSingleNode(".//div[contains(@class, 'video-title')]")?.InnerText.Trim() ?? "Unknown";
            string thumbnailUrl = node.SelectSingleNode(".//img[@class='d-block']")?.GetAttributeValue("src", "N/A") ?? "N/A";
            string profileImageUrl = node.SelectSingleNode(".//div[contains(@class, 'video-avatar')]//img")?.GetAttributeValue("src", "N/A") ?? "N/A";
            string time = node.SelectSingleNode(".//div[contains(@class, 'video-absolute') and contains(@class, 'grey--text')]")?.InnerText.Trim() ?? "Unknown";
            string viewers = status == "Live"
                ? node.SelectSingleNode(".//div[contains(@class, 'video-absolute') and contains(@class, 'red--text')]")?.InnerText.Trim() ?? "N/A"
                : "N/A";
            string link = node.SelectSingleNode(".//a")?.GetAttributeValue("href", "N/A") ?? "N/A";

            return new StreamItem
            {
                Name = title,
                Link = link,
                Start = time,
                Text = status,
                LiveStatus = status,
                ProfilePictureUrl = profileImageUrl,
                BackgroundThumbnailUrl = thumbnailUrl,
            };
        }
    }

    public class ScheduleScraper
    {
        public async Task<List<StreamItem>> ScrapeStreamsAsync(string url)
        {
            List<StreamItem> streamItems = new();

            try
            {
                Debug.WriteLine("Using PuppeteerSharp for scraping...");
                var browserFetcher = new BrowserFetcher();
                await browserFetcher.DownloadAsync();

                using var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true });
                using var page = await browser.NewPageAsync();

                Debug.WriteLine($"Navigating to {url}...");
                await page.GoToAsync(url);

                await page.WaitForSelectorAsync("#today li");
                Debug.WriteLine("Content loaded.");

                string htmlContent = await page.GetContentAsync();

                HtmlDocument doc = new();
                doc.LoadHtml(htmlContent);

                var todayNode = doc.DocumentNode.SelectSingleNode("//ul[@id='today']");
                if (todayNode == null) return streamItems;

                var liNodes = todayNode.SelectNodes("./li");
                if (liNodes == null) return streamItems;

                foreach (var liNode in liNodes)
                {
                    streamItems.Add(ParseStreamItem(liNode));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error: {ex.Message}");
            }

            return streamItems;
        }

        private StreamItem ParseStreamItem(HtmlNode liNode)
        {
            string link = liNode.SelectSingleNode(".//a")?.GetAttributeValue("href", "N/A") ?? "N/A";
            string start = liNode.SelectSingleNode(".//p[contains(@class, 'start')]")?.InnerText.Trim() ?? "N/A";
            string name = liNode.SelectSingleNode(".//p[contains(@class, 'name')]")?.InnerText.Trim() ?? "N/A";
            string text = liNode.SelectSingleNode(".//p[contains(@class, 'txt')]")?.InnerText.Trim() ?? "N/A";

            var liveNode = liNode.SelectSingleNode(".//p[contains(@class, 'cat') and contains(@class, 'now_on_air')]");
            string liveStatus = liveNode != null ? "Live" : "Not Live";

            string profilePicUrl = liNode.SelectSingleNode(".//div[@class='icon clearfix']//img")?.GetAttributeValue("src", "N/A") ?? "N/A";
            string backgroundThumbnailUrl = liNode.SelectSingleNode(".//figure[@class='left']//img")?.GetAttributeValue("src", "N/A") ?? "N/A";

            return new StreamItem
            {
                Link = link,
                Start = start,
                Name = name,
                Text = text,
                LiveStatus = liveStatus,
                ProfilePictureUrl = profilePicUrl,
                BackgroundThumbnailUrl = backgroundThumbnailUrl
            };
        }
    }

    public class StreamItem
    {
        public string? Link { get; set; }
        public string? Start { get; set; }
        public string? Name { get; set; }
        public string? Text { get; set; }
        public string? LiveStatus { get; set; }
        public string? ProfilePictureUrl { get; set; }
        public string? BackgroundThumbnailUrl { get; set; }
    }
}
