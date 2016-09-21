﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsQuery;
using CsQuery.ExtensionMethods.Internal;
using Jackett.Models;
using Jackett.Models.IndexerConfig;
using Jackett.Services;
using Jackett.Utils;
using Jackett.Utils.Clients;
using Newtonsoft.Json.Linq;
using NLog;

namespace Jackett.Indexers
{
    // ReSharper disable once InconsistentNaming
    public class ILoveTorrents : BaseIndexer, IIndexer
    {
        private string BrowseUrl => SiteLink + "browse.php";
        private string LoginUrl => SiteLink + "takelogin.php";

        new ConfigurationDataBasicLogin configData
        {
            get { return (ConfigurationDataBasicLogin)base.configData; }
            set { base.configData = value; }
        }

        public ILoveTorrents(IIndexerManagerService i, IWebClient wc, Logger l, IProtectionService ps)
            : base(name: "ILoveTorrents",
                description: "ILT",
                link: "https://www.ilovetorrents.me/",
                caps: new TorznabCapabilities(),
                manager: i,
                client: wc,
                logger: l,
                p: ps,
                configData: new ConfigurationDataBasicLogin())
        {

            AddCategoryMapping(85, TorznabCatType.Movies3D);
            AddCategoryMapping(23, TorznabCatType.TVAnime);
            AddCategoryMapping(24, TorznabCatType.BooksEbook);
            AddCategoryMapping(4, TorznabCatType.PCGames);
            AddCategoryMapping(38, TorznabCatType.ConsolePS3);
            AddCategoryMapping(38, TorznabCatType.ConsolePS4);
            AddCategoryMapping(38, TorznabCatType.ConsolePSP);
            AddCategoryMapping(43, TorznabCatType.ConsoleWii);
            AddCategoryMapping(43, TorznabCatType.ConsoleWiiU);
            AddCategoryMapping(12, TorznabCatType.ConsoleXBOX360DLC);
            AddCategoryMapping(12, TorznabCatType.ConsoleXbox);
            AddCategoryMapping(12, TorznabCatType.ConsoleXbox360);
            AddCategoryMapping(12, TorznabCatType.ConsoleXboxOne);
            AddCategoryMapping(6, TorznabCatType.Audio);

            AddCategoryMapping(7, TorznabCatType.TV);
            AddCategoryMapping(40, TorznabCatType.TVSD);
            AddCategoryMapping(8, TorznabCatType.TVHD);

            AddCategoryMapping(9, TorznabCatType.XXX);
            AddCategoryMapping(11, TorznabCatType.XXXDVD);
            AddCategoryMapping(10, TorznabCatType.XXXx264);

            AddCategoryMapping(80, TorznabCatType.MoviesBluRay);
            AddCategoryMapping(20, TorznabCatType.MoviesDVD);
            AddCategoryMapping(41, TorznabCatType.MoviesHD);
            AddCategoryMapping(19, TorznabCatType.Movies);
        }

        public async Task<IndexerConfigurationStatus> ApplyConfiguration(JToken configJson)
        {
            configData.LoadValuesFromJson(configJson);
            var pairs = new Dictionary<string, string> {
                { "username", configData.Username.Value },
                { "password", configData.Password.Value },
                { "returnto", "/" },
                { "login", "Log in!" }
            };

            var loginPage = await RequestStringWithCookies(SiteLink, string.Empty);

            var result = await RequestLoginAndFollowRedirect(LoginUrl, pairs, loginPage.Cookies, true, SiteLink, SiteLink);
            await ConfigureIfOK(result.Cookies, result.Content != null && result.Content.Contains("logout.php"), () =>
            {
                CQ dom = result.Content;
                var messageEl = dom["body > div"].First();
                var errorMessage = messageEl.Text().Trim();
                throw new ExceptionWithConfigData(errorMessage, configData);
            });
            return IndexerConfigurationStatus.RequiresTesting;
        }

        public async Task<IEnumerable<ReleaseInfo>> PerformQuery(TorznabQuery query)
        {
            var releases = new List<ReleaseInfo>();
            var searchString = query.GetQueryString();
            var searchUrl = BrowseUrl;
            var trackerCats = MapTorznabCapsToTrackers(query);
            var queryCollection = new NameValueCollection();

            // Tracker can only search OR return things in categories
            if (!string.IsNullOrWhiteSpace(searchString))
            {
                queryCollection.Add("search", searchString);
                queryCollection.Add("cat", "0");
            }
            else
            {
                foreach (var cat in MapTorznabCapsToTrackers(query))
                {
                    queryCollection.Add("c" + cat, "1");
                }

                queryCollection.Add("incldead", "0");
            }

            searchUrl += "?" + queryCollection.GetQueryString();

            await ProcessPage(releases, searchUrl);
            return releases;
        }

        private async Task ProcessPage(List<ReleaseInfo> releases, string searchUrl)
        {
            var response = await RequestStringWithCookiesAndRetry(searchUrl, null, BrowseUrl);
            var results = response.Content;
            try
            {
                CQ dom = results;

                var rows = dom[".koptekst tr"];
                foreach (var row in rows.Skip(1))
                {
                    var release = new ReleaseInfo();

                    var link = row.Cq().Find("td:eq(1) a:eq(0)").First();
                    release.Guid = new Uri(SiteLink + link.Attr("href"));
                    release.Comments = release.Guid;
                    release.Title = link.Text().Trim(); 
                    release.Description = release.Title;

                    // If we search an get no results, we still get a table just with no info.
                    if (string.IsNullOrWhiteSpace(release.Title))
                    {
                        break;
                    }

                    // Check if the release has been assigned a category
                    if (row.Cq().Find("td:eq(0) a").Length > 0)
                    {
                        var cat = row.Cq().Find("td:eq(0) a").First().Attr("href").Substring(15);
                        release.Category = MapTrackerCatToNewznab(cat);
                    }

                    var qLink = row.Cq().Find("td:eq(2) a").First();
                    release.Link = new Uri(SiteLink + qLink.Attr("href"));

                    var added = row.Cq().Find("td:eq(7)").First().Text().Trim();
                    var date = added.Substring(0, 10);
                    var time = added.Substring(12, 8);
                    var dateTime = date + time;
                    release.PublishDate = DateTime.ParseExact(dateTime, "yyyy-MM-ddHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToLocalTime();

                    var sizeStr = row.Cq().Find("td:eq(8)").First().Text().Trim();
                    release.Size = ReleaseInfo.GetBytes(sizeStr);

                    release.Seeders = ParseUtil.CoerceInt(row.Cq().Find("td:eq(10)").First().Text().Trim());
                    release.Peers = ParseUtil.CoerceInt(row.Cq().Find("td:eq(11)").First().Text().Trim()) + release.Seeders;

                    releases.Add(release);
                }
            }
            catch (Exception ex)
            {
                OnParseError(results, ex);
            }
        }
    }
}
