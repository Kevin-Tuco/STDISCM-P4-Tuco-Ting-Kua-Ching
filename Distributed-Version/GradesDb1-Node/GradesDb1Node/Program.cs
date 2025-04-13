using Microsoft.AspNetCore.Builder; 
using Microsoft.AspNetCore.Http; 
using Microsoft.Data.Sqlite; 
using System.Text.Json; 
using System.Text; 
using System.IO; 
using System.Collections.Generic; 
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args); var app = builder.Build();

// Define the path for the Grades database. 
string dbPath = "grades1.db";

// Ensure the database exists and the Grades table is created. 
EnsureDatabase(dbPath);

// GET /status endpoint to report the node's status. 
app.MapGet("/status", () => Results.Ok(new { Name = "GradesDb1", Status = "Online" }));

// POST /config endpoint to accept configuration updates. 
app.MapPost("/config", async (HttpContext context) => 
{ 
    using var reader = new StreamReader(context.Request.Body); 
    string debugMessage = await reader.ReadToEndAsync(); 
    Console.WriteLine($"[GradesDb1] Received config update: {debugMessage}"); 
    return Results.Ok(new { Message = "Config updated on GradesDb1" }); });

// POST /query endpoint to handle grade-related actions. 
app.MapPost("/query", async (HttpContext context) => 
{ 
    Console.WriteLine("[GradesDb] 🔁 /process handler hit (same logic as /query)");
    using var reader = new StreamReader(context.Request.Body); 
    string body = await reader.ReadToEndAsync(); 
    var payload = JsonDocument.Parse(body).RootElement;
    if (!payload.TryGetProperty("action", out JsonElement actionElem))
    return Results.BadRequest(new { message = "Action not specified." });

    string action = actionElem.GetString() ?? string.Empty;

    // Action: getGrades – Return a list of grade records for the given student.
    if (action == "getGrades")
    {
        if (!context.Request.Headers.TryGetValue("studentId", out var studentIdHeader))
            return Results.BadRequest(new { message = "Student ID missing" });

        Console.WriteLine($"[GradesDb1] studentIdHeader received: {studentIdHeader}");

        try
        {
            int studentId = int.Parse(studentIdHeader!);
            var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT g.course_id, g.grade
                FROM Grades g
                JOIN (
                    SELECT course_id, MAX(grade_id) AS latest
                    FROM Grades
                    WHERE student_id = $sid
                    GROUP BY course_id
                ) latestGrades ON g.course_id = latestGrades.course_id AND g.grade_id = latestGrades.latest
                WHERE g.student_id = $sid;
                ";
            cmd.Parameters.AddWithValue("$sid", studentId);

            var grades = new List<object>();
            using var readerDb = await cmd.ExecuteReaderAsync();
            while (await readerDb.ReadAsync())
            {
                grades.Add(new
                {
                    courseId = readerDb.GetInt32(0),
                    gradeValue = readerDb.GetDouble(1)
                });
            }

            return Results.Json(grades);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GradesDb1] ERROR: {ex.Message}");
            return Results.Problem($"Grades DB error: {ex.Message}");
        }
    }
    // Action: uploadGrade – Insert a grade record into the Grades table.
    else if (action == "uploadGrade")
    {
        if (!payload.TryGetProperty("studentId", out JsonElement studentIdElem) ||
            !payload.TryGetProperty("courseId", out JsonElement courseIdElem) ||
            !payload.TryGetProperty("gradeValue", out JsonElement gradeValueElem))
        {
            return Results.BadRequest(new { message = "Missing parameters for uploading grade." });
        }
        
        int studentId = studentIdElem.GetInt32();
        int courseId = courseIdElem.GetInt32();
        double gradeValue = gradeValueElem.GetDouble();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = "INSERT INTO Grades (student_id, course_id, grade) VALUES (@studentId, @courseId, @gradeValue);";
        insertCmd.Parameters.AddWithValue("@studentId", studentId);
        insertCmd.Parameters.AddWithValue("@courseId", courseId);
        insertCmd.Parameters.AddWithValue("@gradeValue", gradeValue);

        int rowsAffected = await insertCmd.ExecuteNonQueryAsync();
        if (rowsAffected > 0)
        {
            return Results.Ok(new { message = "Grade uploaded successfully!" });
        }
        else
        {
            return Results.BadRequest(new { message = "Failed to upload grade." });
        }
    }
    else if (action == "hasPassed")
    {
        if (!payload.TryGetProperty("studentId", out JsonElement sidElem) ||
            !payload.TryGetProperty("courseId", out JsonElement cidElem))
        {
            return Results.BadRequest(new { message = "Missing studentId or courseId." });
        }

        int studentId = sidElem.GetInt32();
        int courseId = cidElem.GetInt32();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = @"
            SELECT grade FROM Grades 
            WHERE student_id = $sid AND course_id = $cid";
        checkCmd.Parameters.AddWithValue("$sid", studentId);
        checkCmd.Parameters.AddWithValue("$cid", courseId);

        using var gradeReader = await checkCmd.ExecuteReaderAsync();
        while (await gradeReader.ReadAsync())
        {
            double grade = gradeReader.GetDouble(0);
            if (grade > 0.0)
                return Results.Json(new { hasPassed = true });
        }

        return Results.Json(new { hasPassed = false });
    }


    else
    {
        return Results.BadRequest(new { message = $"Unsupported action: {action}" });
    }
});

app.Run();

// Local helper method to ensure the Grades database exists and has the required table. 
void EnsureDatabase(string path) { 
    if (!File.Exists(path)) { 
        Console.WriteLine("[GradesDb1] Creating database..."); 
        using var conn = new SqliteConnection($"Data Source={path}"); conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS Grades (
            grade_id INTEGER PRIMARY KEY AUTOINCREMENT,
            student_id INT NOT NULL,
            course_id INT NOT NULL,
            grade REAL NOT NULL,
            graded_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        );
        ";
    cmd.ExecuteNonQuery();
    }
}
