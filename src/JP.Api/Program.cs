using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using JP.Api.Data;
using JP.Api.Entities;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// --- Services (the equivalent of Spring's @Bean / @Configuration) ---
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Identity: user management + password hashing. AddIdentityCore is the lightweight
// version for APIs (no cookies, no login pages). JWT comes in the next step.
builder.Services.AddIdentityCore<User>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
    })
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddControllers()
    // Enums as text in the JSON ("VeryGood" instead of 2)
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// Allow the future Next.js frontend locally
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:3000").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// --- HTTP pipeline ---
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                // OpenAPI JSON: /openapi/v1.json
    app.MapScalarApiReference();     // Test UI: /scalar

    // Apply migrations and insert demo data on startup
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.SeedAsync(db, scope.ServiceProvider.GetRequiredService<UserManager<User>>());
}

app.UseExceptionHandler();
app.UseCors();
app.MapControllers();

app.Run();
