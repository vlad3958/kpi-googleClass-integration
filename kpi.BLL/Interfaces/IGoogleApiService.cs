namespace kpi.BLL.Service;

using Google.Apis.Classroom.v1;

public interface IGoogleApiService
{
    ClassroomService ClassroomService { get; }
}