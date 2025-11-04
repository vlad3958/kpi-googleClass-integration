using Google.Apis.Classroom.v1.Data;
using kpi.BLL.Service;
using kpi.API.Dto;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Collections.Generic;

namespace kpi.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InvitationsController : ControllerBase
    {
        private readonly IGoogleApiService _google;

    public InvitationsController(IGoogleApiService google) => _google = google;

        [HttpPost]
        public ActionResult Create([FromBody] InvitationRequest dto)
        {
            if (string.IsNullOrWhiteSpace(dto.CourseId) || string.IsNullOrWhiteSpace(dto.UserEmail))
                return BadRequest("CourseId and UserEmail are required");

            var id = CreateInvitation(dto.CourseId, dto.UserEmail, dto.Role);
            var get = _google.ClassroomService.Invitations.Get(id).Execute();
            return CreatedAtAction(nameof(Get), new { id }, new { get.Id, get.CourseId, get.UserId, get.Role });
        }

        [HttpGet("{id}")]
        public ActionResult Get(string id)
        {
            try
            {
                var inv = _google.ClassroomService.Invitations.Get(id).Execute();
                return Ok(new { inv.Id, inv.CourseId, inv.UserId, inv.Role });
            }
            catch (Google.GoogleApiException gex) when (gex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return NotFound();
            }
        }

        [HttpPost("bulk")]
        public ActionResult Bulk([FromBody] BulkInvitationRequest body)
        {
            if (body?.Items == null || body.Items.Count == 0) return BadRequest("Items are required");

            var results = body.Items.Select(item =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(item.CourseId) || string.IsNullOrWhiteSpace(item.UserEmail))
                        throw new ArgumentException("CourseId and UserEmail are required");

                    var id = CreateInvitation(item.CourseId, item.UserEmail, item.Role);
                    return new BulkInvitationResult { Request = item, Success = true, InvitationId = id };
                }
                catch (Google.GoogleApiException gex)
                {
                    return new BulkInvitationResult { Request = item, Success = false, Error = $"GoogleApiException {(int)gex.HttpStatusCode}: {gex.Message}" };
                }
                catch (Exception ex)
                {
                    return new BulkInvitationResult { Request = item, Success = false, Error = ex.Message };
                }
            }).ToList();

            var summary = new
            {
                total = results.Count,
                success = results.Count(r => r.Success),
                failed = results.Count(r => !r.Success),
                results
            };
            return Ok(summary);
        }

        private string CreateInvitation(string courseId, string userEmail, string role)
        {
            var invitation = new Invitation
            {
                CourseId = courseId,
                UserId = userEmail,
                Role = role?.Equals("TEACHER", System.StringComparison.OrdinalIgnoreCase) == true ? "TEACHER" : "STUDENT"
            };
            return _google.ClassroomService.Invitations.Create(invitation).Execute().Id;
        }
    }
}
