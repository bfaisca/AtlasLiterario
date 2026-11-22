using AtlasLiterario.Application.Abstractions;

namespace AtlasLiterario.Infrastructure.Services;

public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
