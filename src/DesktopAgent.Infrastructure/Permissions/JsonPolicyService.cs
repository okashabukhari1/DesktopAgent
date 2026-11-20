using System.Text.Json;
using System.Collections.Concurrent;
using DesktopAgent.Core.Permissions;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Infrastructure.Permissions;

public sealed class JsonPolicyService : IPolicyService
{
    private readonly ILogger<JsonPolicyService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ConcurrentDictionary<string, PermissionPolicy> _policies = new(StringComparer.OrdinalIgnoreCase);

    public JsonPolicyService(ILogger<JsonPolicyService> logger, string? baseDirectory = null)
    {
        _logger = logger;
        var root = baseDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopAgent");
        PermissionsPath = Path.Combine(root, "permissions.json");
    }

    public string PermissionsPath { get; }
    public IReadOnlyList<AgentCapability> Capabilities => AgentCapabilities.All;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _policies.Clear();
            if (File.Exists(PermissionsPath))
            {
                try
                {
                    await using var input = File.OpenRead(PermissionsPath);
                    var saved = await JsonSerializer.DeserializeAsync<Dictionary<string, PermissionPolicy>>(input, _jsonOptions, cancellationToken).ConfigureAwait(false);
                    if (saved is not null)
                    {
                        foreach (var (id, policy) in saved)
                        {
                            if (AgentCapabilities.Find(id) is not null && Enum.IsDefined(policy))
                                _policies[id] = policy;
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(exception, "Permission settings could not be read; safe defaults will be used");
                }
            }
        }
        finally { _gate.Release(); }
    }

    public PermissionPolicy GetPolicy(string capabilityId)
    {
        var capability = AgentCapabilities.Find(capabilityId);
        if (capability is null) throw new ArgumentException("Unknown capability.", nameof(capabilityId));
        return _policies.TryGetValue(capability.Id, out var configured) ? configured : capability.DefaultPolicy;
    }

    public PolicyEvaluation Evaluate(string capabilityId)
    {
        var capability = AgentCapabilities.Find(capabilityId);
        if (capability is null)
            return new(capabilityId, PolicyOutcome.Unavailable, PermissionPolicy.Deny, "This capability is not registered.");
        if (capability.Availability != CapabilityAvailability.Available)
            return new(capability.Id, PolicyOutcome.Unavailable, GetPolicy(capability.Id), capability.Requirement);
        var policy = GetPolicy(capability.Id);
        return policy switch
        {
            PermissionPolicy.Allow => new(capability.Id, PolicyOutcome.Allowed, policy, "Allowed by your permission settings."),
            PermissionPolicy.Ask => new(capability.Id, PolicyOutcome.ConfirmationRequired, policy, "Your confirmation is required for this action."),
            _ => new(capability.Id, PolicyOutcome.Denied, policy, "This capability is denied in your permission settings.")
        };
    }

    public async Task SetPolicyAsync(string capabilityId, PermissionPolicy policy, CancellationToken cancellationToken = default)
    {
        var capability = AgentCapabilities.Find(capabilityId);
        if (capability is null) throw new ArgumentException("Unknown capability.", nameof(capabilityId));
        if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _policies[capability.Id] = policy;
            await SaveUnderLockAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _policies.Clear();
            await SaveUnderLockAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task SaveUnderLockAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(PermissionsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = PermissionsPath + ".tmp";
        await using (var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            await JsonSerializer.SerializeAsync(output, _policies, _jsonOptions, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, PermissionsPath, overwrite: true);
        _logger.LogInformation("PermissionPoliciesSaved CapabilityCount={CapabilityCount}", _policies.Count);
    }
}
