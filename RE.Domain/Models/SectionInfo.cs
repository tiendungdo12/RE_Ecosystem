namespace RE.Domain.Models;

public class SectionInfo
{
    public string Name { get; set; } = string.Empty;
    public double Entropy { get; set; }
    public long RawSize { get; set; }
    public long VirtualSize { get; set; }
    public string Characteristics { get; set; } = string.Empty;
    public bool IsWritableAndExecutable { get; set; }
    public List<string> Anomalies { get; set; } = new();
    public bool IsFlagged { get; set; }
    public string AnomaliesSummary => Anomalies.Count > 0 ? string.Join(", ", Anomalies) : "None";
}