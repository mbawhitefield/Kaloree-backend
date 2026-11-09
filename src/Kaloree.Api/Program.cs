using System.Text;
using System.Threading.RateLimiting;
using Kaloree.Api.Api;
using Kaloree.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.BearerToken;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => { o.IncludeScopes = true; o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ"; o.UseUtcTimestamp = true; });
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables();
builder.WebHost.ConfigureKestrel(o => { o.Limits.MaxRequestBodySize = 8 * 1024 * 1024; o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15); });
if (Environment.GetEnvironmentVariable("PORT") is { } port) builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
var connection = builder.Configuration.GetConnectionString("Database") ?? throw new InvalidOperationException("ConnectionStrings:Database is required");
var auth = builder.Configuration.GetSection("Auth");
if ((auth["Mode"] ?? "local").ToLowerInvariant() is not ("local" or "oidc"))
    throw new InvalidOperationException("Auth:Mode must be local or oidc");
var developmentAuth = builder.Environment.IsDevelopment() && auth.GetValue<bool>("DevelopmentTokens");
var localAuth = !string.Equals(auth["Mode"], "oidc", StringComparison.OrdinalIgnoreCase);
if (!localAuth && !developmentAuth && (!Uri.TryCreate(auth["Authority"], UriKind.Absolute, out var authority) || authority.Scheme != "https" || string.IsNullOrWhiteSpace(auth["Audience"])))
    throw new InvalidOperationException("An HTTPS Auth:Authority and Auth:Audience are required");
builder.Services.AddDbContext<AppDb>(o => o.UseNpgsql(connection, npg => npg.CommandTimeout(15)));
builder.Services.AddDataProtection().SetApplicationName("Kaloree").PersistKeysToDbContext<AppDb>();
builder.Services.AddIdentityApiEndpoints<IdentityUser>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.SignIn.RequireConfirmedEmail = true;
    o.Password.RequiredLength = 12;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<AppDb>();
builder.Services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, o =>
{
    o.BearerTokenExpiration = TimeSpan.FromMinutes(15);
    o.RefreshTokenExpiration = TimeSpan.FromDays(30);
});
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));
builder.Services.AddSingleton<IdentityEmail>();
builder.Services.AddSingleton<IEmailSender<IdentityUser>>(sp => sp.GetRequiredService<IdentityEmail>());
builder.Services.AddScoped<CurrentAccount>(); builder.Services.AddScoped<Quota>();
builder.Services.AddSingleton<PhotoCipher>();
builder.Services.AddHttpClient("food-catalog", c => c.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddSingleton(sp => new FoodCatalog(sp.GetRequiredService<IHttpClientFactory>().CreateClient("food-catalog"), sp.GetRequiredService<IConfiguration>()));
builder.Services.AddHttpClient<IMealVision, OpenAiVision>(c => c.Timeout = TimeSpan.FromSeconds(75));
builder.Services.AddHttpClient<ISubscriptionProvider, RevenueCat>(c => c.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddAuthentication(o =>
{
    o.DefaultScheme = localAuth && !developmentAuth ? IdentityConstants.BearerScheme : JwtBearerDefaults.AuthenticationScheme;
    o.DefaultAuthenticateScheme = o.DefaultScheme;
    o.DefaultChallengeScheme = o.DefaultScheme;
}).AddJwtBearer(o =>
{
    o.MapInboundClaims = false; o.RequireHttpsMetadata = true;
    if (developmentAuth)
    {
        var key = auth["DevelopmentSigningKey"] ?? "";
        if (key.Length < 64) throw new InvalidOperationException("Development signing key must have at least 64 characters");
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = "kaloree-local", ValidAudience = "kaloree-api",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateIssuer = true, ValidateAudience = true, ValidateIssuerSigningKey = true, ValidateLifetime = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.FromSeconds(30)
        };
    }
    else if (!localAuth)
    {
        o.Authority = auth["Authority"]; o.Audience = auth["Audience"];
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true, ValidateIssuerSigningKey = true, ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30), ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256]
        };
    }
});
builder.Services.AddAuthorization(); builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Kaloree API"; document.Info.Version = "v1";
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
    {
        ["bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = localAuth && !developmentAuth ? "Identity opaque token" : "JWT" },
        ["revenuecatHmac"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header,
            Name = "X-RevenueCat-Webhook-Signature", Description = "HMAC SHA-256 over timestamp.raw-body; see operations runbook." }
    };
    foreach (var path in document.Paths)
        foreach (var operation in path.Value.Operations?.Values.AsEnumerable() ?? Enumerable.Empty<OpenApiOperation>())
        {
            if (path.Key.StartsWith("/api/", StringComparison.Ordinal) &&
                (!path.Key.StartsWith("/api/v1/auth/", StringComparison.Ordinal) ||
                 path.Key.StartsWith("/api/v1/auth/manage/", StringComparison.Ordinal) || path.Key == "/api/v1/auth/logout")) operation.Security =
                [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("bearer", document)] = [] }];
            else if (path.Key == "/webhooks/revenuecat") operation.Security =
                [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("revenuecatHmac", document)] = [] }];
        }
    return Task.CompletedTask;
}));
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
    if (origins.Length > 0) p.WithOrigins(origins).WithMethods("GET", "POST", "PUT", "DELETE").WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "X-Confirm-Delete");
}));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", h => RateLimitPartition.GetFixedWindowLimiter(
        h.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(h =>
        RateLimitPartition.GetFixedWindowLimiter(h.User.FindFirst("sub")?.Value ?? h.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    foreach (var proxy in builder.Configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? [])
        o.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
});
var role = builder.Configuration["Service:Role"] ?? "api";
if (role is not ("api" or "worker" or "all")) throw new InvalidOperationException("Service:Role must be api, worker or all");
if (builder.Configuration.GetValue<bool>("FoodCatalog:Enabled") && string.IsNullOrWhiteSpace(builder.Configuration["FoodCatalog:UserAgent"]))
    throw new InvalidOperationException("FoodCatalog:UserAgent with support contact is required");
if (builder.Configuration.GetValue<bool>("AI:Enabled"))
{
    if (string.IsNullOrWhiteSpace(builder.Configuration["AI:ApiKey"]) || string.IsNullOrWhiteSpace(builder.Configuration["AI:Model"]))
        throw new InvalidOperationException("AI:ApiKey and AI:Model are required when AI is enabled");
    if (builder.Configuration.GetValue("AI:MonthlyScanLimit", 100) < 1 || builder.Configuration.GetValue("AI:DailyGlobalLimit", 1000) < 1)
        throw new InvalidOperationException("AI quotas must be positive");
}
if (builder.Configuration.GetValue<bool>("Billing:Enabled") &&
    (string.IsNullOrWhiteSpace(builder.Configuration["Billing:ApiKey"]) || (builder.Configuration["Billing:WebhookSecret"]?.Length ?? 0) < 32))
    throw new InvalidOperationException("Billing API key and webhook signing secret of at least 32 characters are required");
if (role is "worker" or "all") builder.Services.AddHostedService<AnalysisWorker>();
var app = builder.Build();
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await db.Database.OpenConnectionAsync();
    await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(708390127)");
    try { await db.Database.MigrateAsync(); }
    finally { await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(708390127)"); }
    return;
}
if (builder.Configuration.GetValue<bool>("AI:Enabled")) _ = app.Services.GetRequiredService<PhotoCipher>();
if (Array.IndexOf(args, "--import-foods") is var importIndex && importIndex >= 0)
{
    if (importIndex + 1 >= args.Length) throw new InvalidOperationException("--import-foods requires a JSON file path");
    using var scope = app.Services.CreateScope();
    await CatalogImport.Run(scope.ServiceProvider.GetRequiredService<AppDb>(), args[importIndex + 1], CancellationToken.None);
    return;
}
app.UseForwardedHeaders();
app.UseExceptionHandler(handler => handler.Run(async h =>
{
    var ex = h.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, code) = ex switch
    {
        ApiException a => (a.Status, a.Code),
        DbUpdateConcurrencyException => (409, "version_conflict"),
        DbUpdateException { InnerException: PostgresException { SqlState: "23505" } } => (409, "duplicate_resource"),
        DbUpdateException { InnerException: PostgresException { SqlState: "23503" } } => (409, "related_resource_missing"),
        System.Text.Json.JsonException or BadHttpRequestException or FormatException => (400, "invalid_request"),
        _ => (500, "internal_error")
    };
    if (status == 500) h.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Kaloree.Errors")
        .LogError("Unhandled request failure; type={Type}; traceId={TraceId}", ex?.GetType().Name, h.TraceIdentifier);
    h.Response.StatusCode = status;
    await Results.Problem(statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["traceId"] = h.TraceIdentifier }).ExecuteAsync(h);
}));
app.Use(async (h, next) =>
{
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    h.Response.OnStarting(() =>
    {
        h.Response.Headers["X-Content-Type-Options"] = "nosniff";
        h.Response.Headers["Referrer-Policy"] = "no-referrer";
        h.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        if (h.Request.Path.StartsWithSegments("/api")) h.Response.Headers.CacheControl = "no-store";
        if (!app.Environment.IsDevelopment()) h.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        return Task.CompletedTask;
    });
    h.Response.OnCompleted(() =>
    {
        var duration = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var route = (h.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
        Telemetry.RequestMilliseconds.Record(duration, new KeyValuePair<string, object?>("route", route),
            new KeyValuePair<string, object?>("status", h.Response.StatusCode));
        if (!h.Request.Path.StartsWithSegments("/health")) app.Logger.LogInformation(
            "HTTP {Method} {Route} {Status} {ElapsedMs}ms traceId={TraceId}", h.Request.Method, route,
            h.Response.StatusCode, Math.Round(duration, 1), h.TraceIdentifier);
        return Task.CompletedTask;
    });
    await next();
});
app.UseCors(); app.UseAuthentication(); app.UseRateLimiter();
if (localAuth && !developmentAuth) app.Use(LocalIdentity.ValidateLocalIdentity);
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", async (AppDb db, CancellationToken ct) =>
{
    try { await db.Accounts.AsNoTracking().Select(x => x.Id).Take(1).ToListAsync(ct); return Results.Ok(new { status = "ready" }); }
    catch (Exception) { return Results.StatusCode(503); }
}).AllowAnonymous();
if (role is "api" or "all")
{
    if (localAuth) app.MapLocalIdentity();
    var api = app.MapGroup("/api/v1").RequireAuthorization().AddEndpointFilter<AccountFilter>();
    api.MapProfile(); api.MapFoods(); api.MapDiary(); api.MapPlans(); api.MapAnalysis(); app.MapBilling();
    if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();
}
app.Run();
public partial class Program;
