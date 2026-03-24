namespace Elysium.WorkStation.Models
{
    public class MouseScheduleEntry
    {
        public DayOfWeek Day { get; set; }
        public bool IsEnabled { get; set; } = true;
        public TimeSpan StartTime { get; set; } = new(8, 0, 0);
        public TimeSpan EndTime { get; set; } = new(18, 0, 0);
    }
}
