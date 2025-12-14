using CalculatorAgent.Memory;
using CalculatorAgent.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;

// Načtení konfigurace - nejprve user secrets, pak environment variables
var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables()
    .Build();

var apiKey = configuration["OPENAI_API_KEY"]
    ?? throw new InvalidOperationException("OPENAI_API_KEY není nastavena. Nastavte v user secrets nebo jako proměnnou prostředí.");

var model = configuration["OPENAI_MODEL"] ?? "gpt-4o-mini";

// Vytvoření pluginu pro výpočty
var calculator = new CalculatorTool();

// Vytvoření chat klienta
var chatClient = new OpenAIClient(apiKey)
    .GetChatClient(model)
    .AsIChatClient();

// Vytvoření agenta s logging middleware a AIContextProvider
AIAgent agent = chatClient
    .AsBuilder()
    .Use(getResponseFunc: LoggingChatClientMiddleware, getStreamingResponseFunc: null)  // Middleware pro logování
    .BuildAIAgent(new ChatClientAgentOptions
    {
        Name = "Kalkulačka",
        ChatOptions = new ChatOptions
        {
            Instructions = """
                Jsi kalkulačka s pamětí. Vždy odpovídej česky.

                Pro matematické výpočty vždy použij funkci Calculate.
                Když uživatel chce uložit hodnotu (např. "zapamatuj si to jako sleva"), potvrdí mu uložení.
                Když se uživatel ptá na uloženou hodnotu, použij informace z paměti poskytnuté v kontextu.
                """,
            Tools = [AIFunctionFactory.Create(calculator.Calculate)]
        },
        AIContextProviderFactory = ctx => new CalculatorMemory(
            chatClient,
            ctx.SerializedState,
            ctx.JsonSerializerOptions)
    });

// Chat smyčka
Console.WriteLine("Kalkulačka s pamětí (AIContextProvider + Logging) - 'exit' pro ukončení");
Console.WriteLine("======================================================================");

AgentThread thread = agent.GetNewThread();

while (true)
{
    Console.Write("\nYou: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;

    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("quit", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Nashledanou!");
        break;
    }

    try
    {
        var response = await agent.RunAsync(input, thread);
        Console.WriteLine($"Agent: {response}");

        // Zobrazit aktuální stav paměti
        var memory = thread.GetService<CalculatorMemory>();
        if (memory?.Facts.SavedValues.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[Paměť: {string.Join(", ", memory.Facts.SavedValues.Select(kv => $"{kv.Key}={kv.Value}"))}]");
            Console.ResetColor();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Chyba: {ex.Message}");
    }
}

// ==================== Logging Middleware ====================

/// <summary>
/// Middleware pro logování komunikace s LLM.
/// Zachytává zprávy před odesláním a odpovědi po přijetí.
/// </summary>
async Task<ChatResponse> LoggingChatClientMiddleware(
    IEnumerable<ChatMessage> messages,
    ChatOptions? options,
    IChatClient innerChatClient,
    CancellationToken cancellationToken)
{
    Console.ForegroundColor = ConsoleColor.DarkMagenta;
    Console.WriteLine("\n╔══════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║                    >>> ODESÍLÁM DO LLM <<<                       ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
    Console.ResetColor();

    // Logování všech zpráv posílaných do LLM
    foreach (var message in messages)
    {
        Console.ForegroundColor = message.Role.Value switch
        {
            "system" => ConsoleColor.Blue,
            "user" => ConsoleColor.Green,
            "assistant" => ConsoleColor.Yellow,
            "tool" => ConsoleColor.Magenta,
            _ => ConsoleColor.White
        };

        Console.WriteLine($"[{message.Role.Value.ToUpper()}]:");

        // Zobrazit textový obsah
        var text = message.Text;
        if (!string.IsNullOrEmpty(text))
        {
            // Zkrátit dlouhé texty
            var displayText = text.Length > 500 ? text[..500] + "... (zkráceno)" : text;
            Console.WriteLine($"  {displayText.Replace("\n", "\n  ")}");
        }

        // Zobrazit tool calls pokud existují
        foreach (var content in message.Contents)
        {
            if (content is FunctionCallContent functionCall)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"  [TOOL CALL] {functionCall.Name}({string.Join(", ", functionCall.Arguments?.Select(a => $"{a.Key}={a.Value}") ?? [])})");
            }
            else if (content is FunctionResultContent functionResult)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"  [TOOL RESULT] {functionResult.CallId}: {functionResult.Result}");
            }
        }
    }

    Console.ResetColor();
    Console.WriteLine("─────────────────────────────────────────────────────────────────────");

    // Volání LLM
    var response = await innerChatClient.GetResponseAsync(messages, options, cancellationToken);

    // Logování odpovědi
    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.WriteLine("\n╔══════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║                    <<< ODPOVĚĎ Z LLM <<<                         ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
    Console.ResetColor();

    foreach (var message in response.Messages)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[{message.Role.Value.ToUpper()}]:");

        var text = message.Text;
        if (!string.IsNullOrEmpty(text))
        {
            var displayText = text.Length > 500 ? text[..500] + "... (zkráceno)" : text;
            Console.WriteLine($"  {displayText.Replace("\n", "\n  ")}");
        }

        // Zobrazit tool calls v odpovědi
        foreach (var content in message.Contents)
        {
            if (content is FunctionCallContent functionCall)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"  [TOOL CALL] {functionCall.Name}({string.Join(", ", functionCall.Arguments?.Select(a => $"{a.Key}={a.Value}") ?? [])})");
            }
        }
    }

    Console.ResetColor();
    Console.WriteLine("─────────────────────────────────────────────────────────────────────\n");

    return response;
}
