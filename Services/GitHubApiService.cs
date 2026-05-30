using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Git2Auditor.Models;
using Octokit;

namespace Git2Auditor.Services;

public class GitHubApiService : IGitHubApiService
{
    public async Task<(List<GroupHealthData> GroupHealths, List<StudentPerformanceRecord> IndividualRecords)> FetchAndAggregateDataAsync(string pat, List<GroupRepoConfig> groupConfigs, List<StudentInfo> students)
    {
        var github = new GitHubClient(new ProductHeaderValue("Git2Auditor-Agent"))
        {
            Credentials = new Credentials(pat)
        };

        var allRecords = new List<StudentPerformanceRecord>();
        var groupHealths = new List<GroupHealthData>();

        foreach (var config in groupConfigs)
        {
            var groupStudents = students.Where(s => s.GroupId == config.GroupId).ToList();
            var groupHealth = new GroupHealthData { GroupId = config.GroupId };

            var (owner, repo) = ParseOwnerAndRepo(config.RepoOwner, config.RepoName);
            groupHealth.RepoOwner = owner;
            groupHealth.RepoName = repo;

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                foreach (var student in groupStudents)
                {
                    allRecords.Add(new StudentPerformanceRecord { Student = student, Remarks = "未配置仓库" });
                }
                groupHealths.Add(groupHealth);
                continue;
            }

            try
            {
                // ==================== 宏观维度：小组整体指标评估 ====================
                
                var commits = await github.Repository.Commit.GetAll(owner, repo);
                
                // 维度 1: CI/CD 综合得分
                groupHealth.CICDScore = 0;
                try
                {
                    var workflows = await github.Actions.Workflows.List(owner, repo);
                    if (workflows.TotalCount > 0)
                    {
                        double workflowCountScore = Math.Min(40, workflows.TotalCount * 10);
                        var runs = await github.Actions.Workflows.Runs.List(owner, repo);
                        double runFrequencyScore = Math.Min(30, runs.TotalCount * 2);
                        double successRate = runs.TotalCount > 0 ? (double)runs.WorkflowRuns.Count(r => r.Conclusion == WorkflowRunConclusion.Success) / runs.TotalCount : 0;
                        double successScore = successRate * 30;
                        groupHealth.CICDScore = Math.Round(workflowCountScore + runFrequencyScore + successScore, 1);
                    }
                }
                catch { /* 无 Actions 或 权限 */ }

                // 维度 2: 文档完善度
                groupHealth.DocumentationScore = 0;
                try
                {
                    var readme = await github.Repository.Content.GetReadme(owner, repo);
                    if (readme != null)
                    {
                        int length = readme.Content?.Length ?? 0;
                        if (length > 500) groupHealth.DocumentationScore = 80;
                        if (length > 2000) groupHealth.DocumentationScore = 100;
                    }
                }
                catch { /* 无 README */ }

                // ==================== 微观维度：个人协作指标评估 ====================
                
                var issuesTask = github.Issue.GetAllForRepository(owner, repo, new RepositoryIssueRequest { State = ItemStateFilter.All });
                var prsTask = github.PullRequest.GetAllForRepository(owner, repo, new PullRequestRequest { State = ItemStateFilter.All });

                await Task.WhenAll(issuesTask, prsTask);

                var issues = issuesTask.Result.Where(i => i.PullRequest == null).ToList();
                var prs = prsTask.Result;

                // 维度 3: PR 活跃度与质量评估
                try
                {
                    double prCountScore = Math.Min(40, prs.Count * 5.0); 
                    double mergedPrsCount = prs.Count(p => p.Merged);
                    double mergeRateScore = prs.Any() ? (mergedPrsCount / prs.Count * 40.0) : 0;
                    double avgComments = prs.Any() ? prs.Average(p => p.Comments) : 0;
                    double discussionScore = Math.Min(20, avgComments * 5.0);

                    groupHealth.PRActivityScore = Math.Round(prCountScore + mergeRateScore + discussionScore, 1);
                }
                catch { groupHealth.PRActivityScore = 0; }

                // 维度 4: Issue 管理评估
                try
                {
                    double issueCountScore = Math.Min(40, issues.Count * 4.0);
                    double closedIssues = issues.Count(i => i.State == ItemState.Closed);
                    double resolutionRateScore = issues.Any() ? (closedIssues / issues.Count * 60.0) : 0;
                    groupHealth.IssueScore = Math.Round(issueCountScore + resolutionRateScore, 1);
                }
                catch { groupHealth.IssueScore = 0; }

                // 维度 5: 代码审核质量
                try
                {
                    if (prs.Any())
                    {
                        int reviewedPrs = 0;
                        int approvedPrs = 0;
                        int totalPeerComments = 0;

                        var recentPrs = prs.Take(10).ToList();
                        foreach (var pr in recentPrs)
                        {
                            var reviews = await github.PullRequest.Review.GetAll(owner, repo, pr.Number);
                            if (reviews.Any())
                            {
                                reviewedPrs++;
                                if (reviews.Any(r => r.State == PullRequestReviewState.Approved && r.User.Login != pr.User.Login))
                                    approvedPrs++;
                                
                                var reviewComments = await github.PullRequest.ReviewComment.GetAll(owner, repo, pr.Number);
                                totalPeerComments += reviewComments.Count(c => c.User.Login != pr.User.Login);
                            }
                        }

                        double reviewCoverageScore = (double)reviewedPrs / recentPrs.Count * 40;
                        double approvalScore = (double)approvedPrs / recentPrs.Count * 30;
                        double peerCommentScore = Math.Min(30, (double)totalPeerComments / recentPrs.Count * 10);

                        groupHealth.CodeReviewScore = Math.Round(reviewCoverageScore + approvalScore + peerCommentScore, 1);
                    }
                }
                catch { groupHealth.CodeReviewScore = 0; }

                groupHealths.Add(groupHealth);

                // 个人明细处理
                foreach (var student in groupStudents)
                {
                    var username = student.GitHubUsername.Trim().ToLower();
                    if (string.IsNullOrEmpty(username))
                    {
                        allRecords.Add(new StudentPerformanceRecord { Student = student, Remarks = "账号未填写" });
                        continue;
                    }

                    var studentCommits = commits.Where(c => c.Author?.Login?.ToLower() == username).ToList();
                    var studentIssuesAssigned = issues.Where(i => i.Assignees.Any(a => a.Login.ToLower() == username)).ToList();
                    var studentIssuesResolved = studentIssuesAssigned.Where(i => i.State == ItemState.Closed).ToList();
                    var studentPrs = prs.Where(p => p.User?.Login?.ToLower() == username).ToList();
                    
                    var data = new IndividualData
                    {
                        CommitsCount = studentCommits.Count,
                        IssuesAssigned = studentIssuesAssigned.Count,
                        IssuesResolved = studentIssuesResolved.Count,
                        PrsParticipated = studentPrs.Count,
                        PrDiscussionCount = studentPrs.Sum(p => p.Comments)
                    };

                    data.IndividualScore = CalculateHealthScore(data);

                    string remark = "";
                    bool isSoloHero = false;
                    if (data.CommitsCount > 0 && commits.Count > 0)
                    {
                        double commitShare = (double)data.CommitsCount / commits.Count;
                        if (commitShare > 0.6 && groupStudents.Count > 2)
                        {
                            isSoloHero = true;
                            remark = $"⚠️单体英雄：包揽了小组 {commitShare:P0} 的提交";
                        }
                    }

                    allRecords.Add(new StudentPerformanceRecord
                    {
                        Student = student,
                        Data = data,
                        Remarks = remark,
                        IsSoloHero = isSoloHero
                    });
                }
            }
            catch (Exception ex)
            {
                groupHealths.Add(groupHealth);
                foreach (var student in groupStudents)
                {
                    allRecords.Add(new StudentPerformanceRecord { Student = student, Remarks = $"抓取异常: {ex.Message}" });
                }
            }
        }

        return (groupHealths, allRecords);
    }

    private (string owner, string repo) ParseOwnerAndRepo(string inputOwner, string inputRepo)
    {
        string owner = inputOwner?.Trim() ?? "";
        string repo = inputRepo?.Trim() ?? "";
        if (repo.Contains("github.com/"))
        {
            var parts = repo.Split(new[] { "github.com/" }, StringSplitOptions.None)[1].Split('/');
            if (parts.Length >= 2) { owner = parts[0]; repo = parts[1]; }
        }
        else if (owner.Contains("github.com/"))
        {
            var parts = owner.Split(new[] { "github.com/" }, StringSplitOptions.None)[1].Split('/');
            if (parts.Length >= 2) { owner = parts[0]; repo = parts[1]; }
        }
        if (repo.EndsWith(".git")) repo = repo.Substring(0, repo.Length - 4);
        return (owner, repo);
    }

    private double CalculateHealthScore(IndividualData data)
    {
        double score = 0;
        score += Math.Min(30, data.CommitsCount * 0.5);
        score += Math.Min(30, data.IssueResolveRate * 0.3);
        score += Math.Min(40, data.PrsParticipated * 5);
        return Math.Round(score, 1);
    }
}
