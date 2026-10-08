namespace WelcomeMat.Data.Entities;

/// <summary>
/// One application-wide setting.
/// </summary>
public class Setting
{
    public required string Key { get; set; }
    public required string Value { get; set; }
}
