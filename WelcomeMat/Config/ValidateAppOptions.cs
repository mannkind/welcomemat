using Microsoft.Extensions.Options;

namespace WelcomeMat.Config;

/// <summary>
/// An class representing the options validation that runs at startup.
/// </summary>
public class ValidateAppOptions : IValidateOptions<AppOptions>
{
    /// <summary>
    /// Initializes a new instance of the ValidateAppOptions class.
    /// </summary>
    /// <param name="environmentName"></param>
    public ValidateAppOptions(string environmentName)
    {
        this.EnvironmentName = environmentName;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AppOptions options)
    {
        var errors = options.Validate(this.EnvironmentName);
        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }

    /// <summary>
    /// The environment the options are validated against.
    /// </summary>
    private readonly string EnvironmentName;
}
