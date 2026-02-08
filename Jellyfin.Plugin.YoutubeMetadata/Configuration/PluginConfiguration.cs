using MediaBrowser.Model.Plugins;
using System;

public enum IDTypes
{
    YTDLP = 1,
    TubeArchivist = 2
}

namespace Jellyfin.Plugin.YoutubeMetadata.Configuration
{

    public class PluginConfiguration : BasePluginConfiguration
    {
        public IDTypes IDType { get; set; }

        public bool EnableTitleLookupWithoutId { get; set; }

        public int SearchResultLimit { get; set; }

        public bool EnableAutoEpisodeIndexing { get; set; }

        public bool PreferUploaderAsSeriesName { get; set; }

        public bool EnableAiMetadataCleanup { get; set; }

        public bool EnableAiDescriptionCleanup { get; set; }

        public string AiApiKey { get; set; }

        public string AiBaseUrl { get; set; }

        public string AiModel { get; set; }

        public PluginConfiguration()
        {
            // defaults
            IDType = IDTypes.YTDLP;
            EnableTitleLookupWithoutId = true;
            SearchResultLimit = 10;
            EnableAutoEpisodeIndexing = true;
            PreferUploaderAsSeriesName = true;
            EnableAiMetadataCleanup = false;
            EnableAiDescriptionCleanup = false;
            AiApiKey = string.Empty;
            AiBaseUrl = "https://api.openai.com/v1";
            AiModel = "gpt-4o-mini";
        }
    }
}
