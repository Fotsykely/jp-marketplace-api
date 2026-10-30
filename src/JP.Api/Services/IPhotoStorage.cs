namespace JP.Api.Services;

/// <summary>Where photo files live. Local disk today, cloud blob storage later.</summary>
public interface IPhotoStorage
{
    /// <summary>Stores the file and returns its public URL (e.g. "/uploads/3f2a....jpg").</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default);

    /// <summary>Removes a file previously returned by <see cref="SaveAsync"/>. No-op if missing.</summary>
    Task DeleteAsync(string url, CancellationToken ct = default);
}
