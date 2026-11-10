using System.Reflection;
using Hangfire.Common;
using VRCX.API.Attributes;

namespace VRCX.API.Utils;

public static class CronJobCollector
{
    public sealed record CronJob(string Name, string Schedule, Job Job);

    public static List<CronJob> GetAllCronJobs()
    {
        List<CronJob> jobs = [];

        foreach (var type in typeof(CronJobCollector).Assembly.GetTypes())
        {
            if (!type.IsClass) continue;

            var attribute = type.GetCustomAttribute<CronJobAttribute>();
            if (attribute is null) continue;

            var execFunc = type.GetMethod("Execute", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Failed to find \"Execute()\" method of {type.FullName}");

            jobs.Add(new CronJob(attribute.JobName ?? type.Name, attribute.Schedule, new Job(execFunc)));
        }

        var duplicates = jobs.GroupBy(job => job.Name).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException($"Found the following CronJobs with duplicate names: {string.Join(", ", duplicates)}");

        return jobs;
    }
}
