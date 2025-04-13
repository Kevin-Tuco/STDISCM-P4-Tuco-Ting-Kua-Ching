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

    Console.WriteLine("[CoursesDb] Received a POST to /query");
    using var reader = new StreamReader(context.Request.Body);
    string body = await reader.ReadToEndAsync();
    Console.WriteLine($"[CoursesDb] ✅ Raw JSON payload: {body}");
    var payload = JsonDocument.Parse(body).RootElement;

    if (!payload.TryGetProperty("action", out JsonElement actionElem))
        return Results.BadRequest(new { message = "Action not specified." });

    string action = actionElem.GetString()?.Trim() ?? "";
    Console.WriteLine($"[CoursesDb] ✅ action = '{action}' (length: {action.Length})");
    var conn = new SqliteConnection($"Data Source={dbPath}");
    await conn.OpenAsync();
    
    Console.WriteLine($"[CoursesDb] FINAL action string = '{action}' (length: {action.Length})");
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

    else if (action == "getCourse")
    {
        if (!payload.TryGetProperty("courseId", out JsonElement courseIdElem))
            return Results.BadRequest(new { message = "Missing courseId" });

        int courseId = courseIdElem.GetInt32();

        using var conn2 = new SqliteConnection($"Data Source={dbPath}");
        await conn2.OpenAsync();

        var cmd = conn2.CreateCommand();
        cmd.CommandText = "SELECT course_id, course_name FROM Courses WHERE course_id = $id;";
        cmd.Parameters.AddWithValue("$id", courseId);

        using var readerSingle = await cmd.ExecuteReaderAsync();
        if (await readerSingle.ReadAsync())
        {
            var course = new
            {
                courseId = readerSingle.GetInt32(0),
                courseName = readerSingle.GetString(1)
            };
            return Results.Json(course);
        }
        else
        {
            Console.WriteLine($"[CoursesDb] Course with ID {courseId} not found.");
            return Results.NotFound(new { message = "Course not found" });
        }
    }

    else if (action == "getEnrollments")
    {
        Console.WriteLine("[CoursesDb] ✅ Matched getEnrollments block!");

        if (!payload.TryGetProperty("courseId", out JsonElement courseIdElem))
            return Results.BadRequest(new { message = "Missing courseId" });

        int courseId = courseIdElem.GetInt32();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT e.student_id
            FROM Enrollment e
            WHERE e.course_id = $cid;";
        cmd.Parameters.AddWithValue("$cid", courseId);

        var students = new List<object>();
        using var enrollmentReader = await cmd.ExecuteReaderAsync();
        while (await enrollmentReader.ReadAsync())
        {
            int studentId = enrollmentReader.GetInt32(0);
            string username = $"Student #{studentId}";

            // 🔍 Attempt to load actual username from Users.db
            try
            {
                using var userConn = new SqliteConnection("Data Source=Users.db");
                userConn.Open();
                var userCmd = userConn.CreateCommand();
                userCmd.CommandText = "SELECT username FROM Users WHERE user_id = $id";
                userCmd.Parameters.AddWithValue("$id", studentId);
                using var userReader = await userCmd.ExecuteReaderAsync();
                if (await userReader.ReadAsync())
                    username = userReader.GetString(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CoursesDb] ⚠️ Could not retrieve username for ID {studentId}: {ex.Message}");
            }

            students.Add(new
            {
                userId = studentId,
                username = username
            });
        }

        Console.WriteLine($"[CoursesDb] Returning {students.Count} students for courseId: {courseId}");
        return Results.Json(students);
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
        if (!context.Request.Headers.TryGetValue("studentId", out var studentIdHeader))
            return Results.BadRequest(new { message = "Student ID missing" });

        int studentId = int.Parse(studentIdHeader!);
        int courseId = payload.GetProperty("courseId").GetInt32();

        // Check if already enrolled
        var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = @"
            SELECT COUNT(*) 
            FROM Enrollment 
            WHERE student_id = $sid AND course_id = $cid";
        checkCmd.Parameters.AddWithValue("$sid", studentId);
        checkCmd.Parameters.AddWithValue("$cid", courseId);

        if ((long)await checkCmd.ExecuteScalarAsync() > 0)
            return Results.BadRequest(new { message = "Already enrolled in this course." });

        // 🔥 Check if student already passed this course before (grade > 0.0)
        try
        {
            using var gradesConn = new SqliteConnection("Data Source=grades1.db");
            await gradesConn.OpenAsync();

            var checkGradeCmd = gradesConn.CreateCommand();
            checkGradeCmd.CommandText = @"
                SELECT grade FROM Grades 
                WHERE student_id = $sid AND course_id = $cid";
            checkGradeCmd.Parameters.AddWithValue("$sid", studentId);
            checkGradeCmd.Parameters.AddWithValue("$cid", courseId);

            using var gradeReader = await checkGradeCmd.ExecuteReaderAsync();
            if (await gradeReader.ReadAsync())
            {
                double existingGrade = gradeReader.GetDouble(0);
                if (existingGrade > 0.0)
                {
                    return Results.BadRequest(new { message = "Cannot re-enroll: student has already passed this course." });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CoursesDb] ⚠️ Grade check failed: {ex.Message}");
            // Optional: decide if you want to allow enrollment or block it
        }

        // Insert enrollment record
        var enrollCmd = conn.CreateCommand();
        enrollCmd.CommandText = @"
            INSERT INTO Enrollment (student_id, course_id, enrollment_date)
            VALUES ($sid, $cid, CURRENT_TIMESTAMP)";
        enrollCmd.Parameters.AddWithValue("$sid", studentId);
        enrollCmd.Parameters.AddWithValue("$cid", courseId);
        await enrollCmd.ExecuteNonQueryAsync();

        return Results.Ok(new { message = "Enrolled successfully!" });
    }
    else if (action == "removeEnrollment")
    {
        if (!payload.TryGetProperty("studentId", out JsonElement sidElem) ||
            !payload.TryGetProperty("courseId", out JsonElement cidElem))
        {
            return Results.BadRequest(new { message = "Missing studentId or courseId." });
        }

        int studentId = sidElem.ValueKind == JsonValueKind.Number
            ? sidElem.GetInt32()
            : int.Parse(sidElem.GetString() ?? "0");

        int courseId = cidElem.ValueKind == JsonValueKind.Number
            ? cidElem.GetInt32()
            : int.Parse(cidElem.GetString() ?? "0");

        var deleteCmd = conn.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM Enrollment WHERE student_id = $sid AND course_id = $cid;";
        deleteCmd.Parameters.AddWithValue("$sid", studentId);
        deleteCmd.Parameters.AddWithValue("$cid", courseId);

        int affected = await deleteCmd.ExecuteNonQueryAsync();

        return Results.Ok(new
        {
            message = affected > 0
                ? $"Student {studentId} removed from course {courseId}."
                : $"No enrollment found for student {studentId} in course {courseId}."
        });
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
        Console.WriteLine("[CoursesDb1] Creating database...");
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Courses (
                course_id INTEGER PRIMARY KEY AUTOINCREMENT,
                course_name VARCHAR(100) NOT NULL,
                description TEXT,
                max_slots INT NOT NULL,
                teacher_id INT,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );
            CREATE TABLE IF NOT EXISTS Enrollment (
                enrollment_id INTEGER PRIMARY KEY AUTOINCREMENT,
                student_id INT NOT NULL,
                course_id INT NOT NULL,
                enrollment_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                UNIQUE (student_id, course_id)
            );
        ";
        cmd.ExecuteNonQuery();
    }
}
