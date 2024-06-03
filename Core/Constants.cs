namespace Core;

public static class Constants
{
    public const string ClusterId = "sessions-cluster";
    public const string ClientServiceId = "sessions-client";
    public const string ServerServiceId = "sessions-server";

    /// <summary>
    /// In replication
    /// </summary>
    public const string Next = "next";
    
    public const string SessionStreamNamespace = "sessions";
    public const string StreamProvider = "StreamProvider";
    
    // 1, 2...
    public const string SessionGrainOrder = "sgo";
}