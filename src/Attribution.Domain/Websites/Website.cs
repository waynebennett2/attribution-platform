namespace Attribution.Domain.Websites;

// A tracked property and its configuration (spec.md Key Entities §Website).
public class Website
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;
    public IReadOnlyList<string> PermittedOrigins { get; private set; } = Array.Empty<string>();
    public string DefaultNumber { get; private set; } = string.Empty;
    public int SessionTimeoutSeconds { get; private set; } = 1800; // FR-012 default: 30 minutes
    public int HeartbeatIntervalSeconds { get; private set; } = 300; // FR-012 default: 5 minutes
    public int AllocationWindowExtensionSeconds { get; private set; } = 1800; // FR-018 default: 30 minutes
    public int CooldownSeconds { get; private set; } = 1800; // FR-006: must be >= extension
    public bool ConsentRequired { get; private set; } = true;
    public bool ShadowModeEnabled { get; private set; } // FR-049, default false
    public string? BusinessUnit { get; private set; }
    public string LocalTimezone { get; private set; } = "UTC"; // FR-023 time-of-day evaluation
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private Website() { }

    public static Website Create(
        string name,
        IReadOnlyList<string> permittedOrigins,
        string defaultNumber,
        string localTimezone,
        string? businessUnit = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A website must have a name.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(defaultNumber))
        {
            throw new ArgumentException("A website must have a default fallback number (FR-007).", nameof(defaultNumber));
        }

        return new Website
        {
            Name = name,
            PermittedOrigins = permittedOrigins,
            DefaultNumber = defaultNumber,
            LocalTimezone = string.IsNullOrWhiteSpace(localTimezone) ? "UTC" : localTimezone,
            BusinessUnit = businessUnit,
        };
    }

    // FR-006: the cooldown must never be shorter than the allocation-window extension,
    // or two allocation windows for the same number could overlap.
    public void SetAllocationTiming(int allocationWindowExtensionSeconds, int cooldownSeconds)
    {
        if (cooldownSeconds < allocationWindowExtensionSeconds)
        {
            throw new InvalidOperationException(
                "Cooldown must be at least as long as the allocation window extension (FR-006).");
        }

        AllocationWindowExtensionSeconds = allocationWindowExtensionSeconds;
        CooldownSeconds = cooldownSeconds;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void EnableShadowMode() { ShadowModeEnabled = true; UpdatedAt = DateTimeOffset.UtcNow; }

    public void DisableShadowMode() { ShadowModeEnabled = false; UpdatedAt = DateTimeOffset.UtcNow; }

    // Reconstructs a Website from stored state (Infrastructure only, see AssemblyInfo.cs) —
    // bypasses Create()'s validation since a persisted row was already valid when written.
    internal static Website Rehydrate(
        Guid id,
        string name,
        IReadOnlyList<string> permittedOrigins,
        string defaultNumber,
        int sessionTimeoutSeconds,
        int heartbeatIntervalSeconds,
        int allocationWindowExtensionSeconds,
        int cooldownSeconds,
        bool consentRequired,
        bool shadowModeEnabled,
        string? businessUnit,
        string localTimezone,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) => new()
        {
            Id = id,
            Name = name,
            PermittedOrigins = permittedOrigins,
            DefaultNumber = defaultNumber,
            SessionTimeoutSeconds = sessionTimeoutSeconds,
            HeartbeatIntervalSeconds = heartbeatIntervalSeconds,
            AllocationWindowExtensionSeconds = allocationWindowExtensionSeconds,
            CooldownSeconds = cooldownSeconds,
            ConsentRequired = consentRequired,
            ShadowModeEnabled = shadowModeEnabled,
            BusinessUnit = businessUnit,
            LocalTimezone = localTimezone,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
}
