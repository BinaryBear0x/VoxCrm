namespace VoxCrm.Domain.Entities
{
    public static class WhatsAppNotificationStatuses
    {
        public const string Pending = "Pending";
        public const string Processing = "Processing";
        public const string RetryScheduled = "RetryScheduled";
        public const string Sent = "Sent";
        public const string Failed = "Failed";
        public const string Cancelled = "Cancelled";
        public const string NeedsReview = "NeedsReview";

        public static readonly string[] Claimable = { Pending, RetryScheduled };
        public static readonly string[] Terminal = { Sent, Failed, Cancelled, NeedsReview };
    }
}
