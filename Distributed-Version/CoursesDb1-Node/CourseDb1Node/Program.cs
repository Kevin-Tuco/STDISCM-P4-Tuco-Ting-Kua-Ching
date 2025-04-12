using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text;
using System.IO;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

string dbPath = "Courses1.db";

// Ensure DB exists and create the table if missing
EnsureDatabase(dbPath);

app.MapGet("/status", () => Results.Ok(new { Name = "CoursesDb1", Status = "Online" }));

app.MapPost("/config", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    string debugMessage = await reader.ReadToEndAsync();
    Console.WriteLine($"[CoursesDb1] Received config update: {debugMessage}");
    return Results.Ok(new { Message = "Config updated on CoursesDb1" });
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

    var conn = new SqliteConnection($"Data Source={dbPath}");
    await conn.OpenAsync();

    if (action == "getCourses")
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT course_id, course_name, description, max_slots, teacher_id FROM Courses;";
        using var readerDb = await cmd.ExecuteReaderAsync();

        var courses = new List<object>();
        while (await readerDb.ReadAsync())
        {
            int teacherId = readerDb.GetInt32(4);
            string teacherName = $"Teacher #{teacherId}";

            using var userConn = new SqliteConnection("Data Source=Users.db");
            userConn.Open();
            var userCmd = userConn.CreateCommand();
            userCmd.CommandText = "SELECT username FROM Users WHERE user_id = $id";
            userCmd.Parameters.AddWithValue("$id", teacherId);
            using var userReader = userCmd.ExecuteReader();
            if (userReader.Read())
                teacherName = userReader.GetString(0);

            courses.Add(new
            {
                courseId = readerDb.GetInt32(0),
                courseName = readerDb.GetString(1),
                description = readerDb.GetString(2),
                maxSlots = readerDb.GetInt32(3),
                teacherId,
                teacherName
            });
        }

        return Results.Json(courses);
    }

    else if (action == "getEnrolled")
    {
        if (!context.Request.Headers.TryGetValue("studentId", out var studentIdHeader))
            return Results.BadRequest(new { message = "Student ID missing" });
        int studentId = int.Parse(studentIdHeader!);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT c.course_id, c.course_name 
            FROM Courses c
            JOIN Enrollment e ON c.course_id = e.course_id
            WHERE e.student_id = $studentId;";
        cmd.Parameters.AddWithValue("$studentId", studentId);

        var enrolled = new List<object>();
        using var readerDb = await cmd.ExecuteReaderAsync();
        while (await readerDb.ReadAsync())
        {
            enrolled.Add(new
            {
                courseId = readerDb.GetInt32(0),
                courseName = readerDb.GetString(1)
            });
        }

        return Results.Json(enrolled);
    }

    else if (action == "enroll")
    {
        if (!context.Request.Headers.TryGetValue("user_id", out var studentIdHeader))
            return Results.BadRequest(new { message = "Student ID missing" });
        int studentId = int.Parse(studentIdHeader!);
        int courseId = payload.GetProperty("courseId").GetInt32();

        // Check already enrolled
        var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM Enrollment WHERE student_id = $sid AND course_id = $cid";
        checkCmd.Parameters.AddWithValue("$sid", studentId);
        checkCmd.Parameters.AddWithValue("$cid", courseId);
        if ((long)await checkCmd.ExecuteScalarAsync() > 0)
            return Results.BadRequest(new { message = "Already enrolled" });

        // Insert into Enrollment
        var enrollCmd = conn.CreateCommand();
        enrollCmd.CommandText = "INSERT INTO Enrollment (student_id, course_id, enrollment_date) VALUES ($sid, $cid, CURRENT_TIMESTAMP)";
        enrollCmd.Parameters.AddWithValue("$sid", studentId);
        enrollCmd.Parameters.AddWithValue("$cid", courseId);
        await enrollCmd.ExecuteNonQueryAsync();

        return Results.Ok(new { message = "Enrolled successfully!" });
    }

    else
    {
        return Results.BadRequest(new { message = $"Unsupported action: {action}" });
    }
});

app.Run();

// Initialize DB with Users table
void EnsureDatabase(string path)
{
    if (!File.Exists(path))
    {
        Console.WriteLine("[CoursesDB 1] Copying database from Courses.db...");
        File.Copy("Courses.db", path, overwrite: true);
        Console.WriteLine("[CoursesDB 1] Database copy complete.");
    }
}
