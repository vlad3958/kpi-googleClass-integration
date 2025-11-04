namespace kpi.API.Dto
{
    public class BulkInvitationResult
    {
        public InvitationRequest Request { get; set; } = new();
        public bool Success { get; set; }
        public string? InvitationId { get; set; }
        public string? Error { get; set; }
    }
}
