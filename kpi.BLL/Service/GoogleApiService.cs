using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Classroom.v1;
using Google.Apis.Admin.Directory.directory_v1;
using Microsoft.Extensions.Options;
using System.IO;
using System.Threading.Tasks;

public class GoogleApiService : IGoogleApiService
{
    public ClassroomService ClassroomService { get; }
    public DirectoryService DirectoryService { get; }

    public record GoogleSettings
    {
        public string ServiceAccountJsonPath { get; init; } = "kpi-proj-476910-7ee0d0b4ccd2.json";
        public string ImpersonatedAdmin { get; init; } = "kpi-service@kpi-proj-476910.iam.gserviceaccount.com";
    }
    public GoogleApiService(IOptions<GoogleSettings> options)
    {
        var gs = options.Value;
        if (string.IsNullOrEmpty(gs.ServiceAccountJsonPath))
            throw new System.ArgumentException("ServiceAccountJsonPath not configured");

        string[] scopes = new[]
        {
                "https://www.googleapis.com/auth/classroom.courses",
                "https://www.googleapis.com/auth/classroom.rosters",
                "https://www.googleapis.com/auth/classroom.coursework.students",
                "https://www.googleapis.com/auth/admin.directory.user"
            };

        GoogleCredential cred;
        using (var stream = new FileStream(gs.ServiceAccountJsonPath, FileMode.Open, FileAccess.Read))
        {
            cred = GoogleCredential.FromStream(stream)
                .CreateScoped(scopes)
                .CreateWithUser(gs.ImpersonatedAdmin);
        }

        ClassroomService = new ClassroomService(new BaseClientService.Initializer
        {
            HttpClientInitializer = cred,
            ApplicationName = "MySISIntegration-API"
        });

        DirectoryService = new DirectoryService(new BaseClientService.Initializer
        {
            HttpClientInitializer = cred,
            ApplicationName = "MySISIntegration-API"
        });
    }
}