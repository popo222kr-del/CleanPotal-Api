using Microsoft.JSInterop;

namespace ProductionManagement.Web.Services;

public sealed class BrowserFileService
{
    private readonly IJSRuntime _js;

    public BrowserFileService(IJSRuntime js) => _js = js;

    public async Task DownloadAsync(byte[] bytes, string fileName, string contentType)
    {
        await using var stream = new MemoryStream(bytes, writable: false);
        using var reference = new DotNetStreamReference(stream);
        await _js.InvokeVoidAsync("mesFiles.download", fileName, contentType, reference);
    }

    public Task DownloadAsync(string path, string? fileName = null, string contentType = "application/octet-stream")
        => DownloadAsync(File.ReadAllBytes(path), fileName ?? Path.GetFileName(path), contentType);

    public async Task OpenAsync(string path, string? fileName = null, string contentType = "application/octet-stream")
    {
        await using var stream = File.OpenRead(path);
        using var reference = new DotNetStreamReference(stream);
        await _js.InvokeVoidAsync("mesFiles.open", fileName ?? Path.GetFileName(path), contentType, reference);
    }
}
