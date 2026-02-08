namespace Jellyfin.Plugin.YoutubeMetadata
{
    class Constants
    {
        public const string PluginName = "YoutubeAutoMetadata";
        public const string PluginDisplayName = "YouTube Auto Metadata";
        public const string PluginGuid = "bb165877-2d2a-458d-aa02-52b9f632d974";
        public const string ChannelUrl = "https://www.youtube.com/channel/{0}";
        public const string VideoUrl = "https://www.youtube.com/watch?v={0}";
        public const string SearchQuery = "https://www.youtube.com/results?search_query={0}&sp=EgIQAg%253D%253D";
        public const string VideoSearchQuery = "https://www.youtube.com/results?search_query={0}&sp=EgIQAQ%253D%253D";

        public const string YTCHANNEL_RE = @"(?<=\[)[a-zA-Z0-9\-_]{24}(?=\])";
        public const string YTID_RE = @"(?<=\[)[a-zA-Z0-9\-_]{11}(?=\])";
    }
}
