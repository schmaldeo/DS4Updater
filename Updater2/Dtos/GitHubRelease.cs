namespace DS4Updater.Dtos
{
    // Check this linkg for the avalaible options 
    // https://api.github.com/repos/schmaldeo/DS4Windows/releases/latest
    public record GitHubRelease(string tag_name);
}
