using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Classroom.v1;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading.Tasks;
namespace kpi.BLL.Service;
public class GoogleApiService : IGoogleApiService
{
    public ClassroomService ClassroomService { get; }
    public string AdminEmail { get; }

    public record GoogleSettings
    {
        public string ServiceAccountJsonPath { get; init; } = "kpi-proj-476910-4945d2725830.json";
        public string ImpersonatedAdmin { get; init; } = "ber@vlad7930.work.gd";
    }
    public GoogleApiService(IOptions<GoogleSettings> options)
    {
        var gs = options.Value;
        if (string.IsNullOrWhiteSpace(gs.ServiceAccountJsonPath))
            throw new ArgumentException("ServiceAccountJsonPath not configured");

        string[] scopes = new[]
        {
            // Manage & list courses
            "https://www.googleapis.com/auth/classroom.courses",
            // Access rosters (teachers/students)
            "https://www.googleapis.com/auth/classroom.rosters",
            // Create/modify coursework for students
            "https://www.googleapis.com/auth/classroom.coursework.students",
            // Read user profile email (for whoami diagnostics)
            "https://www.googleapis.com/auth/classroom.profile.emails"
        };

            var configured = gs.ServiceAccountJsonPath;
          
            var repoRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
            var credPath = Path.Combine(repoRoot, configured);

        if (!File.Exists(credPath))
            throw new FileNotFoundException($"Could not find Google service account key at '{credPath}'. Set GOOGLE_APPLICATION_CREDENTIALS or configure GoogleSettings:ServiceAccountJsonPath.");

        GoogleCredential cred;
        using (var stream = new FileStream(credPath, FileMode.Open, FileAccess.Read))
        {
            #pragma warning disable CS0618 // Suppress deprecation warning; alternative factory pattern not available in current package version.
            cred = GoogleCredential.FromStream(stream)
                .CreateScoped(scopes)
                .CreateWithUser(gs.ImpersonatedAdmin);
            #pragma warning restore CS0618
        }

        ClassroomService = new ClassroomService(new BaseClientService.Initializer
        {
            HttpClientInitializer = cred,
            ApplicationName = "MySISIntegration-API"
        });
        AdminEmail = gs.ImpersonatedAdmin;

    }
}