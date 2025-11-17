using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace kpi.API.Dto
{
    public class CreateClassroomRequest
    {
        [Required]
        public string CourseName { get; set; } = string.Empty;

        public string Section { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Room { get; set; } = string.Empty;

        // Перший email у списку стане ВЛАСНИКОМ. Решта - запрошеними викладачами.
        [Required]
        public List<string> TeacherEmails { get; set; } = new();

        public List<string> StudentEmails { get; set; } = new();
    }
}