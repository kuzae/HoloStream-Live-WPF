using PuppeteerSharp;
using HtmlAgilityPack;

namespace HoloStream_Live.Services
{
    public class ScheduleScraper
    {
        public async Task<List<StreamItem>> ScrapeStreamsAsync(string url)
        {
            List<StreamItem> streamItems = new();

            try
            {
                Console.WriteLine("Running on non-Android platform, using PuppeteerSharp for scraping...");
                var browserFetcher = new BrowserFetcher();
                await browserFetcher.DownloadAsync();

                using var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true });
                using var page = await browser.NewPageAsync();

                Console.WriteLine($"Navigating to {url}...");
                await page.GoToAsync(url);

                await page.WaitForSelectorAsync("#today li");
                Console.WriteLine("Content loaded.");

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
                Console.WriteLine($"Error: {ex.Message}");
            }

            return streamItems;
        }

        // Helper method to parse the StreamItem from the scraped HTML
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
        public string ?Link { get; set; }
        public string ?Start { get; set; }
        public string ?Name { get; set; }
        public string ?Text { get; set; }
        public string ?LiveStatus { get; set; }
        public string ?ProfilePictureUrl { get; set; }
        public string ?BackgroundThumbnailUrl { get; set; }
    }
}
