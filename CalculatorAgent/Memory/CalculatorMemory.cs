using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CalculatorAgent.Memory;

/// <summary>
/// AIContextProvider pro kalkulačku - automaticky extrahuje a ukládá pojmenované hodnoty z konverzace.
/// </summary>
public sealed class CalculatorMemory : AIContextProvider
{
    private readonly IChatClient _chatClient;

    public CalculatorFacts Facts { get; set; }

    /// <summary>Konstruktor pro novou instanci.</summary>
    public CalculatorMemory(IChatClient chatClient, CalculatorFacts? facts = null)
    {
        _chatClient = chatClient;
        Facts = facts ?? new CalculatorFacts();
    }

    /// <summary>Konstruktor pro deserializaci z uloženého stavu.</summary>
    public CalculatorMemory(IChatClient chatClient, JsonElement serializedState, JsonSerializerOptions? options = null)
    {
        _chatClient = chatClient;
        Facts = serializedState.ValueKind == JsonValueKind.Object
            ? serializedState.Deserialize<CalculatorFacts>(options)!
            : new CalculatorFacts();
    }

    /// <summary>
    /// Volá se PŘED inference - přidává kontext z paměti do promptu.
    /// </summary>
    public override ValueTask<AIContext> InvokingAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[Memory] InvokingAsync - uložených hodnot: {Facts.SavedValues.Count}");
        Console.ResetColor();

        var instructions = new StringBuilder();

        if (Facts.SavedValues.Count > 0)
        {
            instructions.AppendLine("Uložené hodnoty v paměti:");
            foreach (var kv in Facts.SavedValues)
            {
                instructions.AppendLine($"  {kv.Key} = {kv.Value}");
            }
        }

        if (Facts.LastResult != null)
        {
            instructions.AppendLine($"Poslední výsledek výpočtu: {Facts.LastResult}");
        }

        return new ValueTask<AIContext>(new AIContext
        {
            Instructions = instructions.ToString()
        });
    }

    /// <summary>
    /// Volá se PO inference - extrahuje pojmenované hodnoty z konverzace.
    /// </summary>
    public override async ValueTask InvokedAsync(InvokedContext context, CancellationToken cancellationToken = default)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("[Memory] InvokedAsync - extrahuji informace z konverzace...");
        Console.ResetColor();

        try
        {
            // Použít LLM k extrakci pojmenovaných hodnot
            var result = await _chatClient.GetResponseAsync<ExtractedData>(
                context.RequestMessages,
                new ChatOptions
                {
                    Instructions = """
                        Analyzuj konverzaci a extrahuj:
                        1. Pokud uživatel pojmenoval nějakou hodnotu (např. "to je sleva", "zapamatuj jako X", "ulož to jako Y"), vrať ji v poli named_values
                        2. Poslední vypočítaný číselný výsledek (pokud byl)

                        DŮLEŽITÉ: Vrať POUZE hodnoty, které uživatel explicitně pojmenoval nebo chtěl uložit.
                        Pokud uživatel nic nepojmenoval, vrať prázdné pole.
                        """
                },
                cancellationToken: cancellationToken);

            if (result.Result != null)
            {
                // Uložit pojmenované hodnoty
                if (result.Result.NamedValues != null)
                {
                    foreach (var nv in result.Result.NamedValues)
                    {
                        if (!string.IsNullOrWhiteSpace(nv.Name))
                        {
                            Facts.SavedValues[nv.Name] = nv.Value;
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"[Memory] Uloženo: {nv.Name} = {nv.Value}");
                            Console.ResetColor();
                        }
                    }
                }

                // Aktualizovat poslední výsledek
                if (result.Result.LastCalculatedResult.HasValue)
                {
                    Facts.LastResult = result.Result.LastCalculatedResult.Value.ToString();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[Memory] Poslední výsledek: {Facts.LastResult}");
                    Console.ResetColor();
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Memory] Chyba při extrakci: {ex.Message}");
            Console.ResetColor();
        }
    }

    /// <summary>Serializace stavu pro perzistenci.</summary>
    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        return JsonSerializer.SerializeToElement(Facts, jsonSerializerOptions);
    }
}

/// <summary>Model pro extrakci dat z konverzace pomocí LLM.</summary>
internal sealed class ExtractedData
{
    public List<NamedValue>? NamedValues { get; set; }
    public decimal? LastCalculatedResult { get; set; }
}

/// <summary>Pojmenovaná hodnota.</summary>
internal sealed class NamedValue
{
    public string Name { get; set; } = "";
    public decimal Value { get; set; }
}
