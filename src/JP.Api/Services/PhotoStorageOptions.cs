namespace JP.Api.Services;

/// <summary>Bound to the "PhotoStorage" section of appsettings.</summary>
public class PhotoStorageOptions
{
    public const string SectionName = "PhotoStorage";

    /// <summary>Folder where files are written, relative to the app content root.</summary>
    public string Directory { get; set; } = "uploads";

    /// <summary>URL prefix under which the folder is served.</summary>
    public string RequestPath { get; set; } = "/uploads";
}
