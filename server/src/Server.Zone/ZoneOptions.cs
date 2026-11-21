namespace Realmforge.Server.Zone;

public sealed class ZoneOptions
{
    public const string SectionName = "Zone";

    public string ServerId { get; set; } = Environment.MachineName;
    /// <summary>Address clients use to reach this process; differs from the bind address behind NAT or in k8s.</summary>
    public string PublicHost { get; set; } = "127.0.0.1";
    public int UdpPort { get; set; } = 7777;
    public int WebSocketPort { get; set; } = 7778;
    public string ConnectionKey { get; set; } = "realmforge";
    /// <summary>Open-world zones this process simulates; empty means every non-instance zone in the content.</summary>
    public List<string> ZoneIds { get; set; } = new();
    public int MaxMessageBytes { get; set; } = 16 * 1024;
    public int SendQueueLength { get; set; } = 512;
    public TimeSpan HelloTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MembershipRefreshInterval { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>An instance with nobody connected is torn down after this grace period (lets players reconnect).</summary>
    public TimeSpan EmptyInstanceLifetime { get; set; } = TimeSpan.FromMinutes(2);
}
