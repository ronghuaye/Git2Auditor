namespace Git2Auditor.Models;

public class GroupHealthData
{
    public int GroupId { get; set; }
    public string RepoOwner { get; set; } = string.Empty;
    public string RepoName { get; set; } = string.Empty;
    
    // GitHub Actions 综合得分 (结合配置数量、执行频次、成功率)
    public double CICDScore { get; set; }
    
    // 小组 PR 活跃度 (基于 PR 总数、合并率及讨论质量)
    public double PRActivityScore { get; set; }
    
    // 文档完善度得分 (多维度评估：包含 README 质量、文档更新实效性、docs 目录规范化等)
    public double DocumentationScore { get; set; }

    // Issue 管理得分 (基于 Issue 总数及解决率)
    public double IssueScore { get; set; }

    // 代码审核得分 (基于 PR 的 Review 覆盖率、评论互动及 Approved 比例)
    public double CodeReviewScore { get; set; }

    // 小组综合健康度 (五个维度平均值)
    public double GroupHealthScore => (CICDScore + PRActivityScore + DocumentationScore + IssueScore + CodeReviewScore) / 5.0;

    // 雷达图数据数组，方便 UI 绑定
    public double[] RadarValues => new[] 
    { 
        CICDScore, 
        PRActivityScore, 
        CodeReviewScore, 
        DocumentationScore, 
        IssueScore 
    };
}