using Google.Apis.Classroom.v1;
using Google.Apis.Classroom.v1.Data;
using kpi.API.Dto;
using kpi.BLL.Service;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace kpi.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InvitationsController : ControllerBase
    {
        private readonly IGoogleApiService _google;

        public InvitationsController(IGoogleApiService google)
        {
            _google = google;
        }

        // ==========================================
        // 1. —“¬Œ–≈ÕÕﬂ  ”–—”
        // ==========================================
        [HttpPost("create-full")]
        public async Task<ActionResult> CreateCourseWithRoster([FromBody] CreateClassroomRequest request)
        {
            if (request == null) return BadRequest("Body is required");

            var cleanTeachers = request.TeacherEmails?
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            var cleanStudents = request.StudentEmails?
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            if (cleanTeachers.Count == 0)
                return BadRequest("At least one teacher email is required.");

            var ownerId = cleanTeachers[0];
            var teachersToInvite = cleanTeachers.Skip(1).ToList();

            var service = _google.ClassroomService;

            var courseObj = new Course
            {
                Name = request.CourseName,
                Section = request.Section,
                DescriptionHeading = request.Description,
                Room = request.Room,
                OwnerId = ownerId,
                CourseState = "ACTIVE"
            };

            Course createdCourse;
            try
            {
                createdCourse = await service.Courses.Create(courseObj).ExecuteAsync();
            }
            catch (Google.GoogleApiException gex)
            {
                return StatusCode((int)gex.HttpStatusCode, new { error = "Failed to create course", googleMessage = gex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Unknown error", details = ex.Message });
            }

            var courseId = createdCourse.Id;
            var report = new
            {
                CourseId = courseId,
                Link = createdCourse.AlternateLink,
                Owner = createdCourse.OwnerId,
                Errors = new List<string>()
            };

            var tasks = new List<Task>();
            foreach (var email in teachersToInvite) tasks.Add(InviteUserAsync(service, courseId, email, "TEACHER", report.Errors));
            foreach (var email in cleanStudents) tasks.Add(InviteUserAsync(service, courseId, email, "STUDENT", report.Errors));
            await Task.WhenAll(tasks);

            return Ok(report);
        }

        // ==========================================
        // 2. Œ“–»Ã¿ÕÕﬂ Œ÷≤ÕŒ  (—”Ã¿ ¡¿À≤¬)
        // ==========================================
        [HttpGet("grades/all")]
        public async Task<ActionResult<AllGradesResponse>> GetAllGrades()
        {
            var service = _google.ClassroomService;
            var adminEmail = "ber@vlad.work.gd";

            var coursesRequest = service.Courses.List();
            coursesRequest.TeacherId = "me";

            var coursesResponse = await coursesRequest.ExecuteAsync();
            var courses = coursesResponse.Courses;

            if (courses == null || courses.Count == 0)
            {
                return Ok(new AllGradesResponse { AdminEmail = adminEmail, Courses = new List<CourseGrades>() });
            }

            var result = new AllGradesResponse { AdminEmail = adminEmail };

            var semaphore = new System.Threading.SemaphoreSlim(10);
            var tasks = courses.Select(async course =>
            {
                await semaphore.WaitAsync();
                try
                {
                    return await GetGradesForSingleCourseAsync(service, course);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var processedCourses = await Task.WhenAll(tasks);
            result.Courses = processedCourses.Where(c => c != null).ToList()!;

            return Ok(result);
        }

        // ==========================================
        // ƒŒœŒÃ≤∆Õ≤ Ã≈“Œƒ»
        // ==========================================

        private async Task<CourseGrades?> GetGradesForSingleCourseAsync(ClassroomService service, Course course)
        {
            try
            {
                var studentsReq = service.Courses.Students.List(course.Id);
                var studentsRes = await studentsReq.ExecuteAsync();
                var students = studentsRes.Students;

                if (students == null || students.Count == 0)
                    return new CourseGrades { CourseId = course.Id, CourseName = course.Name, Link = course.AlternateLink };

                var workReq = service.Courses.CourseWork.List(course.Id);
                var workRes = await workReq.ExecuteAsync();
                var courseWorks = workRes.CourseWork;

                if (courseWorks == null || courseWorks.Count == 0)
                {
                    // —ÚÛ‰ÂÌÚË ∫, Á‡‚‰‡Ì¸ ÌÂÏ‡∫ -> —ÛÏ‡ 0
                    return new CourseGrades
                    {
                        CourseId = course.Id,
                        CourseName = course.Name,
                        Link = course.AlternateLink,
                        Students = students.Select(s => new StudentGrades
                        {
                            StudentName = s.Profile.Name.FullName,
                            Email = s.Profile.EmailAddress,
                            UserId = s.UserId,
                            TotalScore = 0
                        }).ToList()
                    };
                }

                var workMap = courseWorks.ToDictionary(k => k.Id, v => new { v.Title, v.MaxPoints });

                var subsReq = service.Courses.CourseWork.StudentSubmissions.List(course.Id, "-");
                var subsRes = await subsReq.ExecuteAsync();
                var allSubmissions = subsRes.StudentSubmissions;

                var resultStudents = new List<StudentGrades>();

                foreach (var student in students)
                {
                    var sData = new StudentGrades
                    {
                        StudentName = student.Profile.Name.FullName,
                        Email = student.Profile.EmailAddress,
                        UserId = student.UserId
                    };

                    if (allSubmissions != null)
                    {
                        var studentSubs = allSubmissions.Where(sub => sub.UserId == student.UserId);
                        foreach (var sub in studentSubs)
                        {
                            if (workMap.TryGetValue(sub.CourseWorkId, out var workInfo))
                            {
                                sData.Grades.Add(new GradeItem
                                {
                                    WorkTitle = workInfo.Title,
                                    MaxPoints = workInfo.MaxPoints,
                                    Score = sub.AssignedGrade ?? sub.DraftGrade,
                                    State = sub.State
                                });
                            }
                        }
                    }

                    if (sData.Grades.Any(g => g.Score.HasValue))
                    {
                        sData.TotalScore = sData.Grades
                            .Where(g => g.Score.HasValue)
                            .Sum(g => g.Score.Value);
                    }
                    else
                    {
                        sData.TotalScore = 0;
                    }

                    resultStudents.Add(sData);
                }

                return new CourseGrades
                {
                    CourseId = course.Id,
                    CourseName = course.Name,
                    Link = course.AlternateLink,
                    Students = resultStudents
                };
            }
            catch
            {
                return null;
            }
        }

        private async Task InviteUserAsync(ClassroomService service, string courseId, string email, string role, List<string> errorLog)
        {
            if (!IsLikelyEmail(email)) return;
            var invitation = new Invitation { CourseId = courseId, UserId = email, Role = role };
            try
            {
                await service.Invitations.Create(invitation).ExecuteAsync();
            }
            catch (Google.GoogleApiException gex) when (gex.HttpStatusCode == HttpStatusCode.Conflict) { }
            catch (Exception ex)
            {
                lock (errorLog) { errorLog.Add($"Failed to invite {email} as {role}: {ex.Message}"); }
            }
        }

        private static bool IsLikelyEmail(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var parts = value.Split('@');
            return parts.Length == 2 && parts[1].Contains('.') && !value.Any(char.IsWhiteSpace);
        }
    }
}