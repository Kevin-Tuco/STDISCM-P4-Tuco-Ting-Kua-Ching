using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text;
using System.IO;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

string dbPath = "users1.db";

// Ensure DB exists and create the table if missing
EnsureDatabase(dbPath);

app.MapGet("/status", () => Results.Ok(new { Name = "UsersDb1", Status = "Online" }));

app.MapPost("/config", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    string debugMessage = await reader.ReadToEndAsync();
    Console.WriteLine($"[UsersDb1] Received config update: {debugMessage}");
    return Results.Ok(new { Message = "Config updated on UsersDb1" });
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
        //string passwordHash = HashPassword(password);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        string query = "SELECT user_id, username, role FROM Users WHERE username = @username AND password_hash = @passwordHash LIMIT 1;";
        using var cmd = new SqliteCommand(query, conn);
        cmd.Parameters.AddWithValue("@username", username);
        cmd.Parameters.AddWithValue("@passwordHash", password);

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
        Console.WriteLine("[UsersDb1] Creating database...");
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS Users (
            user_id INTEGER PRIMARY KEY AUTOINCREMENT,
            username VARCHAR(50) UNIQUE NOT NULL,
            password_hash VARCHAR(255) NOT NULL,
            email VARCHAR(100) UNIQUE NOT NULL,
            role VARCHAR(20) CHECK (role IN ('student', 'teacher')) NOT NULL,
            created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        );
        CREATE TABLE IF NOT EXISTS Sessions (
            session_id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INT NOT NULL,
            jwt_token VARCHAR(255),  -- store JWTs or token identifiers if needed
            issued_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
            expires_at TIMESTAMP,
            active BOOLEAN DEFAULT TRUE,
            FOREIGN KEY (user_id) REFERENCES Users(user_id)
        );

       INSERT INTO Users (user_id, username, password_hash, email, role, created_at) VALUES
        (1, 'teacher101', 'pass1', 't101@example.com', 'teacher', '2025-04-07 00:39:45'),
        (2, 'teacher102', 'pass2', 't102@example.com', 'teacher', '2025-04-07 00:39:45'),
        (3, 'teacher103', 'pass3', 't103@example.com', 'teacher', '2025-04-07 00:39:45'),
        (4, 'teacher104', 'pass4', 't104@example.com', 'teacher', '2025-04-07 00:39:45'),
        (5, 'teacher105', 'pass5', 't105@example.com', 'teacher', '2025-04-07 00:39:45'),
        (6, 'student1', 'pass1', 's1@example.com', 'student', '2025-04-07 00:39:45'),
        (7, 'student2', 'pass2', 's2@example.com', 'student', '2025-04-07 00:39:45'),
        (8, 'student3', 'pass3', 's3@example.com', 'student', '2025-04-07 00:39:45'),
        (9, 'student4', 'pass4', 's4@example.com', 'student', '2025-04-07 00:39:45'),
        (10, 'student5', 'pass5', 's5@example.com', 'student', '2025-04-07 00:39:45'),
        (11, 'student6', 'pass6', 's6@example.com', 'student', '2025-04-07 00:39:45'),
        (12, 'student7', 'pass7', 's7@example.com', 'student', '2025-04-07 00:39:45'),
        (13, 'student8', 'pass8', 's8@example.com', 'student', '2025-04-07 00:39:45'),
        (14, 'student9', 'pass9', 's9@example.com', 'student', '2025-04-07 00:39:45'),
        (15, 'student10', 'pass10', 's10@example.com', 'student', '2025-04-07 00:39:45'),
        (16, 'student11', 'pass11', 's11@example.com', 'student', '2025-04-07 00:39:45'),
        (17, 'student12', 'pass12', 's12@example.com', 'student', '2025-04-07 00:39:45'),
        (18, 'student13', 'pass13', 's13@example.com', 'student', '2025-04-07 00:39:45'),
        (19, 'student14', 'pass14', 's14@example.com', 'student', '2025-04-07 00:39:45'),
        (20, 'student15', 'pass15', 's15@example.com', 'student', '2025-04-07 00:39:45'),
        (21, 'student16', 'pass16', 's16@example.com', 'student', '2025-04-07 00:39:45'),
        (22, 'student17', 'pass17', 's17@example.com', 'student', '2025-04-07 00:39:45'),
        (23, 'student18', 'pass18', 's18@example.com', 'student', '2025-04-07 00:39:45'),
        (24, 'student19', 'pass19', 's19@example.com', 'student', '2025-04-07 00:39:45'),
        (25, 'student20', 'pass20', 's20@example.com', 'student', '2025-04-07 00:39:45'),
        (26, 'student21', 'pass21', 's21@example.com', 'student', '2025-04-07 00:39:45'),
        (27, 'student22', 'pass22', 's22@example.com', 'student', '2025-04-07 00:39:45'),
        (28, 'student23', 'pass23', 's23@example.com', 'student', '2025-04-07 00:39:45'),
        (29, 'student24', 'pass24', 's24@example.com', 'student', '2025-04-07 00:39:45'),
        (30, 'student25', 'pass25', 's25@example.com', 'student', '2025-04-07 00:39:45'),
        (31, 'student26', 'pass26', 's26@example.com', 'student', '2025-04-07 00:39:45'),
        (32, 'student27', 'pass27', 's27@example.com', 'student', '2025-04-07 00:39:45'),
        (33, 'student28', 'pass28', 's28@example.com', 'student', '2025-04-07 00:39:45'),
        (34, 'student29', 'pass29', 's29@example.com', 'student', '2025-04-07 00:39:45'),
        (35, 'student30', 'pass30', 's30@example.com', 'student', '2025-04-07 00:39:45');
        ";
        cmd.ExecuteNonQuery();
    }
}

// Simple SHA-256 password hashing
string HashPassword(string password)
{
    using var sha = SHA256.Create();
    var bytes = Encoding.UTF8.GetBytes(password);
    var hash = sha.ComputeHash(bytes);
    return Convert.ToHexString(hash);
}
