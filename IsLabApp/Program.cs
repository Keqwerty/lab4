using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var appName = builder.Configuration["App:Name"] ?? "IsLabApp";
var appVersion = builder.Configuration["App:Version"] ?? "unknown";

var app = builder.Build();
var notes = new ConcurrentDictionary<int, Note>();
var nextNoteId = 0;

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "ok",
        time = DateTimeOffset.UtcNow
    });
})
.WithName("GetHealth")
.WithOpenApi();

app.MapGet("/version", () =>
{
    return Results.Ok(new
    {
        name = appName,
        version = appVersion,
        release = "Lab11"
    });
})
.WithName("GetVersion")
.WithOpenApi();

app.MapGet("/db/ping", async (IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var connectionString = configuration.GetConnectionString("Mssql");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return Results.Ok(new
        {
            status = "error",
            message = "Connection string 'Mssql' is not configured."
        });
    }

    try
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return Results.Ok(new
        {
            status = "ok"
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new
        {
            status = "error",
            message = ex.Message
        });
    }
})
.WithName("PingDatabase")
.WithOpenApi();

app.MapPost("/api/notes", (CreateNoteRequest request) =>
{
    var validationErrors = ValidateNoteRequest(request);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    var noteId = Interlocked.Increment(ref nextNoteId);
    var note = new Note(
        noteId,
        request.Title!.Trim(),
        request.Text!.Trim(),
        DateTimeOffset.UtcNow);

    notes[noteId] = note;

    return Results.Created($"/api/notes/{noteId}", note);
})
.WithName("CreateNote")
.WithOpenApi();

app.MapGet("/api/notes", () =>
{
    var allNotes = notes.Values
        .OrderByDescending(x => x.CreatedAt)
        .ToArray();

    return Results.Ok(allNotes);
})
.WithName("GetNotes")
.WithOpenApi();

app.MapGet("/api/notes/{id:int}", (int id) =>
{
    if (!notes.TryGetValue(id, out var note))
    {
        return Results.NotFound();
    }

    return Results.Ok(note);
})
.WithName("GetNoteById")
.WithOpenApi();

app.MapDelete("/api/notes/{id:int}", (int id) =>
{
    if (!notes.TryRemove(id, out _))
    {
        return Results.NotFound();
    }

    return Results.NoContent();
})
.WithName("DeleteNote")
.WithOpenApi();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.Run();

static Dictionary<string, string[]> ValidateNoteRequest(CreateNoteRequest request)
{
    var errors = new Dictionary<string, string[]>();

    if (string.IsNullOrWhiteSpace(request.Title))
    {
        errors["title"] = ["Title is required."];
    }
    else if (request.Title.Trim().Length > 200)
    {
        errors["title"] = ["Title must be 200 characters or fewer."];
    }

    if (string.IsNullOrWhiteSpace(request.Text))
    {
        errors["text"] = ["Text is required."];
    }
    else if (request.Text.Trim().Length > 4000)
    {
        errors["text"] = ["Text must be 4000 characters or fewer."];
    }

    return errors;
}

record CreateNoteRequest(string? Title, string? Text);
record Note(int Id, string Title, string Text, DateTimeOffset CreatedAt);

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public partial class Program
{
}
