using System;

namespace Git2Auditor.Models;

public class CollaborationData
{
    // Commit 维度
    public int CommitsCount { get; set; }
    public int LinesAdded { get; set; }
    public int LinesDeleted { get; set; }

    // Issue 维度
    public int IssuesAssigned { get; set; }
    public int IssuesResolved { get; set; }
    public double IssueResolveRate => IssuesAssigned == 0 ? 0 : Math.Round((double)IssuesResolved / IssuesAssigned * 100, 2);

    // PR 维度
    public int PrsParticipated { get; set; }
    public double PrAverageMergeTimeHours { get; set; }
    public int PrDiscussionCount { get; set; }

    // 综合健康度计算 (满分 100 分，按特定权重聚合)
    public double HealthScore { get; set; }
}

public class StudentPerformanceRecord
{
    public StudentInfo Student { get; set; } = new();
    public CollaborationData Data { get; set; } = new();
    public string Remarks { get; set; } = string.Empty;
}
