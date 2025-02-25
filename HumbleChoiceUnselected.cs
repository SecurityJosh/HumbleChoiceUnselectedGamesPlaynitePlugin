using HumbleChoice;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Controls;


namespace HumbleChoiceUnselected
{
    public class HumbleChoiceUnselected : LibraryPlugin
    {
        private string UserAgent { get; set; }
        const string baseUrl = "https://www.humblebundle.com";
        const string choiceUrl = "https://www.humblebundle.com/membership";
        const string apiUrl = "https://www.humblebundle.com/api/v1";
        string ExtractProductDataFromHtml(string html, string needle = "<script id=\"webpack-monthly-product-data\" type=\"application/json\">")
        {
            var needleExists = html.IndexOf(needle) > -1;

            if (!needleExists)
            {
                return null;
            }

            var searchStart = html.IndexOf(needle) + needle.Length;
            var endTagLocation = html.IndexOf("</script>", searchStart);

            if (endTagLocation > -1)
            {
                return html.Substring(searchStart, endTagLocation - searchStart);
            }

            return null;
        }

        bool ChoicesAvailable(JsonDocument productData)
        {
            var initialKeyExists = productData.RootElement.GetProperty("contentChoiceOptions").GetProperty("contentChoiceData").TryGetProperty("initial", out var initial);

            if (!initialKeyExists)
            {
                return true;
            }

            initial.TryGetProperty("total_choices", out var choiceLimit);

            var contentChoiceOptions = productData.RootElement.GetProperty("contentChoiceOptions");

            if (!contentChoiceOptions.TryGetProperty("contentChoicesMade", out var _))
            {
                return true;
            }

            var numChoicesMade = contentChoiceOptions.GetProperty("contentChoicesMade").GetProperty("initial").GetProperty("choices_made").GetArrayLength();

            return choiceLimit.GetInt32() > numChoicesMade;
        }

        private static readonly ILogger logger = LogManager.GetLogger();

        public ILogger GetLogger()
        {
            return logger;
        }

        private HumbleChoiceUnselectedSettingsViewModel settings { get; set; }

        public override Guid Id { get; } = Guid.Parse("64ca9178-daac-44e7-a80b-0a33efaa9e6e");

        // Change to something more appropriate
        public override string Name => "Humble Choice (Unselected)";

        // Implementing Client adds ability to open it via special menu in playnite.
        public override LibraryClient Client { get; } = new HumbleChoiceUnselectedClient();

        public override string LibraryIcon => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"icon.png");

        public HumbleChoiceUnselected(IPlayniteAPI api) : base(api)
        {
            settings = new HumbleChoiceUnselectedSettingsViewModel(this);
            Properties = new LibraryPluginProperties
            {
                HasSettings = true
            };
            UserAgent = $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/96.0.4664.110 Safari/537.36 Playnite/{api.ApplicationInfo.ApplicationVersion.ToString(2)}";
        }

        private IEnumerable<GameMetadata> ProcessProduct(string rawProductData)
        {
            var productData = JsonDocument.Parse(rawProductData);

            var title = productData.RootElement.GetProperty("contentChoiceOptions").GetProperty("title").ToString();
            var productUrl = $"{choiceUrl}/{productData.RootElement.GetProperty("contentChoiceOptions").GetProperty("productUrlPath").ToString()}";

            if (!productData.RootElement.GetProperty("contentChoiceOptions").TryGetProperty("gamekey", out var _))
            {
                logger.Info($"No gamekey found so skipping.");
                return new List<GameMetadata>();
            }

            if (!ChoicesAvailable(productData))
            {
                logger.Info($"No choices available for {title}");
                return new List<GameMetadata>();
            }

            var alreadyClaimedTitles = new String[] { };

            if (productData.RootElement.GetProperty("contentChoiceOptions").TryGetProperty("contentChoicesMade", out var choicesMade))
            {
                alreadyClaimedTitles = choicesMade.GetProperty("initial").GetProperty("choices_made").EnumerateArray().Select(x => x.ToString()).ToArray();
            }

            var contentChoiceData = productData.RootElement.GetProperty("contentChoiceOptions").GetProperty("contentChoiceData");

            JsonElement.ObjectEnumerator allTitles = new JsonElement.ObjectEnumerator();

            if (contentChoiceData.TryGetProperty("game_data", out var gameData))
            {
                allTitles = gameData.EnumerateObject();
            }
            else
            {
                foreach (var possibleKey in new string[] { "initial", "initial-get-all-games" })
                {
                    if (contentChoiceData.TryGetProperty(possibleKey, out var key))
                    {
                        allTitles = key.GetProperty("content_choices").EnumerateObject();
                    }
                }
            }

            logger.Info($"Found {allTitles.Count()} games in {title}");

            if (allTitles.Count() == 0)
            {
                logger.Error("allTitles.Count() was zero, either there are no games in the product (unlikely) or we aren't looking at the correct JSON key");
            }

            var unclaimedTitles = allTitles.Where(x => !alreadyClaimedTitles.Contains(x.Name));
            var unclaimedChoiceMetaDatas = unclaimedTitles.Select((x) =>
            {
                var gameName = x.Value.GetProperty("title").ToString();

                logger.Info(gameName);

                return new GameMetadata()
                {
                    Name = gameName,
                    GameId = MakeID(gameName, title),
                    Source = new MetadataNameProperty(title),
                    Links = new List<Link> { new Link("Redemption Page", $"{productUrl}/{x.Name}") }
                };
            });

            return unclaimedChoiceMetaDatas;
        }

        private void RemoveClaimedAndExpiredGames(List<GameMetadata> fromHumble)
        {
            logger.Info($"Removing games that have been claimed or whose keys have expired");
            var library = PlayniteApi.Database.Games;
            var HcUnselectedLibrary = library.Where(x => x.GameId.StartsWith($"{Id.ToString()}/"));

            var idsFromHumble = new HashSet<string>(fromHumble.Select(g => g.GameId), StringComparer.OrdinalIgnoreCase);
            var toRemove = HcUnselectedLibrary.Where(g => !idsFromHumble.Contains(g.GameId)).ToList();
            using (PlayniteApi.Database.BufferedUpdate())
            {
                toRemove.ForEach(g => PlayniteApi.Database.Games.Remove(g.Id));
            }

            if (toRemove.Count > 0)
            {
                var notification = new NotificationMessage(
                        "Games Claimed or Expired",
                        $"The following Humble games were either claimed or their keys expired: {String.Join(", ", toRemove.Select(g => g.Name))}",
                        NotificationType.Info
                        );
                PlayniteApi.Notifications.Add(notification);
            }

            var idsInLibrary = new HashSet<string>(HcUnselectedLibrary.Select(g => g.GameId));
            var addedIds = idsFromHumble.Where(g => !idsInLibrary.Contains(g));
            var addedGames = fromHumble.Where(g => addedIds.Contains(g.GameId)).ToList();
            if (addedGames.Count > 0 && addedGames.Count < idsFromHumble.Count)
            {
                var notification = new NotificationMessage(
                        "Humble Choice Games Added",
                        $"The following games are now available in Humble: {String.Join(", ", addedGames.Select(g => g.Name))}",
                        NotificationType.Info
                        );
                PlayniteApi.Notifications.Add(notification);
            }
        }


        private string MakeID(string name, string source)
        {
            return $"{Id.ToString()}/{name}/{source}";
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            HttpClient client = GetHumbleClient();
            try
            {
                var games = GetChoiceGames(client).GetAwaiter().GetResult().ToList();
                if (settings.Settings.ImportEntitlements)
                {
                    var entitlements = GetEntitlements(client).GetAwaiter().GetResult().ToList();
                    games.AddRange(entitlements);
                }
                RemoveClaimedAndExpiredGames(games);
                return games;
            }
            catch (AggregateException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private async Task<IEnumerable<GameMetadata>> GetChoiceGames(HttpClient client)
        {
            var gameList = new List<GameMetadata>();

            try
            {
                var currentMonthData = await client.GetStringAsync($"{choiceUrl}/home");
                var currentMonthDataJson = ExtractProductDataFromHtml(currentMonthData, "<script id=\"webpack-subscriber-hub-data\" type=\"application/json\">");

                if (currentMonthDataJson == null)
                {
                    logger.Error("Couldn't extract JSON data for current month");
                }
                else
                {
                    gameList.AddRange(ProcessProduct(currentMonthDataJson));
                }
            }
            catch (Exception e)
            {
                logger.Error("There was an error when fetching the data for the current month");
                logger.Error(e.ToString());
            }

            var cursor = "";

            while (cursor != null)
            {
                try
                {
                    var requestUrl = $"{apiUrl}/subscriptions/humble_monthly/subscription_products_with_gamekeys/{cursor}";

                    JsonDocument jsonData = await AuthenticatedApiRequest(client, requestUrl);

                    cursor = jsonData.RootElement.TryGetProperty("cursor", out var _) ? jsonData.RootElement.GetProperty("cursor").GetString() : null;

                    foreach (var product in jsonData.RootElement.GetProperty("products").EnumerateArray())
                    {

                        if (!product.TryGetProperty("title", out var title))
                        {
                            logger.Info($"No title found so bundle appears to be an old Humble Monthly bundle, stopping here.");
                            return gameList;
                        }

                        try
                        {
                            logger.Info($"Found {title}");

                            if (!product.TryGetProperty("gamekey", out var _))
                            {
                                logger.Info($"No gamekey found so skipping.");
                                continue;
                            }

                            var urlPath = product.GetProperty("productUrlPath").ToString();

                            var productUrl = $"{choiceUrl}/{urlPath}";

                            var page = await client.GetStringAsync(productUrl);
                            var rawProductData = ExtractProductDataFromHtml(page);

                            if (rawProductData == null)
                            {
                                logger.Error($"Could not extract JSON from {rawProductData}");
                                continue;
                            }


                            gameList.AddRange(ProcessProduct(rawProductData));
                        }
                        catch (Exception ex)
                        {
                            {
                                logger.Error($"Failed to extract games from {title}");
                                logger.Error(ex.ToString());
                            }
                        }
                    }
                }
                catch (WebException webException)
                {
                    var httpResponse = (HttpWebResponse)webException.Response;
                    if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                    {
                        logger.Info($"Request to {httpResponse.ResponseUri} returned 404 suggesting no further purchases to download.");
                        cursor = null;
                    }
                }
                catch (Exception ex)
                {
                    logger.Error("Caught a generic exception");
                    logger.Error(ex.ToString());
                    cursor = null;
                }
            }

            return gameList;
        }


        private static async Task<JsonDocument> AuthenticatedApiRequest(HttpClient client, string apiRequestUrl)
        {
            logger.Info($"Making request to: {apiRequestUrl}");

            var data = await client.GetStringAsync(apiRequestUrl);

            if (data.Contains("Humble Bundle - Log In"))
            {
                logger.Error("Invalid session cookie");
                throw new Exception("Invalid session cookie");
            }

            logger.Info("Appear to be logged in, parsing response");

            var jsonData = JsonDocument.Parse(data);

            logger.Info("Resonse parsed into JSON");
            return jsonData;
        }


        private HttpClient GetHumbleClient()
        {
            var handler = new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = new CookieContainer()
            };

            var client = new HttpClient(handler);
            var decryptedCookie = Dpapi.Unprotect(settings.Settings.Cookie, Id.ToString(), System.Security.Cryptography.DataProtectionScope.CurrentUser);
            handler.CookieContainer.Add(new Uri(baseUrl), new Cookie("_simpleauth_sess", decryptedCookie));
            string userAgent = $"PlaynitePlugin/1.0.0.2 HumbleChoiceUnselectedGames";
            logger.Info(userAgent);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

            return client;
        }


        private async Task<IEnumerable<GameMetadata>> GetEntitlements(HttpClient client)
        {
            try
            {
                var requestUrl = $"{baseUrl}/home/keys";
                var entitlementsPageHtml = await client.GetStringAsync(requestUrl);
                var userHomeJsonData = ExtractProductDataFromHtml(entitlementsPageHtml, "<script id=\"user-home-json-data\" type=\"application/json\">");
                var userHomeObject = JsonNode.Parse(userHomeJsonData).AsObject();

                if (userHomeObject.TryGetPropertyValue("gamekeys", out var keysNode))
                {
                    var keys = keysNode.AsArray().Select(k => k.GetValue<string>()).ToList();
                    var chunkSize = 40; // number that Humble's website uses
                    var chunkTasks = new List<Task<string>>();
                    for (int i = 0; i < keys.Count; i += chunkSize)
                    {
                        var chunk = keys.Skip(i).Take(chunkSize).ToList();
                        chunkTasks.Add(RequestKeyChunkInfo(client, chunk));
                    }
                    var responses = await Task.WhenAll(chunkTasks);
                    logger.Info("Finished retreiving keys");

                    var combinedJson = MergeResponses(responses);

                    // Log all unique properties found (for discovering properties that may not have shown up in my library)
                    var mergedProperties = new JsonObject();
                    CollectAllTpkProperties(combinedJson, mergedProperties);
                    logger.Info("Merged Properties from all_tpks:");
                    logger.Info(mergedProperties.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

                    return ConvertJsonToMetadata(combinedJson);
                }
            }
            catch (Exception e)
            {
                logger.Error("Couldn't fetch entitlements");
                logger.Error(e.ToString());
            }

            return new List<GameMetadata>();
        }


        private JsonObject MergeResponses(IEnumerable<string> responses)
        {
            var combinedJson = new JsonObject();

            foreach (var response in responses)
            {
                var parsedJson = JsonNode.Parse(response)?.AsObject();
                if (parsedJson != null)
                {
                    foreach (var prop in parsedJson)
                    {
                        if (!combinedJson.ContainsKey(prop.Key))
                        {
                            combinedJson[prop.Key] = prop.Value?.DeepClone();
                        }
                    }
                }
            }

            return combinedJson;
        }


        private void CollectAllTpkProperties(JsonObject rootObject, JsonObject mergedProperties)
        {
            foreach (var prop in rootObject)
            {
                if (prop.Value is JsonObject purchaseObject && purchaseObject.TryGetPropertyValue("tpkd_dict", out var tpkdNode))
                {
                    if (tpkdNode is JsonObject tpkdDict && tpkdDict.TryGetPropertyValue("all_tpks", out var allTpksNode) && allTpksNode is JsonArray allTpksArray)
                    {
                        foreach (var tpk in allTpksArray.OfType<JsonObject>())
                        {
                            foreach (var field in tpk)
                            {
                                if (!mergedProperties.ContainsKey(field.Key))
                                {
                                    mergedProperties[field.Key] = null;
                                }
                            }
                        }
                    }
                }
            }
        }


        private List<GameMetadata> ConvertJsonToMetadata(JsonObject rootObject)
        {
            var metadata = new List<GameMetadata>();
            foreach (var prop in rootObject)
            {
                if (prop.Value is JsonObject purchaseObject && purchaseObject.TryGetPropertyValue("tpkd_dict", out var tpkdNode))
                {
                    if (tpkdNode is JsonObject tpkdDict && tpkdDict.TryGetPropertyValue("all_tpks", out var allTpksNode) && allTpksNode is JsonArray allTpksArray)
                    {
                        foreach (var tpk in allTpksArray.OfType<JsonObject>())
                        {
                            tpk.TryGetPropertyValue("is_expired", out var isExpiredNode);
                            var isExpired = JsonSerializer.Deserialize<bool>(isExpiredNode);
                            if (!tpk.TryGetPropertyValue("redeemed_key_val", out _)
                                    && !tpk.TryGetPropertyValue("redeeming_giftemail", out _)
                                    && !isExpired) // TODO: I don't know if it's redeemable if is_gift == true
                            {
                                tpk.TryGetPropertyValue("human_name", out var nameNode);
                                var name = nameNode.ToString();
                                tpk.TryGetPropertyValue("gamekey", out var gamekeyNode);
                                var gameKey = gamekeyNode.ToString();
                                if (tpk.TryGetPropertyValue("num_days_until_expired", out var numDaysUntilExpiredNode))
                                {
                                    var days = JsonSerializer.Deserialize<int>(numDaysUntilExpiredNode);
                                    if (days <= 30 && days > -1) // TODO: Make this a setting
                                    {
                                        var notification = new NotificationMessage(
                                                "Humble Key Expiring",
                                                $"The key to {name} is expiring in {days} days",
                                                NotificationType.Info,
                                                () => StartUrl($"{baseUrl}/download?key={gameKey}")
                                                );
                                        PlayniteApi.Notifications.Add(notification);
                                    }
                                }
                                metadata.Add(new GameMetadata()
                                {
                                    Name = name,
                                    GameId = MakeID(name, "Humble Entitlements"),
                                    Source = new MetadataNameProperty("Humble Entitlements"),
                                    Links = new List<Link> { new Link("Redemption Page", $"{baseUrl}/download?key={gameKey}") }
                                });
                            }
                        }
                    }
                }
            }
            return metadata;
        }


        private async Task<string> RequestKeyChunkInfo(HttpClient client, List<string> keys)
        {
            logger.Info($"Processing chunk with {keys.Count} keys");
            var requestUrl = $"{apiUrl}/orders?all_tpkds=true&{string.Join("&", keys.Select(k => $"gamekeys={k}"))}";
            return await client.GetStringAsync(requestUrl);
        }


        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }


        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new HumbleChoiceUnselectedSettingsView();
        }

        public static Process StartUrl(string url)
        {
            logger.Debug($"Opening URL: {url}");
            try
            {
                if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    logger.Debug($"URL {url} is not well formatted. Start aborted.");
                    return null;
                }

                return Process.Start(url);
            }
            catch (Exception e)
            {
                // There are some crash report with 0x80004005 error when opening standard URL.
                logger.Error(e, "Failed to open URL.");
                return Process.Start(CmdLineTools.Cmd, $"/C start {url}");
            }
        }
    }

    public static class CmdLineTools
    {
        public const string TaskKill = "taskkill";
        public const string Cmd = "cmd";
        public const string IPConfig = "ipconfig";
    }
}
