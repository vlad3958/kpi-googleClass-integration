namespace kpi.API.Dto
{
    public class InvitationRequest
    {
        public string CourseId { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public string Role { get; set; } = "STUDENT";
        // Optional: which teacher account to act as (OAuth). If empty, DefaultUserEmail will be used.
        public string? TeacherEmail { get; set; }
    }
}
