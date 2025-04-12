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
    using var reader = new StreamReader(context.Request.Body); 
    string body = await reader.ReadToEndAsync(); 
    var payload = JsonDocument.Parse(body).RootElement;
    if (!payload.TryGetProperty("action", out JsonElement actionElem))
    return Results.BadRequest(new { message = "Action not specified." });

    string action = actionElem.GetString() ?? string.Empty;

    // Action: getGrades – Return a list of grade records for the given student.
    if (action == "getGrades")
    {
        if (!payload.TryGetProperty("studentId", out JsonElement studentIdElem))
            return Results.BadRequest(new { message = "Student ID not specified." });
        
        int studentId = studentIdElem.GetInt32();
        var grades = new List<object>();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT course_id, grade FROM Grades WHERE student_id = @studentId;";
        cmd.Parameters.AddWithValue("@studentId", studentId);

        using var dbReader = await cmd.ExecuteReaderAsync();
        while (await dbReader.ReadAsync())
        {
            int courseId = dbReader.GetInt32(0);
            double gradeValue = dbReader.GetDouble(1);
            // CourseName defaults to "N/A"; it can be augmented later by the GradesController.
            grades.Add(new { CourseId = courseId, GradeValue = gradeValue, CourseName = "N/A" });
        }
        return Results.Json(grades);
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
                student_id INTEGER NOT NULL,
                course_id INTEGER NOT NULL,
                grade REAL NOT NULL
            );
        ";
    cmd.ExecuteNonQuery();
    }
}
