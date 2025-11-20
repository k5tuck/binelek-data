using System;
// using Binah.Client.Configuration;  // TODO: Uncomment when Binah.Client is available
using Binah.Context.Data;
using Binah.Context.Services;
using Binah.Core.Extensions;
using Binah.Core.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Prometheus;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Binah Context API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new()
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: 'Bearer {token}'",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new()
    {
        {
            new()
            {
                Reference = new()
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Configure JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret is not configured");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// Configure PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Database connection string is not configured");

builder.Services.AddDbContext<ContextDbContext>(options =>
    options.UseNpgsql(connectionString));

// Configure Embedding Service (Swappable Provider)
var embeddingProvider = builder.Configuration["EmbeddingService:Provider"]?.ToLower() ?? "ollama";

if (embeddingProvider == "ollama")
{
    builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("EmbeddingService:Ollama"));
    builder.Services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>();
    Log.Information("Using Ollama as embedding provider");
}
else if (embeddingProvider == "openai")
{
    builder.Services.Configure<OpenAIOptions>(builder.Configuration.GetSection("EmbeddingService:OpenAI"));
    builder.Services.AddHttpClient<IEmbeddingService, OpenAIEmbeddingService>();
    Log.Information("Using OpenAI as embedding provider");
}
else
{
    throw new InvalidOperationException($"Unknown embedding provider: {embeddingProvider}. Supported providers: ollama, openai");
}

// Configure Qdrant
builder.Services.Configure<QdrantOptions>(builder.Configuration.GetSection("Qdrant"));
builder.Services.AddSingleton<VectorSearchService>();

// Configure Qdrant Client for Kafka consumers
var qdrantConfig = builder.Configuration.GetSection("Qdrant");
var qdrantHost = qdrantConfig["Host"] ?? "localhost";
var qdrantPort = qdrantConfig.GetValue<int>("Port", 6334);
var qdrantUseHttps = qdrantConfig.GetValue<bool>("UseHttps", false);

builder.Services.AddSingleton(sp =>
{
    var client = new Qdrant.Client.QdrantClient(
        host: qdrantHost,
        port: qdrantPort,
        https: qdrantUseHttps
    );
    return client;
});

// Configure Ontology Client
// TODO: Uncomment when Binah.Client is available
// var ontologyServiceUrl = builder.Configuration["Services:OntologyService"]
//     ?? throw new InvalidOperationException("Ontology service URL is not configured");
//
// builder.Services.AddBinahClients(config =>
// {
//     config.OntologyServiceUrl = ontologyServiceUrl;
// });

// Register services
// builder.Services.AddScoped<EnrichmentService>();  // TODO: Uncomment when Binah.Ontology/Client is available
builder.Services.AddScoped<MetadataRepository>();

// Register Entity Enrichment Background Service (forwards to Ontology Service via Kafka)
builder.Services.AddHostedService<Binah.Context.Services.EntityEnrichmentService>();

// Register Kafka consumers for auto-embeddings
builder.Services.AddHostedService<Binah.Context.Consumers.EntityCreatedConsumer>();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck("postgresql", () =>
    {
        try
        {
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection connection string is not configured");
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.ExecuteScalar();
            return HealthCheckResult.Healthy("PostgreSQL is accessible");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"PostgreSQL unreachable: {ex.Message}");
        }
    }, tags: new[] { "db", "sql" })
    .AddAsyncCheck("qdrant", async (cancellationToken) =>
    {
        try
        {
            var qdrantConfig = builder.Configuration.GetSection("Qdrant");
            var host = qdrantConfig["Host"] ?? "localhost";
            var port = qdrantConfig.GetValue<int>("Port", 6334);
            var useHttps = qdrantConfig.GetValue<bool>("UseHttps", false);
            var protocol = useHttps ? "https" : "http";
            var qdrantUrl = $"{protocol}://{host}:{port}";

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var healthResponse = await client.GetAsync($"{qdrantUrl}/health", cancellationToken);
            if (!healthResponse.IsSuccessStatusCode)
                return HealthCheckResult.Unhealthy("Qdrant health check failed");

            var collectionsResponse = await client.GetAsync($"{qdrantUrl}/collections", cancellationToken);
            if (!collectionsResponse.IsSuccessStatusCode)
                return HealthCheckResult.Degraded("Qdrant accessible but collections endpoint failed");

            return HealthCheckResult.Healthy("Qdrant accessible");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Qdrant error: {ex.Message}");
        }
    }, tags: new[] { "vector", "qdrant" })
    .AddAsyncCheck("embedding-service", async (cancellationToken) =>
    {
        try
        {
            // Get embedding service from DI container
            using var scope = builder.Services.BuildServiceProvider().CreateScope();
            var embeddingService = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();

            // Test embedding generation
            var testText = "health check test";
            var embedding = await embeddingService.GenerateEmbeddingAsync(testText);
            var expectedDimension = embeddingService.GetEmbeddingDimension();

            return embedding.Length == expectedDimension
                ? HealthCheckResult.Healthy($"Embedding service operational ({embeddingService.GetProviderName()}, dimension: {expectedDimension})")
                : HealthCheckResult.Degraded($"Unexpected embedding dimension: {embedding.Length}, expected: {expectedDimension}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Embedding service error: {ex.Message}");
        }
    }, tags: new[] { "embedding", "ai" })
    .AddAsyncCheck("enrichment-data", async (cancellationToken) =>
    {
        try
        {
            // Get database context from DI container
            using var scope = builder.Services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContextDbContext>();

            var enrichmentCount = await db.EnrichmentHistory.CountAsync(cancellationToken);

            return enrichmentCount >= 0
                ? HealthCheckResult.Healthy($"{enrichmentCount} enrichment history records")
                : HealthCheckResult.Degraded("No enrichment history available");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Enrichment data check error: {ex.Message}");
        }
    }, tags: new[] { "db", "enrichment" })
    .AddAsyncCheck("embedding-metadata", async (cancellationToken) =>
    {
        try
        {
            // Get database context from DI container
            using var scope = builder.Services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContextDbContext>();

            var metadataCount = await db.EmbeddingMetadata.CountAsync(cancellationToken);

            return metadataCount > 0
                ? HealthCheckResult.Healthy($"{metadataCount} embedding metadata records")
                : HealthCheckResult.Degraded("No embedding metadata records found");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Embedding metadata check error: {ex.Message}");
        }
    }, tags: new[] { "db", "embedding" });

var app = builder.Build();

// Apply database migrations
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ContextDbContext>();
        dbContext.Database.Migrate();
        Log.Information("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to apply database migrations");
    }
}

// Initialize Qdrant collection
using (var scope = app.Services.CreateScope())
{
    try
    {
        var vectorSearchService = scope.ServiceProvider.GetRequiredService<VectorSearchService>();
        await vectorSearchService.EnsureCollectionExistsAsync();
        Log.Information("Qdrant collection initialized successfully");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to initialize Qdrant collection");
    }
}

// Verify embedding service availability
using (var scope = app.Services.CreateScope())
{
    try
    {
        var embeddingService = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();
        var providerName = embeddingService.GetProviderName();
        var dimension = embeddingService.GetEmbeddingDimension();
        
        Log.Information("Embedding service initialized: {Provider} with dimension {Dimension}", providerName, dimension);

        // For Ollama, check if model is available
        if (embeddingService is OllamaEmbeddingService ollamaService)
        {
            var isAvailable = await ollamaService.IsAvailableAsync();
            if (!isAvailable)
            {
                Log.Warning("Ollama service is not available or model is not installed. Please ensure Ollama is running and the model is pulled.");
            }
            else
            {
                Log.Information("Ollama service is available and ready");
            }
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to initialize embedding service");
    }
}

// Configure middleware pipeline
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Prometheus metrics
app.UseMetricServer();
app.UseHttpMetrics();

Log.Information("Binah Context Service starting on {Environment}", app.Environment.EnvironmentName);

app.Run();

// Make Program accessible to integration tests
namespace Binah.Context
{
    public partial class Program { }
}
