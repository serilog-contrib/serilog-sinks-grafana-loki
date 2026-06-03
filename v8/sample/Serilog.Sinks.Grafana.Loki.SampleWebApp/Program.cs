using Serilog;
using Serilog.Debugging;

SelfLog.Enable(Console.Error);

var builder = WebApplication.CreateBuilder();

builder.Logging.ClearProviders();

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

// Add services to the container.
builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapControllers();

app.Run();