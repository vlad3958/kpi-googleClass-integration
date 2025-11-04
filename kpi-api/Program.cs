using kpi.BLL.Service;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static kpi.BLL.Service.GoogleApiService;

var builder = WebApplication.CreateBuilder(args);

// Read config (ServiceAccountJsonPath, ImpersonatedAdmin) from appsettings.json or env
builder.Services.Configure<GoogleSettings>(builder.Configuration.GetSection("GoogleSettings"));

// Register Google service (wrapping Google Credential, ClassroomService, DirectoryService)
builder.Services.AddSingleton<IGoogleApiService, GoogleApiService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Optional: allow all for testing (adjust for production)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();
app.MapControllers();

app.Run();
