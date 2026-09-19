using System.Diagnostics;
using System.Text.Json.Serialization;
using Fulfillment.Api.Middleware;
using Fulfillment.Api.Swagger;
using Fulfillment.Application;
using Fulfillment.Infrastructure;
using Fulfillment.Infrastructure.Persistence;
using Swashbuckle.AspNetCore.Filters;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Every error body, whichever part of the pipeline produced it (MVC model validation, routing and status-code
// pages, or the exception handler), gets the same correlation fields from this one place.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var http = context.HttpContext;
    context.ProblemDetails.Instance ??= $"{http.Request.Method} {http.Request.Path}";
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? http.TraceIdentifier;
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerExamplesFromAssemblyOf<Program>();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Fulfillment API", Version = "v1" });
    options.ExampleFilters();
    options.OperationFilter<DemoOperationFilter>();

    var xml = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xml))
    {
        options.IncludeXmlComments(xml);
    }
});

builder.Services.AddHealthChecks().AddDbContextCheck<FulfillmentDbContext>("database");

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(
    app.Services,
    migrate: app.Configuration.GetValue("Database:MigrateOnStartup", true),
    seed: app.Configuration.GetValue("Database:SeedSampleData", false));

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposed so the integration tests can bootstrap the app with WebApplicationFactory<Program>.
public partial class Program;
