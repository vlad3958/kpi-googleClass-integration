using System.Collections.Generic;

namespace kpi.API.Dto
{
    public class AllGradesResponse
    {
        public string AdminEmail { get; set; } = string.Empty;
        public List<CourseGrades> Courses { get; set; } = new();
    }

    public class CourseGrades
    {
        public string CourseId { get; set; } = string.Empty;
        public string CourseName { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
        public List<StudentGrades> Students { get; set; } = new();
    }

    public class StudentGrades
    {
        public string StudentName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;

        // Список усіх робіт
        public List<GradeItem> Grades { get; set; } = new();

        // Сума всіх отриманих балів
        public double TotalScore { get; set; } = 0;
    }

    public class GradeItem
    {
        public string WorkTitle { get; set; } = string.Empty;
        public double? Score { get; set; }      // Оцінка, яку поставив викладач
        public double? MaxPoints { get; set; }  // Максимальний бал за завдання
        public string State { get; set; } = string.Empty; // TURNED_IN, RETURNED, NEW тощо.
    }
}