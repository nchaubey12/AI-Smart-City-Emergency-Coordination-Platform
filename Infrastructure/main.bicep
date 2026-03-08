// ═══════════════════════════════════════════════════════════════════════════
// Emergency Platform – Azure Infrastructure (Bicep)
// Deploy: az deployment group create --resource-group <rg> --template-file main.bicep
// ═══════════════════════════════════════════════════════════════════════════

param location string = resourceGroup().location
param appName  string = 'emergency-ai'

// ── Azure OpenAI ──────────────────────────────────────────────────────────

resource openAI 'Microsoft.CognitiveServices/accounts@2023-05-01' = {
  name: '${appName}-openai'
  location: location
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: '${appName}-openai'
  }
}

resource gpt4oDeployment 'Microsoft.CognitiveServices/accounts/deployments@2023-05-01' = {
  parent: openAI
  name: 'gpt-4o'
  properties: {
    model: { format: 'OpenAI', name: 'gpt-4o', version: '2024-05-13' }
    raiPolicyName: 'Microsoft.Default'
  }
  sku: { name: 'Standard', capacity: 30 }
}

// ── Azure AI Speech ───────────────────────────────────────────────────────

resource speech 'Microsoft.CognitiveServices/accounts@2023-05-01' = {
  name: '${appName}-speech'
  location: location
  kind: 'SpeechServices'
  sku: { name: 'S0' }
  properties: {}
}

// ── Azure Maps ────────────────────────────────────────────────────────────

resource maps 'Microsoft.Maps/accounts@2023-06-01' = {
  name: '${appName}-maps'
  location: 'global'
  sku: { name: 'G2' }
  kind: 'Gen2'
  properties: {}
}

// ── Azure Service Bus ─────────────────────────────────────────────────────

resource serviceBusNs 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: '${appName}-sb'
  location: location
  sku: { name: 'Standard' }
}

resource incidentsQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNs
  name: 'incidents'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

// ── App Service Plan ──────────────────────────────────────────────────────

resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: '${appName}-plan'
  location: location
  sku: { name: 'B2', tier: 'Basic' }
  kind: 'linux'
  properties: { reserved: true }
}

// ── API App Service ───────────────────────────────────────────────────────

resource apiApp 'Microsoft.Web/sites@2023-01-01' = {
  name: '${appName}-api'
  location: location
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      appSettings: [
        { name: 'AzureOpenAI__Endpoint',       value: openAI.properties.endpoint }
        { name: 'AzureOpenAI__ApiKey',          value: openAI.listKeys().key1 }
        { name: 'AzureOpenAI__DeploymentName',  value: 'gpt-4o' }
        { name: 'AzureSpeech__ApiKey',          value: speech.listKeys().key1 }
        { name: 'AzureSpeech__Region',          value: location }
        { name: 'AzureMaps__SubscriptionKey',   value: maps.listKeys().primaryKey }
        { name: 'ServiceBus__ConnectionString', value: serviceBusNs.listKeys().primaryConnectionString }
        { name: 'ServiceBus__QueueName',        value: 'incidents' }
      ]
    }
  }
}

// ── Azure Functions (Dispatch Processor) ─────────────────────────────────

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: '${replace(appName, '-', '')}fn'
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
}

resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: '${appName}-functions'
  location: location
  kind: 'functionapp,linux'
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|8.0'
      appSettings: [
        { name: 'AzureWebJobsStorage',          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value}' }
        { name: 'FUNCTIONS_EXTENSION_VERSION',  value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME',     value: 'dotnet-isolated' }
        { name: 'ServiceBusConnection',         value: serviceBusNs.listKeys().primaryConnectionString }
      ]
    }
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────

output apiUrl       string = 'https://${apiApp.properties.defaultHostName}'
output openAIEndpoint string = openAI.properties.endpoint
