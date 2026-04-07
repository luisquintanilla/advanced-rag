var builder = DistributedApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Azure OpenAI — reference an existing resource via user secrets
// Required user secrets (set in AppHost project):
//   dotnet user-secrets set "Azure:SubscriptionId" "<subscription-id>"
//   dotnet user-secrets set "Azure:Location" "eastus"
//   dotnet user-secrets set "AzureOpenAI:Name" "<resource-name>"
//   dotnet user-secrets set "AzureOpenAI:ResourceGroup" "<resource-group>"
// ---------------------------------------------------------------------------
var azOpenAiName = builder.AddParameterFromConfiguration("AzureOpenAIName", "AzureOpenAI:Name");
var azOpenAiRg = builder.AddParameterFromConfiguration("AzureOpenAIResourceGroup", "AzureOpenAI:ResourceGroup");

var openai = builder.AddAzureOpenAI("openai")
    .RunAsExisting(azOpenAiName, azOpenAiRg);

// Deployment names must match what exists in your Azure OpenAI resource
openai.AddDeployment("chat", "gpt-5.1", "2025-11-13");
openai.AddDeployment("embedding", "text-embedding-3-small", "1");

var vectorDB = builder.AddQdrant("vectordb")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var webApp = builder.AddProject<Projects.PdfAIngest_Web>("aichatweb-app");
webApp
    .WithReference(openai)
    .WaitFor(openai);
webApp
    .WithReference(vectorDB)
    .WaitFor(vectorDB);

builder.Build().Run();
