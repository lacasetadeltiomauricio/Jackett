﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using CsQuery;
using Jackett.Common.Models;
using Jackett.Common.Models.IndexerConfig;
using Jackett.Common.Services.Interfaces;
using Jackett.Common.Utils;
using Newtonsoft.Json.Linq;
using NLog;
using Newtonsoft.Json;

namespace Jackett.Common.Indexers
{
    public class TorrentLeech : BaseWebIndexer
    {
        private string LoginUrl { get { return SiteLink + "user/account/login/"; } }
        private string SearchUrl { get { return SiteLink + "torrents/browse/list/"; } }

        private new ConfigurationDataBasicLogin configData
        {
            get { return (ConfigurationDataBasicLogin)base.configData; }
            set { base.configData = value; }
        }

        public TorrentLeech(IIndexerConfigurationService configService, Utils.Clients.WebClient wc, Logger l, IProtectionService ps)
            : base(name: "TorrentLeech",
                description: "This is what happens when you seed",
                link: "https://www.torrentleech.org/",
                caps: TorznabUtil.CreateDefaultTorznabTVCaps(),
                configService: configService,
                client: wc,
                logger: l,
                p: ps,
                downloadBase: "https://www.torrentleech.org/download/",
                configData: new ConfigurationDataBasicLogin("For best results, change the 'Default Number of Torrents per Page' setting to the maximum in your profile on the TorrentLeech webpage."))
        {
            Encoding = Encoding.GetEncoding("iso-8859-1");
            Language = "en-us";
            Type = "private";

            AddCategoryMapping(8, TorznabCatType.MoviesSD); // cam
            AddCategoryMapping(9, TorznabCatType.MoviesSD); //ts
            AddCategoryMapping(10, TorznabCatType.MoviesSD); // Sceener
            AddCategoryMapping(11, TorznabCatType.MoviesSD);
            AddCategoryMapping(12, TorznabCatType.MoviesSD);
            AddCategoryMapping(13, TorznabCatType.MoviesHD);
            AddCategoryMapping(14, TorznabCatType.MoviesHD);
            AddCategoryMapping(15, TorznabCatType.Movies); // Boxsets
            AddCategoryMapping(29, TorznabCatType.TVDocumentary);
            AddCategoryMapping(41, TorznabCatType.MoviesHD, "4K Upscaled");
            AddCategoryMapping(47, TorznabCatType.MoviesHD, "Real 4K UltraHD HDR");
            AddCategoryMapping(36, TorznabCatType.MoviesForeign);
            AddCategoryMapping(37, TorznabCatType.MoviesWEBDL);
            AddCategoryMapping(43, TorznabCatType.MoviesSD, "Movies/HDRip");

            AddCategoryMapping(26, TorznabCatType.TVSD);
            AddCategoryMapping(27, TorznabCatType.TV); // Boxsets
            AddCategoryMapping(32, TorznabCatType.TVHD);
            AddCategoryMapping(44, TorznabCatType.TVFOREIGN, "TV/Foreign");

            AddCategoryMapping(17, TorznabCatType.PCGames);
            AddCategoryMapping(18, TorznabCatType.ConsoleXbox);
            AddCategoryMapping(19, TorznabCatType.ConsoleXbox360);
            AddCategoryMapping(40, TorznabCatType.ConsoleXbox, "Games/XBOXONE");
            AddCategoryMapping(20, TorznabCatType.ConsolePS3); // PS2
            AddCategoryMapping(21, TorznabCatType.ConsolePS3);
            AddCategoryMapping(22, TorznabCatType.ConsolePSP);
            AddCategoryMapping(28, TorznabCatType.ConsoleWii);
            AddCategoryMapping(30, TorznabCatType.ConsoleNDS);
            AddCategoryMapping(39, TorznabCatType.ConsolePS4);
            AddCategoryMapping(42, TorznabCatType.PCMac, "Games/Mac");

            AddCategoryMapping(16, TorznabCatType.AudioVideo);
            AddCategoryMapping(31, TorznabCatType.Audio);

            AddCategoryMapping(34, TorznabCatType.TVAnime);
            AddCategoryMapping(35, TorznabCatType.TV); // Cartoons

            AddCategoryMapping(5, TorznabCatType.Books);
            AddCategoryMapping(45, TorznabCatType.BooksEbook, "Books/EBooks");
            AddCategoryMapping(46, TorznabCatType.BooksComics, "Books/Comics");

            AddCategoryMapping(23, TorznabCatType.PCISO);
            AddCategoryMapping(24, TorznabCatType.PCMac);
            AddCategoryMapping(25, TorznabCatType.PCPhoneOther);
            AddCategoryMapping(33, TorznabCatType.PC0day);

            AddCategoryMapping(38, TorznabCatType.Other, "Education");
        }

        public override async Task<IndexerConfigurationStatus> ApplyConfiguration(JToken configJson)
        {
            LoadValuesFromJson(configJson);
            await DoLogin();
            return IndexerConfigurationStatus.RequiresTesting;
        }

        private async Task DoLogin()
        {
            var pairs = new Dictionary<string, string> {
                { "username", configData.Username.Value },
                { "password", configData.Password.Value }
            };

            var result = await RequestLoginAndFollowRedirect(LoginUrl, pairs, null, true, null, LoginUrl);
            await ConfigureIfOK(result.Cookies, result.Content != null && result.Content.Contains("/user/account/logout"), () =>
            {
                CQ dom = result.Content;
                var errorMessage = dom["div#login_heading + div.card-panel-error"].Text();
                throw new ExceptionWithConfigData(errorMessage, configData);
            });
        }

        protected override async Task<IEnumerable<ReleaseInfo>> PerformQuery(TorznabQuery query)
        {
            var releases = new List<ReleaseInfo>();
            var searchString = query.GetQueryString();
            searchString = searchString.Replace('-', ' '); // remove dashes as they exclude search strings
            var searchUrl = SearchUrl;

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchUrl += "query/" + WebUtility.UrlEncode(searchString) + "/";
            }
            string.Format(SearchUrl, WebUtility.UrlEncode(searchString));

            var cats = MapTorznabCapsToTrackers(query);
            if (cats.Count > 0)
            {
                searchUrl += "categories/";
                foreach (var cat in cats)
                {
                    if (!searchUrl.EndsWith("/"))
                        searchUrl += ",";
                    searchUrl += cat;
                }
            }
            else
            {
                searchUrl += "newfilter/2"; // include 0day and music
            }

            var results = await RequestStringWithCookiesAndRetry(searchUrl);

            if (results.Content.Contains("/user/account/login"))
            {
                //Cookie appears to expire after a period of time or logging in to the site via browser
                await DoLogin();
                results = await RequestStringWithCookiesAndRetry(searchUrl);
            }

            try
            {
                dynamic jsonObj = JsonConvert.DeserializeObject(results.Content);

                foreach (var torrent in jsonObj.torrentList)
                {
                    var release = new ReleaseInfo();

                    release.MinimumRatio = 1;
                    release.MinimumSeedTime = 172800;

                    release.Guid = new Uri(SiteLink + "torrent/" + torrent.fid);
                    release.Comments = release.Guid;
                    release.Title = torrent.name;

                    if (!query.MatchQueryStringAND(release.Title))
                        continue;

                    release.Link = new Uri(SiteLink + "download/" + torrent.fid + "/" + torrent.filename);

                    release.PublishDate = DateTimeUtil.UnixTimestampToDateTime(ParseUtil.CoerceLong(torrent.addedTimestamp.ToString()));

                    release.Size = (long)torrent.size;

                    release.Seeders = ParseUtil.CoerceInt(torrent.seeders.ToString());
                    release.Peers = release.Seeders + ParseUtil.CoerceInt(torrent.leechers.ToString());

                    release.Category = MapTrackerCatToNewznab(torrent.categoryID.ToString());

                    release.Grabs = ParseUtil.CoerceInt(torrent.completed.ToString());

                    release.Imdb = ParseUtil.GetImdbID(torrent.imdbID.ToString());

                    release.DownloadVolumeFactor = 1;
                    release.UploadVolumeFactor = 1;

                    releases.Add(release);
                }
            }
            catch (Exception ex)
            {
                OnParseError(results.Content, ex);
            }

            return releases;
        }
    }
}
