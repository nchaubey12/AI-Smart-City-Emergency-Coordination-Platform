# 🚨 Emergency AI Coordination Platform
### Microsoft AI Dev Days Hackathon — Multi-Agent Emergency Response System

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                     Blazor WebAssembly Frontend                  │
│  Dashboard │ Report (Text/Image/Audio) │ LocationPicker          │
└──────────────────────────┬──────────────────────────────────────┘
                           │ REST API
┌──────────────────────────▼──────────────────────────────────────┐
│                  ASP.NET Core Web API                            │
│               EmergencyController                                │
│         /api/emergency/{text|image|audio|image/upload}          │
└──────────────────────────┬──────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────┐
│                  Multi-Agent Pipeline                            │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │  InputNormalizerAgent  (orchestrator)                   │    │
│  └────┬──────────────┬───────────────┬──────────────┬──────┘    │
│       │              │               │              │            │
│  ┌────▼───┐   ┌──────▼──┐   ┌───────▼──┐   ┌──────▼───────┐   │
│  │ Speech │   │   NLP   │   │ Vision  │   │  Severity +  │   │
│  │ Agent  │   │  Agent  │   │  Agent  │   │  Dispatch    │   │
│  └────┬───┘   └──────┬──┘   └───────┬──┘   └──────┬───────┘   │
│       └──────────────┴───────────────┴──────────────┘           │
└──────────────────────────┬──────────────────────────────────────┘
                           │
           ┌───────────────┼───────────────┐
           │               │               │
    ┌──────▼─────┐  ┌──────▼─────┐  ┌─────▼──────┐
    │ Azure      │  │ Azure Maps │  │ Service    │
    │ OpenAI     │  │ (Geocode)  │  │ Bus Queue  │
    │ GPT-4o     │  └────────────┘  └─────┬──────┘
    └────────────┘                        │
                                   ┌──────▼──────┐
                                   │ Azure Func  │
                                   │ Dispatch    │
                                   │ Processor   │
                                   └─────────────┘
```

---

## Project Structure

```
EmergencyPlatform/
├── EmergencyPlatform.sln
├── Backend/
│   ├── EmergencyPlatform.Api.csproj
│   ├── Program.cs                      ← DI + middleware setup
│   ├── appsettings.json                ← Azure config keys
│   ├── Controllers/
│   │   └── EmergencyController.cs      ← REST endpoints
│   ├── Agents/
│   │   ├── InputNormalizerAgent.cs     ← Pipeline orchestrator
│   │   ├── NlpTextAgent.cs             ← Text classification
│   │   ├── VisionAgent.cs              ← GPT-4o vision analysis
│   │   ├── SeverityAgent.cs            ← Final severity rating
│   │   └── DispatchAgent.cs            ← Unit dispatch logic
│   ├── Services/
│   │   ├── AzureOpenAIService.cs       ← OpenAI REST wrapper
│   │   ├── AzureSpeechService.cs       ← Speech-to-text
│   │   ├── AzureMapsService.cs         ← Geocoding
│   │   └── ServiceBusPublisher.cs      ← Message publishing
│   ├── Models/
│   │   └── IncidentReport.cs           ← All domain models
│   └── Functions/
│       └── DispatchProcessorFunction.cs ← Azure Function triggers
├── Frontend/
│   ├── EmergencyPlatform.Frontend.csproj
│   ├── Program.cs
│   ├── Pages/
│   │   ├── Dashboard.razor             ← Live ops center
│   │   └── ReportPage.razor            ← Multi-modal input
│   ├── Components/
│   │   ├── IncidentResultCard.razor    ← Result display
│   │   └── LocationPicker.razor        ← GPS/manual location
│   ├── Services/
│   │   └── EmergencyApiService.cs      ← API client
│   ├── Models/
│   │   └── ViewModels.cs               ← Frontend models
│   └── wwwroot/
│       └── index.html                  ← JS interop (geolocation)
└── Infrastructure/
    └── main.bicep                      ← Azure IaC deployment
```

---

## Quick Start

### Prerequisites
- .NET 8 SDK
- Azure subscription with access to:
  - Azure OpenAI (GPT-4o deployment)
  - Azure AI Speech
  - Azure Maps
  - Azure Service Bus

### 1. Configure Azure Keys

Edit `Backend/appsettings.json`:

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://YOUR_RESOURCE.openai.azure.com",
    "ApiKey":   "YOUR_KEY",
    "DeploymentName": "gpt-4o"
  },
  "AzureSpeech": { "ApiKey": "YOUR_KEY", "Region": "eastus" },
  "AzureMaps":   { "SubscriptionKey": "YOUR_KEY" },
  "ServiceBus":  {
    "ConnectionString": "Endpoint=sb://...",
    "QueueName": "incidents"
  }
}
```

### 2. Run the Backend

```bash
cd Backend
dotnet run
# API available at: https://localhost:5001
# Swagger UI:       https://localhost:5001/swagger
```

### 3. Run the Frontend

```bash
cd Frontend
dotnet run
# Blazor app at: https://localhost:5002
```

### 4. Deploy to Azure

```bash
# 1. Create resource group
az group create --name emergency-ai-rg --location eastus

# 2. Deploy all Azure resources
az deployment group create \
  --resource-group emergency-ai-rg \
  --template-file Infrastructure/main.bicep

# 3. Publish API
cd Backend
dotnet publish -c Release
az webapp deploy --resource-group emergency-ai-rg \
  --name emergency-ai-api \
  --src-path bin/Release/net8.0/publish

# 4. Publish Frontend (static files to Azure Static Web Apps or App Service)
cd Frontend
dotnet publish -c Release
```

---

## API Reference

### POST /api/emergency/text
```json
{
  "text": "There is a large fire at 5th and Main. I can see flames coming from the 3rd floor window.",
  "lat": 40.7128,
  "lon": -74.0060
}
```

### POST /api/emergency/image
```json
{
  "base64Image": "<base64 encoded JPEG>",
  "mimeType": "image/jpeg",
  "lat": 40.7128,
  "lon": -74.0060
}
```

### POST /api/emergency/image/upload
Multipart form: `file` (image), `lat`, `lon`

### POST /api/emergency/audio
```json
{
  "base64Audio": "<base64 encoded WAV>",
  "mimeType": "audio/wav"
}
```

---

## Example Output

```json
{
  "incident_id": "A1B2C3D4",
  "input_type": "text",
  "incident_type": "fire",
  "severity_level": "critical",
  "location_data": {
    "lat": 40.7128,
    "lon": -74.006,
    "inferred_location_description": "5th and Main"
  },
  "entities": {
    "people_involved": 0,
    "vehicles_involved": 0,
    "hazards": "flames, smoke"
  },
  "reasoning_summary": "Active structure fire with visible flames on 3rd floor. Immediate life safety risk requires critical classification.",
  "dispatch_recommendation": {
    "units_required": ["fire", "ambulance"],
    "priority": 1
  },
  "timestamp": "2025-07-14T09:30:00Z",
  "agent_trace": [
    { "agent": "NlpTextAgent",  "output": "type=fire, severity=critical", "duration_ms": 812 },
    { "agent": "SeverityAgent", "output": "severity=critical",            "duration_ms": 340 },
    { "agent": "DispatchAgent", "output": "units=[fire,ambulance], priority=1", "duration_ms": 290 }
  ]
}
```

---

## Azure Services Used

| Service | Usage |
|---------|-------|
| Azure OpenAI GPT-4o | NLP classification, Vision analysis, Severity & Dispatch reasoning |
| Azure AI Speech | Audio → text transcription |
| Azure Maps | Geocoding location descriptions to lat/lon |
| Azure Service Bus | Async incident message queue for dispatch systems |
| Azure Functions | Service Bus triggered dispatch processor |
| Azure App Service | Host backend API + frontend |

---

## Hackathon Checklist

- [x] Multi-modal input: text, image, audio
- [x] Multi-agent pipeline with tracing
- [x] Structured JSON output matching spec
- [x] Azure OpenAI GPT-4o vision + NLP
- [x] Azure Speech transcription
- [x] Azure Maps geocoding
- [x] Azure Service Bus async dispatch
- [x] Azure Functions downstream processor
- [x] Blazor frontend dashboard
- [x] One-command Azure Bicep deployment
- [x] Swagger API documentation
