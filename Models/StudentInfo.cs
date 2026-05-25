namespace Git2Auditor.Models;

public class StudentInfo
{
    public string StudentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsLeader { get; set; }
    public string GitHubUsername { get; set; } = string.Empty;
    public int GroupId { get; set; }
}
