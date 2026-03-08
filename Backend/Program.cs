using EmergencyPlatform.Agents;
using EmergencyPlatform.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Emergency Coordination API", Version = "v1" });
});

// HTTP clients
builder.Services.AddHttpClient("AzureOpenAI");
builder.Services.AddHttpClient();         // default factory for Speech / Maps

// Azure service wrappers
builder.Services.AddSingleton<AzureOpenAIService>();
builder.Services.AddSingleton<AzureSpeechService>();
builder.Services.AddSingleton<AzureMapsService>();
builder.Services.AddSingleton<ServiceBusPublisher>();

// Agents
builder.Services.AddScoped<NlpTextAgent>();
builder.Services.AddScoped<VisionAgent>();
builder.Services.AddScoped<SeverityAgent>();
builder.Services.AddScoped<DispatchAgent>();
builder.Services.AddScoped<InputNormalizerAgent>();

// CORS (allow Blazor frontend and any dev origin)
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader()));

// Increase multipart limits for image uploads
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 20 * 1024 * 1024;   // 20 MB
});

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.Run();
