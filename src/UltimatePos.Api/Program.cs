using Microsoft.AspNetCore.Authorization;
using Serilog;
using UltimatePos.Application;
using UltimatePos.Infrastructure;

var builder = WebApplication.CreateBuilder(args);


builder.Host.UseSerilog((context,config)=>config.ReadFrom.Configuration(context.Configuration));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


// JWT bearer + permission-policy wiring lands here once Auth is ported — build
// order step 1.

builder.Services.AddAuthorization(options =>
{
    // Default-deny: every endpoint requires authentication unless it opts out
    // with [AllowAnonymous]. This is the one line that would have prevented most
    // of the original repo's authorization gaps.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
