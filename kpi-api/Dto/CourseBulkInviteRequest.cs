using System.Collections.Generic;

namespace kpi.API.Dto
{
    public class CourseBulkInviteRequest
    {
        // Accepts either a numeric courseId or a full Classroom course link
        public string Course { get; set; } = string.Empty;
        public List<string> TeacherEmails { get; set; } = new();
        public List<string> StudentEmails { get; set; } = new();
    }
}
