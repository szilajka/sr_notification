namespace SrNotification.Data;

/// <summary>Column lengths, shared by the EF configuration and the code that fills the columns.</summary>
public static class FieldLengths
{
    public const int Url = 2048;
    public const int Title = 1000;
    public const int Author = 500;
    public const int ExternalId = 1000;
    public const int ETag = 500;
    public const int Error = 2000;
    public const int Email = 320;
    public const int Name = 200;
    public const int UserExternalId = 200;
    public const int TokenHash = 64;
}
