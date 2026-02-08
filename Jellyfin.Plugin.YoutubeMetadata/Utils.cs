using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using Jellyfin.Plugin.YoutubeMetadata.Configuration;
using NYoutubeDL;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.YoutubeMetadata
{
    /// <summary>
    /// Represents a search result from YouTube.
    /// </summary>
    public class YTSearchResult
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string ChannelId { get; set; }
        public string Uploader { get; set; }
        public string ThumbnailUrl { get; set; }
    }

    public class Utils
    {
        private static readonly Regex EpisodeRegex = new(@"(?:episode|folge|ep\.?)\s*([0-9]{1,4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SeasonEpisodeRegex = new(@"s([0-9]{1,2})e([0-9]{1,3})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsFresh(MediaBrowser.Model.IO.FileSystemMetadata fileInfo)
        {
            if (fileInfo.Exists && DateTime.UtcNow.Subtract(fileInfo.LastWriteTimeUtc).Days <= 10)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        ///  Returns the Youtube ID from the file path. Matches last 11 character field inside square brackets.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static string GetYTID(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var match = Regex.Match(name, Constants.YTID_RE);
            if (!match.Success)
            {
                match = Regex.Match(name, Constants.YTCHANNEL_RE);
            }
            return match.Value;
        }

        public static string BuildSearchQuery(string title, string path)
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                return CleanupSearchText(title);
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var filename = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            return CleanupSearchText(filename);
        }

        public static string CleanupSearchText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var withoutId = Regex.Replace(value, @"\[[a-zA-Z0-9\-_]{11,24}\]", string.Empty);
            var withoutSeparators = Regex.Replace(withoutId, @"[_\.]+", " ");
            return Regex.Replace(withoutSeparators, @"\s+", " ").Trim();
        }

        public static string ToSafeCacheKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append(invalidChars.Contains(character) ? '_' : character);
            }

            var cleaned = CleanupSearchText(builder.ToString()).Replace(' ', '_');
            return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
        }

        /// <summary>
        /// Creates a person object of type director for the provided name.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="channel_id"></param>
        /// <returns></returns>
        public static PersonInfo CreatePerson(string name, string channel_id)
        {
            return new PersonInfo
            {
                Name = name,
                Type = PersonKind.Director,
                ProviderIds = channel_id is null ?
                        new Dictionary<string, string> {} : new Dictionary<string, string> {
                                { Constants.PluginName, channel_id }
                        },
            };
        }

        /// <summary>
        /// Returns path to where metadata json file should be.
        /// </summary>
        /// <param name="appPaths"></param>
        /// <param name="youtubeID"></param>
        /// <returns></returns>
        public static string GetVideoInfoPath(IServerApplicationPaths appPaths, string youtubeID)
        {
            var dataPath = Path.Combine(appPaths.CachePath, "youtubemetadata", youtubeID);
            return Path.Combine(dataPath, "ytvideo.info.json");
        }

        public static async Task<string> SearchChannel(string query, IServerApplicationPaths appPaths, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ytd = new YoutubeDLP();
            var url = String.Format(Constants.SearchQuery, System.Web.HttpUtility.UrlEncode(query));
            ytd.Options.VerbositySimulationOptions.Simulate = true;
            ytd.Options.GeneralOptions.FlatPlaylist = true;
            ytd.Options.VideoSelectionOptions.PlaylistItems = "1";
            ytd.Options.VerbositySimulationOptions.Print = "url";
            List<string> ytdl_errs = new();
            List<string> ytdl_out = new();
            ytd.StandardErrorEvent += (sender, error) => ytdl_errs.Add(error);
            ytd.StandardOutputEvent += (sender, output) => ytdl_out.Add(output);
            var cookie_file = Path.Join(appPaths.PluginsPath, Constants.PluginName, "cookies.txt");
            if (File.Exists(cookie_file))
            {
                ytd.Options.FilesystemOptions.Cookies = cookie_file;
            }
            var task = ytd.DownloadAsync(url);
            await task;
            if (ytdl_out.Count > 0)
            {
                Uri uri = new Uri(ytdl_out[0]);
                return uri.Segments[uri.Segments.Length - 1].Trim('/');
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Searches YouTube for videos matching the query.
        /// </summary>
        /// <param name="query">Search query string.</param>
        /// <param name="appPaths">Application paths for cookie file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="maxResults">Maximum number of results to return.</param>
        /// <returns>List of search results.</returns>
        public static async Task<List<YTSearchResult>> SearchVideos(string query, IServerApplicationPaths appPaths, CancellationToken cancellationToken, int maxResults = 10)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = new List<YTSearchResult>();
            var ytd = new YoutubeDLP();
            var url = String.Format(Constants.VideoSearchQuery, System.Web.HttpUtility.UrlEncode(query));
            ytd.Options.VerbositySimulationOptions.Simulate = true;
            ytd.Options.GeneralOptions.FlatPlaylist = true;
            ytd.Options.VideoSelectionOptions.PlaylistItems = $"1:{maxResults}";
            // Print unit-separator-separated: id, title, channel_id, uploader, thumbnail
            ytd.Options.VerbositySimulationOptions.Print = "%(id)s\x1f%(title)s\x1f%(channel_id)s\x1f%(uploader)s\x1f%(thumbnail)s";
            List<string> ytdl_out = new();
            ytd.StandardOutputEvent += (sender, output) => ytdl_out.Add(output);
            var cookie_file = Path.Join(appPaths.PluginsPath, Constants.PluginName, "cookies.txt");
            if (File.Exists(cookie_file))
            {
                ytd.Options.FilesystemOptions.Cookies = cookie_file;
            }
            await ytd.DownloadAsync(url);
            foreach (var line in ytdl_out)
            {
                var parts = line.Split('\x1f');
                if (parts.Length >= 5)
                {
                    results.Add(new YTSearchResult
                    {
                        Id = parts[0],
                        Title = parts[1],
                        ChannelId = parts[2],
                        Uploader = parts[3],
                        ThumbnailUrl = parts[4]
                    });
                }
            }
            return results;
        }

        public static async Task<YTSearchResult> SearchBestVideoMatch(
            string query,
            IServerApplicationPaths appPaths,
            CancellationToken cancellationToken,
            int maxResults = 10)
        {
            var safeLimit = Math.Max(1, Math.Min(maxResults, 25));
            var cleanedQuery = CleanupSearchText(query);
            if (string.IsNullOrWhiteSpace(cleanedQuery))
            {
                return null;
            }

            var results = await SearchVideos(cleanedQuery, appPaths, cancellationToken, safeLimit).ConfigureAwait(false);
            if (results.Count == 0)
            {
                return null;
            }

            var scored = results
                .Select(item => new { Item = item, Score = ScoreSearchResult(cleanedQuery, item.Title) })
                .OrderByDescending(pair => pair.Score)
                .ThenBy(pair => pair.Item.Title?.Length ?? int.MaxValue)
                .FirstOrDefault();

            if (scored == null || scored.Score <= 0)
            {
                return results[0];
            }

            return scored.Item;
        }

        private static double ScoreSearchResult(string query, string candidate)
        {
            var normalizedQuery = CleanupSearchText(query).ToLowerInvariant();
            var normalizedCandidate = CleanupSearchText(candidate).ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(normalizedQuery) || string.IsNullOrWhiteSpace(normalizedCandidate))
            {
                return 0;
            }

            if (string.Equals(normalizedQuery, normalizedCandidate, StringComparison.Ordinal))
            {
                return 10;
            }

            if (normalizedCandidate.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                return 8;
            }

            var queryTokens = Tokenize(normalizedQuery);
            var candidateTokens = Tokenize(normalizedCandidate);
            if (queryTokens.Count == 0 || candidateTokens.Count == 0)
            {
                return 0;
            }

            var overlap = queryTokens.Intersect(candidateTokens).Count();
            return (double)overlap / queryTokens.Count;
        }

        private static List<string> Tokenize(string value)
        {
            return value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length > 1)
                .ToList();
        }

        /// <summary>
        /// Searches YouTube for channels matching the query.
        /// </summary>
        /// <param name="query">Search query string.</param>
        /// <param name="appPaths">Application paths for cookie file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="maxResults">Maximum number of results to return.</param>
        /// <returns>List of search results.</returns>
        public static async Task<List<YTSearchResult>> SearchChannels(string query, IServerApplicationPaths appPaths, CancellationToken cancellationToken, int maxResults = 10)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = new List<YTSearchResult>();
            var ytd = new YoutubeDLP();
            var url = String.Format(Constants.SearchQuery, System.Web.HttpUtility.UrlEncode(query));
            ytd.Options.VerbositySimulationOptions.Simulate = true;
            ytd.Options.GeneralOptions.FlatPlaylist = true;
            ytd.Options.VideoSelectionOptions.PlaylistItems = $"1:{maxResults}";
            // Print unit-separator-separated: channel_id, uploader (channel name), thumbnail
            ytd.Options.VerbositySimulationOptions.Print = "%(id)s\x1f%(title)s\x1f%(thumbnail)s";
            List<string> ytdl_out = new();
            ytd.StandardOutputEvent += (sender, output) => ytdl_out.Add(output);
            var cookie_file = Path.Join(appPaths.PluginsPath, Constants.PluginName, "cookies.txt");
            if (File.Exists(cookie_file))
            {
                ytd.Options.FilesystemOptions.Cookies = cookie_file;
            }
            await ytd.DownloadAsync(url);
            foreach (var line in ytdl_out)
            {
                var parts = line.Split('\x1f');
                if (parts.Length >= 3)
                {
                    results.Add(new YTSearchResult
                    {
                        Id = parts[0],        // Channel ID
                        Title = parts[1],     // Channel name
                        ChannelId = parts[0], // Same as Id for channels
                        Uploader = parts[1],  // Same as Title for channels
                        ThumbnailUrl = parts[2]
                    });
                }
            }
            return results;
        }

        public static async Task<bool> ValidCookie(IServerApplicationPaths appPaths, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ytd = new YoutubeDLP();
            var task = ytd.DownloadAsync("https://www.youtube.com/playlist?list=WL");
            List<string> ytdl_errs = new();
            ytd.StandardErrorEvent += (sender, error) => ytdl_errs.Add(error);
            ytd.Options.VideoSelectionOptions.PlaylistItems = "0";
            ytd.Options.VerbositySimulationOptions.SkipDownload = true;
            var cookie_file = Path.Join(appPaths.PluginsPath, Constants.PluginName, "cookies.txt");
            if (File.Exists(cookie_file))
            {
                ytd.Options.FilesystemOptions.Cookies = cookie_file;
            }
            await task;

            foreach (string err in ytdl_errs)
            {
                var match = Regex.Match(err, @".*The playlist does not exist\..*");
                if (match.Success)
                {
                    return false;
                }
            }
            return true;
        }

        public static async Task GetChannelInfo(string id, string name, IServerApplicationPaths appPaths, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ytd = new YoutubeDLP();
            ytd.Options.VideoSelectionOptions.PlaylistItems = "0";
            ytd.Options.FilesystemOptions.WriteInfoJson = true;
            var dataPath = Path.Combine(appPaths.CachePath, "youtubemetadata", name, "ytvideo");
            ytd.Options.FilesystemOptions.Output = dataPath;
            var cookie_file = Path.Join(appPaths.PluginsPath, Constants.PluginName, "cookies.txt");
            if (File.Exists(cookie_file))
            {
                ytd.Options.FilesystemOptions.Cookies = cookie_file;
            }
            List<string> ytdl_errs = new();
            ytd.StandardErrorEvent += (sender, error) => ytdl_errs.Add(error);
            var task = ytd.DownloadAsync(String.Format(Constants.ChannelUrl, id));
            await task;
        }

        public static async Task YTDLMetadata(string id, IServerApplicationPaths appPaths, CancellationToken cancellationToken)
        {
            //var foo = await ValidCookie(appPaths, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var ytd = new YoutubeDLP();
            ytd.Options.FilesystemOptions.WriteInfoJson = true;
            ytd.Options.VerbositySimulationOptions.SkipDownload = true;
            var cookie_file = Path.Join(appPaths.PluginsPath, Constants.PluginName, "cookies.txt");
            if (File.Exists(cookie_file))
            {
                ytd.Options.FilesystemOptions.Cookies = cookie_file;
            }

            var dlstring = "https://www.youtube.com/watch?v=" + id;
            var dataPath = Path.Combine(appPaths.CachePath, "youtubemetadata", id, "ytvideo");
            ytd.Options.FilesystemOptions.Output = dataPath;

            List<string> ytdl_errs = new();
            ytd.StandardErrorEvent += (sender, error) => ytdl_errs.Add(error);
            var task = ytd.DownloadAsync(dlstring);
            await task;
        }

        /// <summary>
        /// Reads JSON data from file.
        /// </summary>
        /// <param name="metaFile"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static YTDLData ReadYTDLInfo(string fpath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string jsonString = File.ReadAllText(fpath);
            return JsonSerializer.Deserialize<YTDLData>(jsonString);
        }

        /// <summary>
        /// Provides a Movie Metadata Result from a json object.
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        public static MetadataResult<Movie> YTDLJsonToMovie(YTDLData json)
        {
            var item = new Movie();
            var result = new MetadataResult<Movie>
            {
                HasMetadata = true,
                Item = item
            };
            result.Item.Name = json.title;
            result.Item.Overview = json.description;
            var date = new DateTime(1970, 1, 1);
            try
            {
                date = DateTime.ParseExact(json.upload_date, "yyyyMMdd", CultureInfo.InvariantCulture);
            }
            catch
            {

            }
            result.Item.ProductionYear = date.Year;
            result.Item.PremiereDate = date;
            result.AddPerson(Utils.CreatePerson(json.uploader, json.channel_id));
            result.Item.ProviderIds.Add(Constants.PluginName, json.id);
            return result;
        }

        /// <summary>
        /// Provides a MusicVideo Metadata Result from a json object.
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        public static MetadataResult<MusicVideo> YTDLJsonToMusicVideo(YTDLData json)
        {
            var item = new MusicVideo();
            var result = new MetadataResult<MusicVideo>
            {
                HasMetadata = true,
                Item = item
            };
            result.Item.Name = String.IsNullOrEmpty(json.track) ? json.title : json.track;
            result.Item.Artists = new List<string> { json.artist };
            result.Item.Album = json.album;
            result.Item.Overview = json.description;
            var date = new DateTime(1970, 1, 1);
            try
            {
                date = DateTime.ParseExact(json.upload_date, "yyyyMMdd", CultureInfo.InvariantCulture);
            }
            catch
            {

            }
            result.Item.ProductionYear = date.Year;
            result.Item.PremiereDate = date;
            result.AddPerson(Utils.CreatePerson(json.uploader, json.channel_id));
            result.Item.ProviderIds.Add(Constants.PluginName, json.id);
            return result;
        }

        public static int? ExtractEpisodeNumber(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var seasonEpisodeMatch = SeasonEpisodeRegex.Match(title);
            if (seasonEpisodeMatch.Success && int.TryParse(seasonEpisodeMatch.Groups[2].Value, out int seasonEpisodeNumber))
            {
                return seasonEpisodeNumber;
            }

            var episodeMatch = EpisodeRegex.Match(title);
            if (episodeMatch.Success && int.TryParse(episodeMatch.Groups[1].Value, out int episodeNumber))
            {
                return episodeNumber;
            }

            return null;
        }

        public static string GetSeriesNameFromVideo(
            YTDLData json,
            string fallbackName,
            bool preferUploaderAsSeriesName)
        {
            if (preferUploaderAsSeriesName && !string.IsNullOrWhiteSpace(json?.uploader))
            {
                return json.uploader;
            }

            if (!string.IsNullOrWhiteSpace(json?.playlist_title))
            {
                return json.playlist_title;
            }

            if (!string.IsNullOrWhiteSpace(json?.uploader))
            {
                return json.uploader;
            }

            if (!string.IsNullOrWhiteSpace(fallbackName))
            {
                return fallbackName;
            }

            return string.Empty;
        }

        /// <summary>
        /// Provides a Episode Metadata Result from a json object.
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        public static MetadataResult<Episode> YTDLJsonToEpisode(
            YTDLData json,
            PluginConfiguration config = null,
            string fallbackTitle = null)
        {
            var item = new Episode();
            var result = new MetadataResult<Episode>
            {
                HasMetadata = true,
                Item = item
            };
            result.Item.Name = string.IsNullOrWhiteSpace(json.title) ? fallbackTitle : json.title;
            result.Item.Overview = json.description ?? string.Empty;
            var date = new DateTime(1970, 1, 1);
            try
            {
                date = DateTime.ParseExact(json.upload_date, "yyyyMMdd", CultureInfo.InvariantCulture);
            }
            catch
            {

            }
            result.Item.ProductionYear = date.Year;
            result.Item.PremiereDate = date;
            result.Item.ForcedSortName = date.ToString("yyyyMMdd") + "-" + result.Item.Name;
            result.AddPerson(Utils.CreatePerson(json.uploader, json.channel_id));

            var episodeNumber = ExtractEpisodeNumber(result.Item.Name);
            var shouldAutoIndex = config?.EnableAutoEpisodeIndexing ?? true;
            result.Item.IndexNumber = shouldAutoIndex && episodeNumber.HasValue
                ? episodeNumber.Value
                : 1;
            result.Item.ParentIndexNumber = 1;
            result.Item.ProviderIds.Add(Constants.PluginName, json.id);
            return result;
        }

        /// <summary>
        /// Provides a MusicVideo Metadata Result from a json object.
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        public static MetadataResult<Series> YTDLJsonToSeries(
            YTDLData json,
            PluginConfiguration config = null,
            string fallbackName = null)
        {
            var item = new Series();
            var result = new MetadataResult<Series>
            {
                HasMetadata = true,
                Item = item
            };
            var preferUploader = config?.PreferUploaderAsSeriesName ?? true;
            result.Item.Name = GetSeriesNameFromVideo(json, fallbackName, preferUploader);
            result.Item.Overview = string.IsNullOrWhiteSpace(json.description) ? "Series metadata from YouTube." : json.description;
            result.Item.ProviderIds.Add(Constants.PluginName, json.channel_id);
            return result;
        }
    }
}
