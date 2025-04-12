using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();

var allowedOrigins = "_myAllowSpecificOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: allowedOrigins,
                      policy =>
                      {
                          policy.WithOrigins("http://localhost:5001")
                                .AllowAnyHeader()
                                .AllowAnyMethod();
                      });
});

builder.Services.AddHttpClient();

var app = builder.Build();

// Apply CORS before the endpoints
app.UseCors(allowedOrigins);

string secretKey = "YourVeryVeryVerySecureSecretKey123!";
string brokerUrl = "http://localhost:5000";

app.MapGet("/status", () =>
{
    return Results.Ok(new { Name = "AuthController", Status = "Online" });
});

app.MapPost("/config", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    string debugMessage = await reader.ReadToEndAsync();
    Console.WriteLine($"[AuthController] Received config update: {debugMessage}");
    return Results.Ok(new { Message = "Config updated on AuthController" });
});

app.MapPost("/process", async (HttpContext context) =>
{
    // Read and parse the incoming JSON payload.
    using var reader = new StreamReader(context.Request.Body);
    string payloadRaw = await reader.ReadToEndAsync();
    Console.WriteLine($"[AuthController] Processing forwarded payload: {payloadRaw}");

    using JsonDocument doc = JsonDocument.Parse(payloadRaw);
    JsonElement root = doc.RootElement;
    if (!root.TryGetProperty("action", out JsonElement actionElem))
    {
        return Results.BadRequest(new { message = "Action not specified." });
    }
    string? action = actionElem.GetString();
    if (string.IsNullOrEmpty(action) || action != "login")
    {
        return Results.BadRequest(new { message = "Unsupported action." });
    }

    // Retrieve the username and password.
    if (!root.TryGetProperty("username", out JsonElement userElem) ||
        !root.TryGetProperty("password", out JsonElement passElem))
    {
        return Results.BadRequest(new { message = "Username or password missing." });
    }
    string? username = userElem.GetString();
    string? password = passElem.GetString();
    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
    {
        return Results.BadRequest(new { message = "Username or password missing." });
    }

    // Get node statuses from the Broker.
    var httpClient = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
    HttpResponseMessage statusResponse = await httpClient.GetAsync($"{brokerUrl}/api/nodes");
    if (!statusResponse.IsSuccessStatusCode)
    {
        return Results.Json(new { message = "Failed to retrieve node status from broker." }, statusCode: (int)statusResponse.StatusCode);
    }
    string statusContent = await statusResponse.Content.ReadAsStringAsync();
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    List<NodeStatus>? allNodes = JsonSerializer.Deserialize<List<NodeStatus>>(statusContent, options);
    if (allNodes == null)
    {
        return Results.Json(new { message = "Failed to parse node status from broker." }, statusCode: 500);
    }

    // Filter for Users DB nodes that are online.
    var usersDbNodes = allNodes.Where(n => n.Name.StartsWith("UsersDb") && n.IsOnline).ToList();
    if (usersDbNodes.Count == 0)
    {
        return Results.Json(new { message = "No Users DB nodes are currently online." }, statusCode: 400);
    }
    // Choose the online Users DB node with the lowest latency.
    NodeStatus chosenNode = usersDbNodes.OrderBy(n => n.Latency).First();

    // Construct payload for the DB node to process the login.
    var dbPayload = new
    {
        action = "login",
        username = username,
        password = password
    };
    var dbContent = new StringContent(JsonSerializer.Serialize(dbPayload), Encoding.UTF8, "application/json");

    // Forward the login request to the chosen DB node.
    HttpResponseMessage dbResponse = await httpClient.PostAsync($"{chosenNode.Url}/query", dbContent);
    if (!dbResponse.IsSuccessStatusCode)
    {
        string errorMsg = await dbResponse.Content.ReadAsStringAsync();
        return Results.Json(new { message = errorMsg }, statusCode: (int)dbResponse.StatusCode);
    }
    string dbResultRaw = await dbResponse.Content.ReadAsStringAsync();
    User? user = JsonSerializer.Deserialize<User>(dbResultRaw, options);
    if (user == null)
    {
        return Results.Json(new { message = "Invalid credentials." }, statusCode: 401);
    }

    // Generate JWT token for the authenticated user.
    string token = GenerateJwtToken(user, secretKey);
    return Results.Ok(new { token = token });
});

app.Run();

// Helper method to generate a JWT token.
string GenerateJwtToken(User user, string secretKey)
{
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var claims = new List<Claim>
    {
        new Claim("user_id", user.UserId.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role)
    };
    var token = new JwtSecurityToken(
        issuer: null,
        audience: null,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(1),
        signingCredentials: creds
    );
    return new JwtSecurityTokenHandler().WriteToken(token);
}

// Record representing a node status.
public record NodeStatus
{
    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public bool IsOnline { get; init; }
    public bool IsActivated { get; init; }
    public int Latency { get; init; }
}

// Record representing the User object.
public record User
{
    public int UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
}
