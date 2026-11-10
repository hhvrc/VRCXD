namespace VRCX.API.Attributes;

/// <summary>Marks a class with a public <c>Execute()</c> method as a Hangfire recurring job (cron schedule, https://crontab.guru/).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CronJobAttribute : Attribute
{
    public CronJobAttribute(string schedule, string? jobName = null)
    {
        Schedule = schedule;
        JobName = jobName;
    }

    public string Schedule { get; }
    public string? JobName { get; }
}
