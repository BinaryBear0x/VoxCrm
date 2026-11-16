using VoxCrm.Application.Vaccinations;

namespace VoxCrm.Infrastructure.Jobs
{
    public class ReminderJob
    {
        private readonly IVaccinationReminderScheduler _scheduler;

        public ReminderJob(IVaccinationReminderScheduler scheduler)
        {
            _scheduler = scheduler;
        }

        public async Task ProcessDailyRemindersAsync()
        {
            await _scheduler.ReconcileAsync();
        }
    }
}
