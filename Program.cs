using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowPurdueFrontend", policy =>
    {
        policy
            .WithOrigins("https://cgtweb2.tech.purdue.edu")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient("openai", client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/");
    var apiKey = builder.Configuration["OpenAI:ApiKey"];

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        throw new InvalidOperationException("OpenAI API key is missing.");
    }

    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", apiKey);
});

var app = builder.Build();

app.UseCors("AllowPurdueFrontend");
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/", () => Results.Text("Azure app is running."));

app.MapPost("/api/chat", async (
    ChatRequest request,
    IHttpClientFactory httpClientFactory,
    IConfiguration config) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "Message cannot be empty." });
    }

    var model = config["OpenAI:Model"] ?? "gpt-4o-mini";
    var client = httpClientFactory.CreateClient("openai");

    var payload = new OpenAIChatRequest
    {
        Model = model,
        Messages = new List<OpenAIMessage>
        {
            new()
            {
                Role = "system",
                Content = "You are a helpful AI assistant in an ASP.NET chatbot app. Keep answers clear and useful."
            },
            new()
            {
                Role = "user",
                Content = request.Message
            }
        },
        Temperature = 0.7
    };

    var json = JsonSerializer.Serialize(payload);
    using var content = new StringContent(json, Encoding.UTF8, "application/json");

    using var response = await client.PostAsync("v1/chat/completions", content);
    var responseText = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        return Results.Problem(
            title: "OpenAI API error",
            detail: responseText,
            statusCode: (int)response.StatusCode
        );
    }

    var openAiResponse = JsonSerializer.Deserialize<OpenAIChatResponse>(responseText);

    var reply = openAiResponse?.Choices?.FirstOrDefault()?.Message?.Content
                ?? "No response returned.";

    return Results.Ok(new ChatResponse(reply));
});

app.Run();

public record ChatRequest(string Message);
public record ChatResponse(string Reply);

public class OpenAIChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = default!;

    [JsonPropertyName("messages")]
    public List<OpenAIMessage> Messages { get; set; } = new();

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.7;
}

public class OpenAIMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = default!;

    [JsonPropertyName("content")]
    public string Content { get; set; } = default!;
}

public class OpenAIChatResponse
{
    [JsonPropertyName("choices")]
    public List<OpenAIChoice>? Choices { get; set; }
}

public class OpenAIChoice
{
    [JsonPropertyName("message")]
    public OpenAIMessage? Message { get; set; }
}