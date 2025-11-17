using System.Collections.Generic;

namespace kpi.API.Dto
{
    public class BulkCourseInvitationRequest
    {
            public List<string> StudentEmails { get; set; } = new();
            public List<string>? TeacherEmails { get; set; }
    }
}
