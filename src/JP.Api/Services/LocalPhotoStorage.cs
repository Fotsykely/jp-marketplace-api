using Microsoft.Extensions.Options;

namespace JP.Api.Services;

/// <summary>Stores photos on the local disk. Fine for development, not for several servers.</summary>
public class LocalPhotoStorage : IPhotoStorage
{
    private readonly string _root;
    private readonly string _requestPath;

    public LocalPhotoStorage(IOptions<PhotoStorageOptions> options, IWebHostEnvironment env)
    {
        _root = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.Directory));
        _requestPath = options.Value.RequestPath.TrimEnd('/');
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default)
    {
        // Never reuse the client's file name: a generated name rules out path traversal.
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

        await using var file = File.Create(Path.Combine(_root, fileName));
        await content.CopyToAsync(file, ct);

        return $"{_requestPath}/{fileName}";
    }

    public Task DeleteAsync(string url, CancellationToken ct = default)
    {
        // Only the last segment counts, so a crafted URL cannot point outside the folder.
        var path = Path.Combine(_root, Path.GetFileName(url));
        if (File.Exists(path)) File.Delete(path);

        return Task.CompletedTask;
    }
}
