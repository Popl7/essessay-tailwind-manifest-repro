namespace Essessay.Models;

public record FlakyViewModel(string Label, int Attempt, DateTimeOffset At);

public record SlowRequestViewModel(string Mode, DateTimeOffset StartedAt, DateTimeOffset FinishedAt);
