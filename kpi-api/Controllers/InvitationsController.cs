using Google.Apis.Classroom.v1.Data;
using kpi.API.Dto;
using kpi.BLL.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace kpi.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InvitationsController : ControllerBase
    {
        private sealed class TeacherOpResult
        {
            public string Email { get; set; } = string.Empty;
            public string Mode { get; set; } = string.Empty; // added | invited | exists | failed
            public bool Success { get; set; }
            public string? Error { get; set; }
        }

        private readonly IGoogleApiService _google;

        public InvitationsController(IGoogleApiService google)
        {
            _google = google;
        }

        // Single endpoint: invite teachers (optional) and students to a course.
        // Accepts either numeric courseId OR a full Classroom URL OR the base64 token after /c/.
        // POST: api/invitations/course/{courseIdOrUrlOrToken}/bulk
        // Body: { "studentEmails": ["s1@x.com", "s2@x.com"], "teacherEmails": ["t1@x.com", "t2@x.com"] }
        [HttpPost("course/{courseId}/bulk")]
        public ActionResult BulkForCourse(string courseId, [FromBody] BulkCourseInvitationRequest body)
        {
            if (string.IsNullOrWhiteSpace(courseId)) return BadRequest("courseId is required");
            if (body == null) return BadRequest("Request body is required");
            if (!TryResolveCourseId(courseId, out var resolvedCourseId))
            {
                return BadRequest(new { error = "Unable to resolve courseId from input.", input = courseId, hint = "Pass numeric ID or full Classroom URL (with courseid=... or /c/<token>)." });
            }

            var service = _google.ClassroomService;
            try
            {

                var teacherResults = new List<TeacherOpResult>();
                int totalTeachersProcessed = 0;
                if (body.TeacherEmails != null && body.TeacherEmails.Count > 0)
                {
                    var validTeacherEmails = body.TeacherEmails
                        .Select(e => e?.Trim())
                        .Where(e => !string.IsNullOrWhiteSpace(e) && IsLikelyEmail(e!))
                        .Select(e => e!)
                        .Distinct(System.StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    totalTeachersProcessed = validTeacherEmails.Count;

                    foreach (var tEmail in validTeacherEmails)
                    {
                        try
                        {
                            var t = new Teacher { UserId = tEmail };
                            service.Courses.Teachers.Create(t, resolvedCourseId).Execute();
                            teacherResults.Add(new TeacherOpResult { Email = tEmail, Mode = "added", Success = true, Error = null });
                        }
                        catch (Google.GoogleApiException tex) when ((int)tex.HttpStatusCode == 409)
                        {
                            teacherResults.Add(new TeacherOpResult { Email = tEmail, Mode = "exists", Success = true, Error = null });
                        }
                        catch (Google.GoogleApiException)
                        {
                            try
                            {
                                var inv = new Invitation { CourseId = resolvedCourseId, UserId = tEmail, Role = "TEACHER" };
                                service.Invitations.Create(inv).Execute();
                                teacherResults.Add(new TeacherOpResult { Email = tEmail, Mode = "invited", Success = true, Error = null });
                            }
                            catch (Google.GoogleApiException tex2)
                            {
                                teacherResults.Add(new TeacherOpResult { Email = tEmail, Mode = "failed", Success = false, Error = tex2.Message });
                            }
                        }
                        catch (Exception ex)
                        {
                            teacherResults.Add(new TeacherOpResult { Email = tEmail, Mode = "failed", Success = false, Error = ex.Message });
                        }
                    }
                }

                // Invite students (always as STUDENT). Ignore empty or invalid emails silently.
                var validStudentEmails = (body.StudentEmails ?? new List<string>())
                    .Select(e => e?.Trim())
                    .Where(e => !string.IsNullOrWhiteSpace(e) && IsLikelyEmail(e!))
                    .Select(e => e!)
                    .Distinct(System.StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var studentResults = validStudentEmails.Select(email =>
                {
                    try
                    {
                        var invitation = new Invitation
                        {
                            CourseId = resolvedCourseId,
                            UserId = email,
                            Role = "STUDENT"
                        };
                        var id = service.Invitations.Create(invitation).Execute().Id;
                        return new BulkInvitationResult
                        {
                            Request = new InvitationRequest { CourseId = resolvedCourseId, UserEmail = email, Role = "STUDENT" },
                            Success = true,
                            InvitationId = id
                        };
                    }
                    catch (Google.GoogleApiException gex) when ((int)gex.HttpStatusCode == 409)
                    {
                        return new BulkInvitationResult
                        {
                            Request = new InvitationRequest { CourseId = resolvedCourseId, UserEmail = email, Role = "STUDENT" },
                            Success = true,
                            InvitationId = null,
                            Error = "Already invited or enrolled (409)."
                        };
                    }
                    catch (Google.GoogleApiException gex)
                    {
                        return new BulkInvitationResult
                        {
                            Request = new InvitationRequest { CourseId = resolvedCourseId, UserEmail = email, Role = "STUDENT" },
                            Success = false,
                            Error = gex.Message
                        };
                    }
                    catch (Exception ex)
                    {
                        return new BulkInvitationResult
                        {
                            Request = new InvitationRequest { CourseId = resolvedCourseId, UserEmail = email, Role = "STUDENT" },
                            Success = false,
                            Error = ex.Message
                        };
                    }
                }).ToList();

                var summary = new
                {
                    teachers = new
                    {
                        total = totalTeachersProcessed,
                        added = teacherResults.Count(r => r.Success && r.Mode == "added"),
                        invited = teacherResults.Count(r => r.Success && r.Mode == "invited"),
                        exists = teacherResults.Count(r => r.Success && r.Mode == "exists"),
                        failed = teacherResults.Count(r => !r.Success),
                        results = teacherResults
                    },
                    students = new
                    {
                        total = studentResults.Count,
                        success = studentResults.Count(r => r.Success),
                        failed = studentResults.Count(r => !r.Success),
                        results = studentResults
                    }
                };
                return Ok(summary);
            }
            catch (Google.GoogleApiException gex) when (gex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                return NotFound(new { error = "Course not found. Pass numeric courseId or a valid Classroom URL (not enrollment code).", details = gex.Message });
            }
        }

        private static bool IsLikelyEmail(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            // Lightweight heuristic: exactly one '@', local and domain parts present, domain contains a dot, and no spaces
            var parts = value.Split('@');
            if (parts.Length != 2) return false;
            if (parts[0].Length == 0) return false;
            var domain = parts[1];
            if (domain.Length < 3 || !domain.Contains('.')) return false;
            if (value.Any(char.IsWhiteSpace)) return false;
            return true;
        }

        private static bool TryResolveCourseId(string input, out string courseId)
        {
            courseId = string.Empty;
            if (string.IsNullOrWhiteSpace(input)) return false;
            try { input = Uri.UnescapeDataString(input); } catch { }

            // Case 1: already numeric
            if (input.All(char.IsDigit))
            {
                courseId = input;
                return true;
            }

            // Case 2: full URL
            if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
            {
                // try ?courseid=...
                var query = QueryHelpers.ParseQuery(uri.Query);
                if (query.TryGetValue("courseid", out var qVal))
                {
                    var v = qVal.ToString();
                    if (v.All(char.IsDigit)) { courseId = v; return true; }
                }

                // try /c/<segment>
                var segments = uri.AbsolutePath.Split('/', System.StringSplitOptions.RemoveEmptyEntries);
                var idx = System.Array.IndexOf(segments, "c");
                if (idx >= 0 && idx + 1 < segments.Length)
                {
                    var token = segments[idx + 1];
                    if (token.All(char.IsDigit)) { courseId = token; return true; }
                    if (TryDecodeBase64UrlToken(token, out var decoded) && decoded.All(char.IsDigit))
                    {
                        courseId = decoded; return true;
                    }
                }
            }
            else
            {
                // Case 3: just the token
                if (TryDecodeBase64UrlToken(input, out var decoded) && decoded.All(char.IsDigit))
                {
                    courseId = decoded; return true;
                }
            }
            return false;
        }

        private static bool TryDecodeBase64UrlToken(string token, out string decoded)
        {
            decoded = string.Empty;
            if (string.IsNullOrWhiteSpace(token)) return false;
            try
            {
                var b64 = token.Replace('-', '+').Replace('_', '/');
                switch (b64.Length % 4)
                {
                    case 2: b64 += "=="; break;
                    case 3: b64 += "="; break;
                }
                var bytes = System.Convert.FromBase64String(b64);
                decoded = Encoding.ASCII.GetString(bytes).Trim();
                return true;
            }
            catch { return false; }
        }
    }
}
