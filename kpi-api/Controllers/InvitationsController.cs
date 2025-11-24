using Google.Apis.Classroom.v1;
using Google.Apis.Classroom.v1.Data;
using kpi.API.Dto;
using kpi.BLL.Service;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
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

        [HttpPost("course/invite")]
        public async Task<ActionResult> InviteTeachersAndStudents([FromBody] CourseBulkInviteRequest request)
        {
            if (request == null) return BadRequest("Body required");
            if (string.IsNullOrWhiteSpace(request.Course)) return BadRequest("Course id or link required");

            var courseId = ResolveCourseId(request.Course);
            if (string.IsNullOrWhiteSpace(courseId)) return BadRequest("Unable to extract numeric courseId from provided value");

            var service = _google.ClassroomService;
            // Validate course exists (optional but helpful)
            try
            {
                _ = await service.Courses.Get(courseId).ExecuteAsync();
            }
            catch (Google.GoogleApiException gex) when (gex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                return NotFound(new { error = "Course not found or access denied", courseId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to validate course", details = ex.Message });
            }

            var cleanTeachers = (request.TeacherEmails ?? new List<string>())
                .Where(IsLikelyEmail)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var cleanStudents = (request.StudentEmails ?? new List<string>())
                .Where(IsLikelyEmail)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var errors = new List<string>();
            var tasks = new List<Task>();
            foreach (var t in cleanTeachers) tasks.Add(InviteUserAsync(service, courseId, t, "TEACHER", errors));
            foreach (var s in cleanStudents) tasks.Add(InviteUserAsync(service, courseId, s, "STUDENT", errors));
            await Task.WhenAll(tasks);

            var result = new
            {
                CourseId = courseId,
                TeachersInvited = cleanTeachers.Count,
                StudentsInvited = cleanStudents.Count,
                Errors = errors
            };
            return Ok(result);
        }

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

            var ownerId = "ber@vlad.work.gd";

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
            foreach (var email in cleanTeachers) tasks.Add(InviteUserAsync(service, courseId, email, "TEACHER", report.Errors));
            foreach (var email in cleanStudents) tasks.Add(InviteUserAsync(service, courseId, email, "STUDENT", report.Errors));
            await Task.WhenAll(tasks);

            return Ok(report);
        }

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


        [HttpGet("courses/id-by-name")]
        public async Task<ActionResult> GetCourseIdByName([FromQuery] string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return BadRequest("name query parameter required");
            var service = _google.ClassroomService;
            string? pageToken = null;
            do
            {
                var req = service.Courses.List();
                req.PageToken = pageToken;
                req.TeacherId = "me";
                var resp = await req.ExecuteAsync();
                if (resp.Courses != null)
                {
                    var match = resp.Courses.FirstOrDefault(c => string.Equals(c.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        return Ok(new { match.Id, match.Name, match.AlternateLink });
                    }
                }
                pageToken = resp.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));

            return NotFound(new { error = "Course not found", name });
        }

        [HttpGet("courses/{courseId}/grades")]
        public async Task<ActionResult> GetGradesForCourse(string courseId)
        {
            if (string.IsNullOrWhiteSpace(courseId)) return BadRequest("courseId required");
            var service = _google.ClassroomService;
            try
            {
                var course = await service.Courses.Get(courseId).ExecuteAsync();
                var data = await GetGradesForSingleCourseAsync(service, course);
                if (data == null) return NotFound(new { error = "No data or inaccessible", courseId });
                return Ok(data);
            }
            catch (Google.GoogleApiException gex) when (gex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                return NotFound(new { error = "Course not found", courseId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to retrieve course grades", details = ex.Message });
            }
        }

        [HttpGet("grades/summary")]
        public async Task<ActionResult> GetGradesSummary()
        {
            var service = _google.ClassroomService;
            var coursesReq = service.Courses.List();
            coursesReq.TeacherId = "me";
            var coursesResp = await coursesReq.ExecuteAsync();
            var courses = coursesResp.Courses ?? new List<Course>();
            if (courses.Count == 0) return Ok(new List<object>());

            var semaphore = new System.Threading.SemaphoreSlim(10);
            var courseTasks = courses.Select(c => GetCourseSummaryAsync(service, c, semaphore));
            var summaries = await Task.WhenAll(courseTasks);
            return Ok(summaries);
        }

        [HttpGet("courses/ids")]
        public async Task<ActionResult> GetCourseIdsForAdmin()
        {
            var service = _google.ClassroomService;
            var adminEmail = "ber@vlad.work.gd"; // impersonated admin
            var list = new List<object>();
            string? pageToken = null;
            try
            {
                do
                {
                    var req = service.Courses.List();
                    req.PageToken = pageToken;
                    req.TeacherId = "me";
                    var resp = await req.ExecuteAsync();
                    if (resp.Courses != null)
                    {
                        list.AddRange(resp.Courses.Select(c => (object)new { c.Id, c.Name, c.Section, c.AlternateLink }));
                    }
                    pageToken = resp.NextPageToken;
                } while (!string.IsNullOrEmpty(pageToken));
                return Ok(new { AdminEmail = adminEmail, Count = list.Count, Courses = list });
            }
            catch (Google.GoogleApiException gex)
            {
                return StatusCode((int)gex.HttpStatusCode, new { error = "Failed to list courses", googleMessage = gex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Unknown error", details = ex.Message });
            }
        }


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

                    sData.TotalScore = sData.Grades
                        .Where(g => g.Score.HasValue)
                        .Sum(g => g.Score ?? 0);

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

        private static string ResolveCourseId(string input)
        {
            var trimmed = input.Trim();
            if (trimmed.All(char.IsDigit)) return trimmed; // pure numeric

            // Try query param courseid=123456789
            var matchQuery = Regex.Match(trimmed, "courseid=(\\d+)");
            if (matchQuery.Success) return matchQuery.Groups[1].Value;

            // Extract last numeric segment if any
            var segments = trimmed.Split('/', '?');
            foreach (var seg in segments.Reverse())
            {
                var s = seg.Trim();
                if (s.All(char.IsDigit) && s.Length > 5) return s; // heuristic length
            }
            return string.Empty;
        }

        private async Task<object> GetCourseSummaryAsync(ClassroomService service, Course course, System.Threading.SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            try
            {
                var data = await GetGradesForSingleCourseAsync(service, course);
                if (data == null || data.Students == null || data.Students.Count == 0)
                    return new { course.Id, course.Name, AverageScore = 0, Students = 0 };
                var scores = data.Students.Select(s => s.TotalScore).ToList();
                double avg = scores.Count == 0 ? 0 : scores.Average();
                return new { course.Id, course.Name, AverageScore = avg, Students = data.Students.Count };
            }
            catch
            {
                return new { course.Id, course.Name, AverageScore = 0.0, Students = 0 };
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}