using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
var app = builder.Build();

string brokerUrl = "http://localhost:5000";

// GET /status endpoint remains the same
app.MapGet("/status", () =>
{
    return Results.Ok(new { Name = "CoursesController", Status = "Online" });
});

// POST /config remains the same
app.MapPost("/config", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    string debugMessage = await reader.ReadToEndAsync();
    Console.WriteLine($"[CoursesController] Received config update: {debugMessage}");
    return Results.Ok(new { Message = "Config updated on CoursesController" });
});

// POST /process: Forwards requests to the appropriate Courses DB node.
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

    // Determine studentId if needed (for enroll and getEnrolled)
    int studentId = 0;
    if (action == "getEnrolled" || action == "enroll")
    {
        //Try to get studentId from the JSON payload first…
        if (root.TryGetProperty("studentId", out JsonElement studentIdElem))
        {   
            studentId = studentIdElem.GetInt32();
        }

        // Otherwise, try to extract it from authenticated user claims.
        else if (context.User.Identity is { IsAuthenticated: true })
        {
            int.TryParse(context.User.FindFirst("studentId")?.Value, out studentId);
        }
        if (studentId == 0)
        {
            return Results.BadRequest(new { message = "Student ID missing." });
        }
    }

    var httpClientFactory = context.RequestServices.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();

    // Get the broker nodes status
    HttpResponseMessage statusResponse = await httpClient.GetAsync($"{brokerUrl}/api/nodes");
    if (!statusResponse.IsSuccessStatusCode)
        return Results.Problem(detail: "Failed to retrieve node status from broker.", statusCode: 500);

    string statusContent = await statusResponse.Content.ReadAsStringAsync();
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    List<NodeStatus> allNodes = JsonSerializer.Deserialize<List<NodeStatus>>(statusContent, options);

    // Select available Courses DB nodes.
    var coursesNodes = allNodes.Where(n => n.Name.StartsWith("CoursesDb") && n.IsOnline && n.IsActivated).ToList();
    if (!coursesNodes.Any())
        return Results.BadRequest(new { message = "No Courses DB nodes are online." });

    NodeStatus chosenCoursesDb = coursesNodes.OrderBy(n => n.Latency).First();

    // Build the payload object for forwarding.
    object payloadObj;
    if (action == "getCourses")
    {
        payloadObj = new { action = "getCourses" };
    }
    else if (action == "getEnrolled")
    {
        payloadObj = new { action = "getEnrolled", studentId = studentId };
    }
    else if (action == "enroll")
    {
        // Extract courseId from payload
        int courseId = root.GetProperty("courseId").GetInt32();
        // Extract studentId from payload (which you are now sending)
        studentId = root.TryGetProperty("studentId", out JsonElement sElem) ? sElem.GetInt32() : 0;
        if (studentId == 0)
        {
            return Results.BadRequest(new { message = "Student ID missing." });
        }
        
        // Call GradesController to check if student passed
        var gradesNodes = allNodes.Where(n => n.Name.StartsWith("GradesDb") && n.IsOnline && n.IsActivated).ToList();
        if (!gradesNodes.Any())
            return Results.Problem("No Grades DB nodes online to verify past grades.");

        NodeStatus chosenGradesDb = gradesNodes.OrderBy(n => n.Latency).First();

        var checkPassPayload = new
        {
            action = "hasPassed",
            studentId,
            courseId
        };

        var checkPassRequest = new HttpRequestMessage(HttpMethod.Post, $"{chosenGradesDb.Url}/query")
        {
            Content = new StringContent(JsonSerializer.Serialize(checkPassPayload), Encoding.UTF8, "application/json")
        };
        await Task.Delay(chosenGradesDb.Latency);

        var passResponse = await httpClient.SendAsync(checkPassRequest);
        var passBody = await passResponse.Content.ReadAsStringAsync();
        if (passResponse.IsSuccessStatusCode)
        {
            var passJson = JsonDocument.Parse(passBody).RootElement;
            if (passJson.TryGetProperty("hasPassed", out var hasPassedElem) && hasPassedElem.GetBoolean())
            {
                return Results.BadRequest(new { message = "Cannot re-enroll: student has already passed this course." });
            }
        }
        else
        {
            return Results.Problem($"Error checking past grade: {passBody}");
        }

        payloadObj = new { action = "enroll", courseId = courseId };
    }
    else if (action == "getEnrollments")
    {
        // Extract courseId from payload
        if (!root.TryGetProperty("courseId", out JsonElement courseIdElem))
            return Results.BadRequest(new { message = "Missing courseId" });

        int courseId = courseIdElem.GetInt32();
        payloadObj = new { action = "getEnrollments", courseId };
    }
    else
    {
        return Results.BadRequest(new { message = $"Unsupported action: {action}" });
    }

    string forwardPayload = JsonSerializer.Serialize(payloadObj);

    // Create an HttpRequestMessage so that we can add a header if needed.
    var requestMessage = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{chosenCoursesDb.Url}/query")
    {
        Content = new StringContent(forwardPayload, Encoding.UTF8, "application/json")
    };

    // For actions that require a studentId (enroll and getEnrolled), add it as a header.
    if (action == "getEnrolled" || action == "enroll")
    {
        requestMessage.Headers.Add("studentId", studentId.ToString());
    }

    // Apply the simulated latency.
    await Task.Delay(chosenCoursesDb.Latency);

    HttpResponseMessage dbResponse = await httpClient.SendAsync(requestMessage);
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
