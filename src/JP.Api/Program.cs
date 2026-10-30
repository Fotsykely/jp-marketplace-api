using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using JP.Api.Data;
using JP.Api.Entities;
using JP.Api.Services;
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

// JWT: read the "Jwt" settings, then tell ASP.NET how to validate incoming tokens
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddScoped<TokenService>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // keep claim names as written ("sub", "unique_name")
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = jwt.Issuer,
            ValidateAudience = true, ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            NameClaimType = JwtRegisteredClaimNames.UniqueName
        };
    });
builder.Services.AddAuthorization();

// Photo storage: controllers depend on IPhotoStorage, so swapping to cloud storage later
// only means changing this registration.
builder.Services.Configure<PhotoStorageOptions>(builder.Configuration.GetSection(PhotoStorageOptions.SectionName));
builder.Services.AddSingleton<IPhotoStorage, LocalPhotoStorage>();

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
app.UseAuthentication(); // who are you? (reads the token)
app.UseAuthorization();  // are you allowed? ([Authorize])
app.MapControllers();

app.Run();
