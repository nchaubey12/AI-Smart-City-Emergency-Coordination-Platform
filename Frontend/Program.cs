using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using EmergencyPlatform.Frontend.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<EmergencyPlatform.Frontend.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5000/")
});

// EmergencyApiService is used by the report submission page (api/emergency/...)
// AuthService and ReportApiService are used by the dashboard (api/reports/...)
// All three must be registered.
builder.Services.AddScoped<EmergencyApiService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ReportApiService>();

await builder.Build().RunAsync();