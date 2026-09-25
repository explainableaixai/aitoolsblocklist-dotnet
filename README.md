# AlphaQuantum.AIToolsBlocklist

.NET client for the AI tool register that helps companies [protect internal AI tools across the enterprise](https://www.aitoolsblocklist.com/enterprise-ai-blocking.php). Pass it a hostname and it reports whether the host belongs to an AI product, how that product is categorised, and what its terms say about training on customer data. It targets .NET 8 and uses only the base class library, `HttpClient` and `System.Text.Json`.

```bash
dotnet add package AlphaQuantum.AIToolsBlocklist
```

## Quick look

```csharp
using AlphaQuantum.AIToolsBlocklist;

var client = new AIToolsBlocklistClient(Environment.GetEnvironmentVariable("AQ_API_KEY")!);
var r = await client.CheckAsync("copilot.microsoft.com");

if (r["blocked"].GetBoolean())
    Console.WriteLine($"{r["domain"]}: {r["primary_category"]} ({r["trains_on_data"]})");
```

`CheckAsync` returns `Dictionary<string, JsonElement>`. Read values with the usual `JsonElement` accessors, such as `GetBoolean()`, `GetString()` or `EnumerateArray()`.

## Fields you will use

- **`blocked`**: `true` when the host belongs to a listed AI tool.
- **`primary_category`**: the tool's main function, for example writing, coding or image generation.
- **`ai_type`**: whether AI is the whole product or one feature of a larger one.
- **`categories`**: an array of objects with `category` and, where relevant, `subcategory`.
- **`matched_domain`**: present when a parent domain matched rather than the exact host.
- **`trains_on_data`**, **`opt_out_available`**, **`enterprise_no_training`**, **`api_no_training`**: the vendor's position on training, each `yes`, `no`, `opt_out_default` or `unstated`.
- **`terms_checked`**: when those terms were last read.

Fields that do not apply are absent, so use `TryGetValue` for the optional ones:

```csharp
var name = r.TryGetValue("matched_domain", out var m) ? m.GetString() : r["domain"].GetString();
```

## Registering it in ASP.NET Core

The constructor accepts an `HttpClient`, which fits `IHttpClientFactory` well. Register a typed client so connection pooling and handler lifetimes are managed for you:

```csharp
builder.Services.AddHttpClient<AIToolsBlocklistClient>((http, sp) =>
{
    http.Timeout = TimeSpan.FromSeconds(10);
    var key = builder.Configuration["AiToolsBlocklist:ApiKey"]!;
    return new AIToolsBlocklistClient(key, http);
});
```

Keep the key in user secrets during development and in Key Vault or environment variables in production. It never belongs in `appsettings.json` under source control.

## Caching with IMemoryCache

Traffic concentrates on a small set of hosts, so a cache removes most calls:

```csharp
public sealed class AiToolLookup(AIToolsBlocklistClient client, IMemoryCache cache)
{
    public Task<Dictionary<string, JsonElement>?> GetAsync(string host, CancellationToken ct) =>
        cache.GetOrCreateAsync(host.ToLowerInvariant(), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
            return client.CheckAsync(host, ct)!;
        });
}
```

Twelve hours matches how quickly the register changes for a typical host. Cache the "not an AI tool" answers as well, because they are most of the traffic.

## An internal lookup endpoint

Service desks and scripts often want a quick "is this AI?" answer without holding a key themselves. A minimal API endpoint does it:

```csharp
app.MapGet("/ai-check/{host}", async (string host, AiToolLookup lookup, CancellationToken ct) =>
{
    var r = await lookup.GetAsync(host, ct);
    return r is null ? Results.NotFound() : Results.Ok(new
    {
        host,
        isAiTool = r["blocked"].GetBoolean(),
        category = r.TryGetValue("primary_category", out var c) ? c.GetString() : null,
        trains = r.TryGetValue("trains_on_data", out var t) ? t.GetString() : null
    });
}).RequireAuthorization();
```

Put it behind your normal authentication, and the key stays on the server.

## Turning findings into policy

The register describes. Your policy decides. Keep the decision in one method so reviewers can read it:

```csharp
static string Decide(Dictionary<string, JsonElement> r)
{
    if (!r["blocked"].GetBoolean()) return "allow";
    return r.TryGetValue("trains_on_data", out var t) ? t.GetString() switch
    {
        "no" => "allow-and-log",
        "opt_out_default" => "warn",
        _ => "block"
    } : "block";
}
```

Microsoft-centric environments often feed the resulting block list into their endpoint or proxy tooling as custom indicators. For that, the downloadable list is a better source than per-host calls.

## Errors

- **`ArgumentException`**: thrown by the constructor for an empty key, and by `CheckAsync` for an empty host.
- **`ApiException`**: thrown for any non-success HTTP status. It exposes `StatusCode` and the raw `Body`. A 401 or 403 means the key is wrong or the monthly quota is used. A 429 means too many requests.
- **`TaskCanceledException`**: raised on timeout or when your `CancellationToken` fires.
- **`JsonException`**: raised if the body is not the expected JSON object.

The client does not retry. If you want retries, add a resilience handler to the typed client registration (for example with `Microsoft.Extensions.Http.Resilience`), and retry only on 429 and 5xx responses.

## Testing

The constructor's `HttpClient` parameter makes stubbing simple. Give it an `HttpClient` built on a custom `HttpMessageHandler` that returns canned JSON. The package's own tests do exactly that, and also check that the key travels in the `X-API-Key` header and that a 429 surfaces as `ApiException` with the right status.

## Knowing what to block first

Rules work better when they start from real usage. To [detect shadow AI tools that employees use without IT approval](https://www.shadowaitools.com/free-shadow-ai-audit.php), run the log audit before you write policy. If your organisation also runs its own AI agents, give them an [AI agent allow list with page-level limits](https://www.aiagentallowlist.com). And for everything outside AI, [enterprise web security](https://www.webfilteringdatabase.com) categories cover the rest of the web.

## Same register, other runtimes

The Go module [aitoolsblocklist-go](https://pkg.go.dev/github.com/explainableaixai/aitoolsblocklist-go) suits network services. For data work there is [the Python package](https://pypi.org/project/aitoolsblocklist/), and front-end teams use [the npm client](https://www.npmjs.com/package/aitoolsblocklist).

## License

MIT
