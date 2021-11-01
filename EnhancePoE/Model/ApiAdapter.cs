using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Net;
using EnhancePoE.Model;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace EnhancePoE
{
    public class ApiAdapter
    {

        private static HttpClient _httpClient;
        private readonly static object _lock = new object();
        private readonly static ApiAdapter _httpClientUtils = new ApiAdapter();

        static ApiAdapter()
        {
            _httpClient = GetHttpClient();
        }

        public static ApiAdapter GetHttpClientUtils()
        {
            return _httpClientUtils;
        }

        private ApiAdapter()
        {

        }

        private static HttpClient GetHttpClient()
        {
            lock (_lock)
            {
                if (_httpClient == null)
                {
                    var handler = new HttpClientHandler() { UseCookies = false };
                    _httpClient = new HttpClient(handler);
                    _httpClient.Timeout = TimeSpan.FromSeconds(1800);
                }
            }
            return _httpClient;
        }

        public static bool IsFetching { get; set; } = false;
        private static StashTabPropsList PropsList { get; set; }
        public static bool FetchError { get; set; } = false;
        public static bool FetchingDone { get; set; } = false;
        public static async Task<bool> GenerateUri()
        {
            FetchError = false;
            FetchingDone = false;
            Trace.WriteLine("generating uris!!");
            if (Properties.Settings.Default.accName != ""
                && Properties.Settings.Default.League != "")
            {
                //ChaosRecipeEnhancer.aTimer.Enabled = false;
                //Trace.WriteLine("stopping timer");
                //Trace.WriteLine(ChaosRecipeEnhancer.aTimer.Interval);
                //Trace.WriteLine(ChaosRecipeEnhancer.aTimer.)
                string accName = Properties.Settings.Default.accName.Trim();
                string league = Properties.Settings.Default.League.Trim();

                if(await GetProps(accName, league))
                {
                    if (!FetchError)
                    {
                        GenerateStashTabs();
                        GenerateStashtabUris(accName, league);
                        return true;
                    }
                }

                // https://www.pathofexile.com/character-window/get-stash-items?accountName=kosace&tabIndex=0&league=Heist
            }
            else
            {
                MessageBox.Show("缺少设置!" +  Environment.NewLine + "请输入账号名、赛季和仓库.");
            }
            IsFetching = false;
            return false;
        }

        private static void GenerateStashTabs()
        {
            List<StashTab> ret = new List<StashTab>();

            // mode = ID
            if (Properties.Settings.Default.StashtabMode == 0)
            {
                StashTabList.GetStashTabIndices();
                if(PropsList != null)
                {
                    foreach (StashTabProps p in PropsList.tabs)
                    {
                        for (int i = StashTabList.StashTabIndices.Count - 1; i > -1; i--)
                        {
                            if (StashTabList.StashTabIndices[i] == p.i)
                            {
                                StashTabList.StashTabIndices.RemoveAt(i);
                                ret.Add(new StashTab(p.n, p.i));
                            }
                        }
                    }
                    StashTabList.StashTabs = ret;
                    GetAllTabNames();
                }
            }
            // mode = Name
            else
            {
                if(PropsList != null)
                {
                    string stashName = Properties.Settings.Default.StashTabName;
                    foreach (StashTabProps p in PropsList.tabs)
                    {
                        if (p.n.StartsWith(stashName))
                        {
                            ret.Add(new StashTab(p.n, p.i));
                        }
                    }
                    StashTabList.StashTabs = ret;
                }
            }
            Trace.WriteLine(StashTabList.StashTabs.Count, "stash tab count");
        }

        private static void GenerateStashtabUris(string accName, string league)
        {
            string baseUrl = "poe.game.qq.com";
            if (Properties.Settings.Default.GameArea == 1)
            {
                baseUrl = "www.pathofexile.com";
            }
            foreach (StashTab i in StashTabList.StashTabs)
            {
                string stashTab = i.TabIndex.ToString();
                i.StashTabUri = new Uri($"https://{baseUrl}/character-window/get-stash-items?accountName={accName}&tabIndex={stashTab}&league={league}");
            }
        }

        private static void GetAllTabNames()
        {
            foreach(StashTab s in StashTabList.StashTabs)
            {
                foreach(StashTabProps p in PropsList.tabs)
                {
                    if(s.TabIndex == p.i)
                    {
                        s.TabName = p.n;
                    }
                }
            }
        }

        private static async Task<bool> GetProps(string accName, string league)
        {
            //Trace.WriteLine(IsFetching, "isfetching props");
            if(IsFetching)
            {
                return false;
            }
            if (Properties.Settings.Default.SessionId == "")
            {
                MessageBox.Show("缺少设置!" + Environment.NewLine + "请输入PoE Session Id.");
                return false;
            }
            // check rate limit
            if (RateLimit.CheckForBan())
            {
                return false;
            }
            // -1 for 1 request + 3 times if ratelimit high exceeded
            if(RateLimit.RateLimitState[0] >= RateLimit.MaximumRequests - 4)
            {
                RateLimit.RateLimitExceeded = true;
                return false;
            }
            IsFetching = true;
            string baseUrl = "poe.game.qq.com";
            if (Properties.Settings.Default.GameArea == 1)
            {
                baseUrl = "www.pathofexile.com";
            }
            Uri propsUri = new Uri($"https://{baseUrl}/character-window/get-stash-items?accountName={accName}&tabs=1&league={league}");

            string sessionId = Properties.Settings.Default.SessionId;


            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, propsUri);
            message.Headers.Add("Host", baseUrl);
            message.Headers.Add("Cookie", "POESESSID=" + sessionId);
            message.Headers.Add("User-Agent", $"EnhancePoEApp/v{Assembly.GetExecutingAssembly().GetName().Version}");

            HttpResponseMessage res = await _httpClient.SendAsync(message);

            if (res.IsSuccessStatusCode)
            {
                using (HttpContent content = res.Content)
                {
                    string resContent = await content.ReadAsStringAsync();
                    //Trace.Write(resContent);
                    PropsList = JsonSerializer.Deserialize<StashTabPropsList>(resContent);

                    Trace.WriteLine(res.Headers, "res headers");

                    // get new rate limit values
                    //RateLimit.IncreaseRequestCounter();
                    string rateLimit = res.Headers.GetValues("X-Rate-Limit-Account").FirstOrDefault();
                    string rateLimitState = res.Headers.GetValues("X-Rate-Limit-Account-State").FirstOrDefault();
                    string responseTime = res.Headers.GetValues("Date").FirstOrDefault();
                    RateLimit.DeserializeRateLimits(rateLimit, rateLimitState);
                    RateLimit.DeserializeResponseSeconds(responseTime);
                }
            }
            else
            {
                if(res.StatusCode == HttpStatusCode.Forbidden)
                {
                    System.Windows.MessageBox.Show("Connection forbidden. Please check your Accountname and POE Session ID. You may have to refresh your POE Session ID sometimes.", "Error fetching data", MessageBoxButton.OK, MessageBoxImage.Error);
                } 
                else
                {
                    System.Windows.MessageBox.Show(res.ReasonPhrase, "Error fetching data", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                FetchError = true;
                return false;
            }

            //await Task.Delay(1000);
            IsFetching = false;
            return true;
        }

        public async static Task<bool> GetItems()
        {
            if (IsFetching)
            {
                Trace.WriteLine("already fetching");
                return false;
            }
            if (Properties.Settings.Default.SessionId == "")
            {
                MessageBox.Show("Missing Settings!" + Environment.NewLine + "Please set PoE Session Id.");
                return false;
            }
            if (FetchError)
            {
                return false;
            }
            // check rate limit
            if (RateLimit.RateLimitState[0] >= RateLimit.MaximumRequests - StashTabList.StashTabs.Count - 4)
            {
                RateLimit.RateLimitExceeded = true;
                return false;
            }
            IsFetching = true;
            List<Uri> usedUris = new List<Uri>();

            bool flag = true;

            string sessionId = Properties.Settings.Default.SessionId;

            List<Task> tasks = new List<Task>();
            Parallel.ForEach(StashTabList.StashTabs, i =>
                {
                    var t = Task.Run(async () =>
                    {
                        // check rate limit ban
                        try
                        {
                            if (RateLimit.CheckForBan())
                            {
                                flag = false;
                            }
                            if (!usedUris.Contains(i.StashTabUri))
                            {
                                string baseUrl = "poe.game.qq.com";
                                if (Properties.Settings.Default.GameArea == 1)
                                {
                                    baseUrl = "www.pathofexile.com";
                                }
                                HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, i.StashTabUri);
                                message.Headers.Add("Host", baseUrl);
                                message.Headers.Add("Cookie", "POESESSID="+sessionId);
                                message.Headers.Add("User-Agent", $"EnhancePoEApp/v{Assembly.GetExecutingAssembly().GetName().Version}");

                                Trace.WriteLine(i.TabIndex + "start" + System.Environment.TickCount);

                                HttpResponseMessage res = await _httpClient.SendAsync(message);


                            Trace.WriteLine(i.TabIndex + "end" + System.Environment.TickCount);

                            usedUris.Add(i.StashTabUri);
                            if (res.IsSuccessStatusCode)
                            {
                                using (HttpContent content = res.Content)
                                {
                                    // get new rate limit values
                                    //RateLimit.IncreaseRequestCounter();
                                    string rateLimit = res.Headers.GetValues("X-Rate-Limit-Account").FirstOrDefault();
                                    string rateLimitState = res.Headers.GetValues("X-Rate-Limit-Account-State").FirstOrDefault();
                                    string responseTime = res.Headers.GetValues("Date").FirstOrDefault();
                                    RateLimit.DeserializeRateLimits(rateLimit, rateLimitState);
                                    RateLimit.DeserializeResponseSeconds(responseTime);

                                    // deserialize response
                                    string resContent = await content.ReadAsStringAsync();
                                    ItemList deserializedContent = JsonSerializer.Deserialize<ItemList>(resContent);
                                    i.ItemList = deserializedContent.items;
                                    i.Quad = deserializedContent.quadLayout;

                                    i.CleanItemList();
                                }
                            }
                            else
                            {
                                FetchError = true;
                                    System.Windows.MessageBox.Show(res.ReasonPhrase, "Error fetching data", MessageBoxButton.OK, MessageBoxImage.Error);
                                flag = false;
                            }
                            }
                        }
                        catch (Exception e)
                        {
                            flag = false;
                            Trace.WriteLine(e,"获取失败");
                        }

                    });
                    tasks.Add(t);
                });
            await Task.WhenAll(tasks);


            IsFetching = false;
            FetchingDone = true;
            if (!flag)
            {
                return false;
            }
            return true;
        }
    }
}
