using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CleanPotal.Infrastructure.Services;

public sealed class PortalFileService : IPortalFileService
{
    private readonly CleanPotalDbContext _db;
    private readonly string[] _allowedRoots;

    public PortalFileService(CleanPotalDbContext db, IConfiguration configuration)
    {
        _db = db;
        _allowedRoots = configuration.GetSection("PortalFiles:AllowedRoots")
            .GetChildren()
            .Select(item => item.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizeRoot(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<PortalFileDescriptor?> ResolveAsync(
        int itemId,
        CancellationToken cancellationToken = default)
    {
        var item = await _db.PortalItems
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == itemId, cancellationToken);

        if (item is null || item.Type is "web" or "folder" || string.IsNullOrWhiteSpace(item.Path))
            return null;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(item.Path.Trim());
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (!Path.IsPathFullyQualified(fullPath) || !_allowedRoots.Any(root => IsWithinRoot(fullPath, root)))
            return null;

        if (!File.Exists(fullPath))
            return null;

        return new PortalFileDescriptor(fullPath, Path.GetFileName(fullPath));
    }

    private static string NormalizeRoot(string path)
        => Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsWithinRoot(string fullPath, string root)
        => fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
           || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
           || fullPath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
