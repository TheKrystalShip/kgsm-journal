using System.Text.Json;
using Microsoft.Extensions.Logging;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// Implementation of the IBlueprintService interface for managing blueprints in KGSM.
/// </summary>
public class BlueprintService : IBlueprintService
{
    /// <summary>The engine's blueprint skeleton, under the engine-reported templates directory.</summary>
    private const string ScaffoldFileName = "blueprint.tp";

    private readonly IKgsmCommandExecutor _commandExecutor;
    private readonly ILogger<BlueprintService> _logger;

    /// <summary>
    /// Initializes a new instance of the BlueprintService class.
    /// </summary>
    /// <param name="commandExecutor">The command executor to use for executing KGSM commands.</param>
    /// <param name="logger">The logger to use for logging.</param>
    public BlueprintService(IKgsmCommandExecutor commandExecutor, ILogger<BlueprintService> logger)
    {
        _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogDebug("BlueprintService initialized");
    }

    /// <inheritdoc/>
    public List<string> List()
    {
        _logger.LogDebug("Listing all blueprints");

        List<string>? blueprintNames = _commandExecutor
            .ExecuteForJson<List<string>>(["blueprints", "list", "--json"]);

        if (blueprintNames == null)
        {
            _logger.LogWarning("No blueprint names found");
            return new();
        }

        _logger.LogDebug("Found {Count} blueprint names", blueprintNames.Count);
        return blueprintNames;
    }

    /// <inheritdoc/>
    public List<string> ListDefault()
    {
        _logger.LogDebug("Listing default blueprints");

        List<string>? blueprintNames = _commandExecutor
            .ExecuteForJson<List<string>>(["blueprints", "list", "default", "--json"]);

        if (blueprintNames == null)
        {
            _logger.LogWarning("No default blueprint names found");
            return new();
        }

        _logger.LogDebug("Found {Count} default blueprint names", blueprintNames.Count);
        return blueprintNames;
    }

    /// <inheritdoc/>
    public List<string> ListCustom()
    {
        _logger.LogDebug("Listing custom blueprints");

        List<string>? blueprintNames = _commandExecutor
            .ExecuteForJson<List<string>>(["blueprints", "list", "custom", "--json"]);

        if (blueprintNames == null)
        {
            _logger.LogWarning("No custom blueprint names found");
            return new();
        }

        _logger.LogDebug("Found {Count} custom blueprint names", blueprintNames.Count);
        return blueprintNames;
    }

    /// <inheritdoc/>
    public Dictionary<string, Blueprint> ListDetailed()
    {
        _logger.LogDebug("Listing detailed blueprints");

        Dictionary<string, Blueprint>? detailedBlueprints = _commandExecutor
            .ExecuteForJson<Dictionary<string, Blueprint>>(["blueprints", "list", "detailed", "--json"]);

        if (detailedBlueprints == null)
        {
            _logger.LogWarning("No detailed blueprints found");
            return new();
        }

        _logger.LogDebug("Found {Count} detailed blueprints", detailedBlueprints.Count);
        return detailedBlueprints;
    }

    /// <inheritdoc/>
    public Blueprint? GetInfo(string blueprintName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintName, nameof(blueprintName));

        _logger.LogDebug("Getting info for blueprint: {Name}", blueprintName);

        Blueprint? blueprint = _commandExecutor
            .ExecuteForJson<Blueprint>(["blueprints", "info", blueprintName, "--json"]);

        if (blueprint != null)
        {
            _logger.LogDebug("Successfully retrieved info for blueprint: {Name}", blueprintName);
        }

        return blueprint;
    }

    /// <inheritdoc/>
    public string? FindPath(string blueprintName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintName, nameof(blueprintName));

        _logger.LogDebug("Finding path for blueprint: {Name}", blueprintName);

        KgsmResult result = _commandExecutor
            .Execute("blueprints", "find", blueprintName);

        if (result.ExitCode != 0)
        {
            _logger.LogError("Failed to find blueprint path for {Name}: {Error}", blueprintName, result.Stderr);
            return null;
        }

        string path = result.Stdout.Trim();

        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("Blueprint path for {Name} is empty", blueprintName);
            return null;
        }

        _logger.LogDebug("Found blueprint path for {Name}: {Path}", blueprintName, path);
        return path;
    }

    /// <inheritdoc/>
    public BlueprintCandidates? FindAll(string blueprintName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintName, nameof(blueprintName));

        _logger.LogDebug("Resolving blueprint candidates for: {Name}", blueprintName);

        // A name that exists in neither tier exits non-zero with no JSON, which surfaces here as null —
        // the honest "resolves to nothing", not an empty candidate set.
        BlueprintCandidates? candidates = _commandExecutor
            .ExecuteForJson<BlueprintCandidates>(["blueprints", "find", blueprintName, "--json"]);

        if (candidates is null)
        {
            _logger.LogDebug("No blueprint candidates found for: {Name}", blueprintName);
            return null;
        }

        _logger.LogDebug("Resolved {Name} to {Path} ({Count} candidates)",
            blueprintName, candidates.Resolved, candidates.Candidates.Count);
        return candidates;
    }

    /// <inheritdoc/>
    public BlueprintValidation? Validate(string blueprintNameOrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintNameOrPath, nameof(blueprintNameOrPath));

        _logger.LogDebug("Validating blueprint: {NameOrPath}", blueprintNameOrPath);

        // Probe, not Execute: an INVALID blueprint is a successful check with a negative verdict, and the
        // engine reports it as a non-zero exit carrying the JSON verdict on stdout. ExecuteForJson would
        // discard exactly the answer that matters, so the verdict is deserialized here instead.
        KgsmResult result = _commandExecutor
            .Probe("blueprints", "validate", blueprintNameOrPath, "--json");

        if (string.IsNullOrWhiteSpace(result.Stdout))
        {
            // No JSON at all — the name resolved to nothing, or the engine failed before it could judge.
            // Unknown, never an assumed pass.
            _logger.LogWarning("Blueprint validation for {NameOrPath} returned no verdict (exit {ExitCode}): {Error}",
                blueprintNameOrPath, result.ExitCode, result.Stderr);
            return null;
        }

        BlueprintValidation? validation;
        try
        {
            validation = JsonSerializer.Deserialize(result.Stdout, KgsmJsonContext.Default.BlueprintValidation);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize blueprint validation verdict for {NameOrPath}", blueprintNameOrPath);
            return null;
        }

        if (validation is not null)
        {
            _logger.LogDebug("Blueprint {NameOrPath} valid={Valid} ({Count} errors)",
                blueprintNameOrPath, validation.Valid, validation.Errors.Count);
        }

        return validation;
    }

    /// <inheritdoc/>
    public string? GetScaffold()
    {
        _logger.LogDebug("Reading the blueprint scaffold template");

        KgsmPaths? paths;
        try { paths = _commandExecutor.ExecuteForJson<KgsmPaths>(["--paths", "--json"]); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query kgsm --paths --json for the templates directory");
            return null;
        }

        string? templatesDir = paths?.System?.TemplatesDir;
        if (string.IsNullOrWhiteSpace(templatesDir))
        {
            _logger.LogWarning("kgsm --paths --json did not report a templates directory");
            return null;
        }

        string path = Path.Combine(templatesDir.Trim(), ScaffoldFileName);

        try
        {
            string content = File.ReadAllText(path);
            _logger.LogDebug("Read the blueprint scaffold from {Path} ({Length} bytes)", path, content.Length);
            return content;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to read the blueprint scaffold at {Path}", path);
            return null;
        }
    }

    /// <inheritdoc/>
    public BlueprintRequirements? BlueprintRequirements(string blueprintName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintName, nameof(blueprintName));

        // Probe: an absent requirement exits non-zero and still prints the answer worth reading.
        KgsmResult result = _commandExecutor.Probe("blueprints", "requirements", blueprintName, "--json");

        if (string.IsNullOrWhiteSpace(result.Stdout))
        {
            _logger.LogWarning("Blueprint requirements for {Blueprint} returned no answer (exit {ExitCode}): {Error}",
                blueprintName, result.ExitCode, result.Stderr);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(result.Stdout, KgsmJsonContext.Default.BlueprintRequirements);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize requirements for blueprint {Blueprint}", blueprintName);
            return null;
        }
    }
}
