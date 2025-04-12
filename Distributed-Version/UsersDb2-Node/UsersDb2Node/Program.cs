using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text;
using System.IO;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

string dbPath = "users2.db";

// Ensure DB exists and create the table if missing
EnsureDatabase(dbPath);

app.MapGet("/status", () => Results.Ok(new { Name = "UsersDb2", Status = "Online" }));

app.MapPost("/config", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    string debugMessage = await reader.ReadToEndAsync();
    Console.WriteLine($"[UserDb2] Received config update: {debugMessage}");
    return Results.Ok(new { Message = "Config updated on UserDb2" });
});
// Main handler for queries
app.MapPost("/query", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    string body = await reader.ReadToEndAsync();
    var payload = JsonDocument.Parse(body).RootElement;

    if (!payload.TryGetProperty("action", out JsonElement actionElem))
        return Results.BadRequest(new { message = "Action not specified." });

    string action = actionElem.GetString() ?? "";

    if (action == "login")
    {
        if (!payload.TryGetProperty("username", out JsonElement userElem) ||
            !payload.TryGetProperty("password", out JsonElement passElem))
            return Results.BadRequest(new { message = "Missing credentials." });

        string username = userElem.GetString() ?? "";
        string password = passElem.GetString() ?? "";

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        string query = "SELECT UserId, Username, Role FROM Users WHERE Username = @username AND Password = @password LIMIT 1;";
        using var cmd = new SqliteCommand(query, conn);
        cmd.Parameters.AddWithValue("@username", username);
        cmd.Parameters.AddWithValue("@password", password);

        using var readerDb = await cmd.ExecuteReaderAsync();
        if (await readerDb.ReadAsync())
        {
            var user = new
            {
                UserId = readerDb.GetInt32(0),
                Username = readerDb.GetString(1),
                Role = readerDb.GetString(2)
            };
            return Results.Json(user);
        }
        else
        {
            return Results.Json(new { message = "Invalid credentials." }, statusCode: 401);
        }
    }

    return Results.BadRequest(new { message = $"Unsupported action: {action}" });
});

app.Run();

// Initialize DB with Users table
void EnsureDatabase(string path)
{
    if (!File.Exists(path))
    {
        Console.WriteLine("[UsersDB 2] Creating database...");
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS Users (
            UserId INTEGER PRIMARY KEY AUTOINCREMENT,
            Username TEXT NOT NULL,
            Password TEXT NOT NULL,
            Role TEXT NOT NULL
        );

        INSERT INTO Users (Username, Password, Role) VALUES 
        ('student1', 'pass123', 'student'),
        ('faculty1', 'teach456', 'faculty');
        ";
        cmd.ExecuteNonQuery();
    }
}
