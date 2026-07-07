namespace VoxCrm.Application.Audit;

public static class AuditLogLevels
{
    public const string Debug = "Debug";
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Error = "Error";
    public const string Critical = "Critical";
}

public static class AuditLogSources
{
    public const string Web = "Web";
    public const string Api = "Api";
    public const string Gateway = "Gateway";
    public const string Worker = "Worker";
    public const string Hangfire = "Hangfire";
}

public static class AuditLogCategories
{
    public const string Security = "Security";
    public const string Operation = "Operation";
    public const string WhatsApp = "WhatsApp";
    public const string Validation = "Validation";
    public const string Exception = "Exception";
    public const string System = "System";
}

public static class AuditLogOutcomes
{
    public const string Success = "Success";
    public const string Failed = "Failed";
    public const string Denied = "Denied";
    public const string Deferred = "Deferred";
    public const string RetryScheduled = "RetryScheduled";
}
