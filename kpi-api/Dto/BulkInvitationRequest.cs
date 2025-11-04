using System.Collections.Generic;

namespace kpi.API.Dto
{
    public class BulkInvitationRequest
    {
        public List<InvitationRequest> Items { get; set; } = new();
    }
}
