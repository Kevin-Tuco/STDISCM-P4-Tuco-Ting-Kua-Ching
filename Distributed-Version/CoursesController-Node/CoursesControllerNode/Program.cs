using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
var app = builder.Build();

string brokerUrl = "http://localhost:5000";

// GET /status: Reports that the CoursesController is online.
app.MapGet("/status", () =>
{
    return Results.Ok(new { Name = "CoursesController", Status = "Online" });
});

// POST /config: Receives configuration updates from the Broker.
app.MapPost("/config", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    string debugMessage = await reader.ReadToEndAsync();
    Console.WriteLine($"[CoursesController] Received config update: {debugMessage}");
    return Results.Ok(new { Message = "Config updated on CoursesController" });
});

// POST /process: Processes forwarded requests.
// Supports actions: "getCourses" and "getEnrolled".
app.MapPost("/process", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    string payloadRaw = await reader.ReadToEndAsync();
    Console.WriteLine($"[CoursesController] Processing forwarded payload: {payloadRaw}");

    using JsonDocument doc = JsonDocument.Parse(payloadRaw);
    JsonElement root = doc.RootElement;

    if (!root.TryGetProperty("action", out JsonElement actionElem))
        return Results.BadRequest(new { message = "Action not specified." });

    string action = actionElem.GetString() ?? string.Empty;

    int studentId = 0;
    if (action == "getEnrolled")
    {
        // For getEnrolled, try to get studentId from the payload,
        // or fall back to extracting it from user claims.
        if (root.TryGetProperty("studentId", out JsonElement studentIdElem))
        {
            studentId = studentIdElem.GetInt32();
        }
        else if (context.User.Identity is { IsAuthenticated: true })
        {
            int.TryParse(context.User.FindFirst("user_id")?.Value, out studentId);
        }
        if (studentId == 0)
        {
            return Results.BadRequest(new { message = "Student ID missing." });
        }
    }

    var httpClientFactory = context.RequestServices.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();

    HttpResponseMessage statusResponse = await httpClient.GetAsync($"{brokerUrl}/api/nodes");
    if (!statusResponse.IsSuccessStatusCode)
        return Results.Problem(detail: "Failed to retrieve node status from broker.", statusCode: 500);

    string statusContent = await statusResponse.Content.ReadAsStringAsync();
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    List<NodeStatus> allNodes = JsonSerializer.Deserialize<List<NodeStatus>>(statusContent, options);

    // Select available Courses DB nodes.
    var coursesNodes = allNodes.Where(n => n.Name.StartsWith("CoursesDb") && n.IsOnline).ToList();
    if (!coursesNodes.Any())
        return Results.BadRequest(new { message = "No Courses DB nodes are online." });

    NodeStatus chosenCoursesDb = coursesNodes.OrderBy(n => n.Latency).First();

    object payloadObj;
    if (action == "getCourses")
    {
        // For getCourses, forward only the action.
        payloadObj = new { action = "getCourses" };
    }
    else if (action == "getEnrolled")
    {
        payloadObj = new { action = "getEnrolled", studentId = studentId };
    }
    else
    {
        return Results.BadRequest(new { message = $"Unsupported action: {action}" });
    }

    string forwardPayload = JsonSerializer.Serialize(payloadObj);
    var contentPayload = new StringContent(forwardPayload, Encoding.UTF8, "application/json");

    await Task.Delay(chosenCoursesDb.Latency);
    HttpResponseMessage dbResponse = await httpClient.PostAsync($"{chosenCoursesDb.Url}/query", contentPayload);
    if (!dbResponse.IsSuccessStatusCode)
    {
        string errorMsg = await dbResponse.Content.ReadAsStringAsync();
        return Results.Problem(detail: errorMsg, statusCode: (int)dbResponse.StatusCode);
    }
    string dbResultRaw = await dbResponse.Content.ReadAsStringAsync();
    return Results.Content(dbResultRaw, "application/json");
});

app.Run();

record NodeStatus(string Name, string Url, bool IsOnline, bool IsActivated, int Latency);
