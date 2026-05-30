using System;

namespace Git2Auditor.Models;

public class IndividualData
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

    // 综合得分 (原本叫 HealthScore)
    public double IndividualScore { get; set; }
}

public class StudentPerformanceRecord
{
    public StudentInfo Student { get; set; } = new();
    public IndividualData Data { get; set; } = new();
    public string Remarks { get; set; } = string.Empty;
    
    // 预警标识：是否属于“单体英雄主义”包揽者
    public bool IsSoloHero { get; set; }
}
