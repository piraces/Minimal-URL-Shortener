using LiteDB;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.FileProviders;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ILiteDatabase, LiteDatabase>(_ => new LiteDatabase("short-links.db"));
await using var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Home page: A form for submitting a URL
app.MapGet("/", ctx =>
                {
                    ctx.Response.ContentType = "text/html";
                    return ctx.Response.SendFileAsync("index.html");
                });

// API endpoint for shortening a URL and save it to a local database
app.MapPost("/url", ShortenerDelegate);
app.MapGet("/url/{longUrl}", ShortenerDelegate);

var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
var assetDirectory = Path.Combine(assemblyDirectory, "assets");

// use it
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(assetDirectory),
    RequestPath = "/assets"
});

// Catch all page: redirecting shortened URL to its original address
app.MapFallback(RedirectDelegate);

await app.RunAsync();

static async Task ShortenerDelegate(HttpContext httpContext)
{
    var rawRequest = false;
    var url = string.Empty;

    if (httpContext.Request.RouteValues.TryGetValue("longUrl", out var longUrl))
    {
        url = longUrl.ToString();
        rawRequest = true;
    } else {
        var request = await httpContext.Request.ReadFromJsonAsync<UrlDto>();
        url = request.Url;
    }
    

    if (!Uri.TryCreate(url, UriKind.Absolute, out var inputUri))
    {
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await httpContext.Response.WriteAsync("URL is invalid.");
        return;
    }

    var liteDb = httpContext.RequestServices.GetRequiredService<ILiteDatabase>();
    var links = liteDb.GetCollection<ShortUrl>(BsonAutoId.Int32);
    var entry = new ShortUrl(inputUri);
    links.Insert(entry);

    var result = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/{entry.UrlChunk}";
    if (rawRequest) {
        await httpContext.Response.WriteAsync(result);
    } else {
        await httpContext.Response.WriteAsJsonAsync(new { url = result });
    }
}

static async Task RedirectDelegate(HttpContext httpContext)
{
    var db = httpContext.RequestServices.GetRequiredService<ILiteDatabase>();
    var collection = db.GetCollection<ShortUrl>();

    var path = httpContext.Request.Path.ToUriComponent().Trim('/');
    var id = BitConverter.ToInt32(WebEncoders.Base64UrlDecode(path));
    var entry = collection.Find(p => p.Id == id).FirstOrDefault();

    httpContext.Response.Redirect(entry?.Url ?? "/");

    await Task.CompletedTask;
}

public class ShortUrl
{
    public int Id { get; protected set; }
    public string Url { get; protected set; }
    public string UrlChunk => WebEncoders.Base64UrlEncode(BitConverter.GetBytes(Id));

    public ShortUrl(Uri url)
    {
        Url = url.ToString();
    }
}

public class UrlDto
{
    public string Url { get; set; }
}