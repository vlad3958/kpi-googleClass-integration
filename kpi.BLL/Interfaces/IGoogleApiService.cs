using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Classroom.v1;
using Google.Apis.Admin.Directory.directory_v1;
using Microsoft.Extensions.Options;
using System.IO;
using System.Threading.Tasks;
public interface IGoogleApiService
{
    ClassroomService ClassroomService { get; }
    DirectoryService DirectoryService { get; }
}