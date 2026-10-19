using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Serilog;
using UltimatePos.Api.Auth;
using UltimatePos.Api.Middleware;
using UltimatePos.Api.Services;
using UltimatePos.Application;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Infrastructure;
using UltimatePos.Infrastructure.Configuration;
using UltimatePos.Infrastructure.Payments;
using UltimatePos.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);


builder.Host.UseSerilog((context,config)=>config.ReadFrom.Configuration(context.Configuration));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);


builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUserService>();

// Background work: turn events into notifications and send email/SMS; sweep for stock, expiry, order and payment alerts.
if (builder.Configuration.GetValue("Notifications:WorkersEnabled", true))
{
    builder.Services.AddHostedService<UltimatePos.Api.Background.NotificationDispatchWorker>();
    builder.Services.AddHostedService<UltimatePos.Api.Background.AlertSweepWorker>();
}

var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtSettings.Key))
        };
    });

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
// Add services to the container.
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // Model-binding failures (missing body, malformed GUID, wrong enum) use the same envelope as every other error, with the
        // messages keyed by camelCase field name.
        options.InvalidModelStateResponseFactory = context =>
        {
            var entries = context.ModelState.Where(e => e.Value?.Errors.Count > 0).ToList();
            // A malformed field also fails the whole body parameter ("request is required"); report only the field when there is one.
            if (entries.Any(e => e.Key.StartsWith("$.")))
                entries = entries.Where(e => e.Key.StartsWith("$.")).ToList();
            var details = entries.ToDictionary(
                e => ToFieldName(e.Key),
                e => e.Value!.Errors.Select(x => FriendlyBindingError(x.ErrorMessage)).ToArray());
            var first = details.Values.SelectMany(v => v).FirstOrDefault() ?? "The request is not valid.";
            return new BadRequestObjectResult(ApiResponse<object>.Fail("VALIDATION_FAILED", first, details));
        };
    });

// Origins allowed to call the API from a browser (the separately hosted frontend). Empty = same-origin only.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0)
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("X-License-Warning");
}));
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => {
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token from /api/v1/auth/login — no 'Bearer ' prefix needed, Swagger adds it."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });

});


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseMiddleware<MpesaIpAllowlistMiddleware>();

app.UseHttpsRedirection();

app.UseCors();
app.UseAuthentication();
// A suspended, closed or unlicensed business is refused on every request, not only at sign-in.
app.UseMiddleware<BusinessAccessMiddleware>();
app.UseMiddleware<BranchScopeMiddleware>();

app.UseAuthorization();

app.MapControllers();

if (app.Environment.IsDevelopment()) { 
    using var scope =  app.Services.CreateScope();
    var services = scope.ServiceProvider;
    try
    {
        await DbSeeder.SeedAsync(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

// Every environment: make sure someone can run the platform (the first super user comes from Platform:BootstrapSuperUser).
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await PlatformBootstrapper.RunAsync(scope.ServiceProvider, app.Configuration, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Could not check for a super user (is the database migrated?).");
    }
}

app.Run();

// "request.Lines[0].Quantity" / "$.lines[0].quantity" -> "lines[0].quantity"
static string ToFieldName(string key)
{
    var trimmed = key.StartsWith("$.") ? key[2..] : key;
    if (trimmed.StartsWith("request.", StringComparison.OrdinalIgnoreCase))
        trimmed = trimmed["request.".Length..];
    return string.Join('.', trimmed.Split('.').Select(part => part.Length == 0 ? part : char.ToLowerInvariant(part[0]) + part[1..]));
}

// System.Text.Json's conversion messages name .NET types; clients get a plain sentence instead.
static string FriendlyBindingError(string message) =>
    string.IsNullOrWhiteSpace(message) ? "The value is not valid."
    : message.StartsWith("The JSON value could not be converted", StringComparison.Ordinal) ? "The value is not in the expected format."
    : message;
