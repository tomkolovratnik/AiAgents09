# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Vývojové prostředí

- **OS:** Windows (Git Bash)
- **Shell:** Git Bash (MINGW64)

## Přehled projektu

Konzolová aplikace "Kalkulačka s pamětí" - AI agent implementovaný pomocí Microsoft Agent Framework s automatickou pamětí přes `AIContextProvider`.

## Příkazy

```bash
# Build
dotnet build CalculatorAgent/CalculatorAgent.csproj

# Spuštění (vyžaduje OPENAI_API_KEY)
dotnet run --project CalculatorAgent/CalculatorAgent.csproj

# Volitelně nastavit model
set OPENAI_MODEL=gpt-4o-mini
```

## Architektura

```
CalculatorAgent/
├── Program.cs                    # Chat smyčka, konfigurace agenta s AIContextProvider
├── Plugins/
│   └── CalculatorPlugin.cs       # Nástroj Calculate - vyhodnocení matematických výrazů
├── Memory/
│   ├── CalculatorFacts.cs        # Model pro uložená fakta (Dictionary)
│   └── CalculatorMemory.cs       # AIContextProvider - automatická paměť
└── CalculatorAgent.csproj
```

### Klíčové vzory z Microsoft Agent Framework

1. **Vytvoření agenta s AIContextProvider:**
```csharp
AIAgent agent = chatClient.CreateAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new ChatOptions
    {
        Instructions = "...",
        Tools = [AIFunctionFactory.Create(calculator.Calculate)]
    },
    AIContextProviderFactory = ctx => new CalculatorMemory(
        chatClient,
        ctx.SerializedState,
        ctx.JsonSerializerOptions)
});
```

2. **AIContextProvider metody:**
```csharp
// PŘED inference - přidá kontext z paměti
public override ValueTask<AIContext> InvokingAsync(InvokingContext context, CancellationToken ct)

// PO inference - extrahuje informace z konverzace
public override ValueTask InvokedAsync(InvokedContext context, CancellationToken ct)

// Serializace pro perzistenci
public override JsonElement Serialize(JsonSerializerOptions? options)
```

3. **Přístup k paměti z threadu:**
```csharp
var memory = thread.GetService<CalculatorMemory>();
```

4. **Chat Client Middleware pro logování:**
```csharp
AIAgent agent = chatClient
    .AsBuilder()
    .Use(getResponseFunc: LoggingMiddleware, getStreamingResponseFunc: null)
    .BuildAIAgent(options);

async Task<ChatResponse> LoggingMiddleware(
    IEnumerable<ChatMessage> messages,
    ChatOptions? options,
    IChatClient innerChatClient,
    CancellationToken ct)
{
    // Log před voláním LLM
    foreach (var msg in messages) Console.WriteLine($"[{msg.Role}]: {msg.Text}");

    var response = await innerChatClient.GetResponseAsync(messages, options, ct);

    // Log odpovědi
    foreach (var msg in response.Messages) Console.WriteLine($"[{msg.Role}]: {msg.Text}");

    return response;
}
```

## Závislosti

- `Microsoft.Agents.AI` - Agent Framework
- `Microsoft.Extensions.AI.OpenAI` - Extension pro OpenAI (`AsIChatClient()`)
- `OpenAI` - OpenAI klient

## Reference

Zdrojové kódy Microsoft Agent Framework: `D:\DEV\External\agent-framework`
- Memory dokumentace: https://learn.microsoft.com/en-us/agent-framework/tutorials/agents/memory
- Custom memory ukázka: `dotnet/samples/GettingStarted/AgentWithMemory/AgentWithMemory_Step03_CustomMemory/`
- Middleware ukázka: `dotnet/samples/GettingStarted/Agents/Agent_Step14_Middleware/`
