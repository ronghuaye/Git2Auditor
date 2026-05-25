using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Git2Auditor.Models;
using Octokit;

namespace Git2Auditor.Services;

public class GitHubApiService : IGitHubApiService
{
    public async Task<List<StudentPerformanceRecord>> FetchAndAggregateDataAsync(string pat, List<GroupRepoConfig> groupConfigs, List<StudentInfo> students)
    {
        var github = new GitHubClient(new ProductHeaderValue("Git2Auditor-Agent"))
        {
            Credentials = new Credentials(pat)
        };

        var allRecords = new List<StudentPerformanceRecord>();
        var errors = new List<string>();

        foreach (var config in groupConfigs)
        {
            // 智能解析：如果用户误填了完整 URL，自动提取 Owner 和 Repo
            var (owner, repo) = ParseOwnerAndRepo(config.RepoOwner, config.RepoName);

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
                continue;

            var groupStudents = students.Where(s => s.GroupId == config.GroupId).ToList();
            if (!groupStudents.Any()) continue;

            try
            {
                var commitsTask = github.Repository.Commit.GetAll(owner, repo);
                var issuesTask = github.Issue.GetAllForRepository(owner, repo, new RepositoryIssueRequest { State = ItemStateFilter.All });
                var prsTask = github.PullRequest.GetAllForRepository(owner, repo, new PullRequestRequest { State = ItemStateFilter.All });

                await Task.WhenAll(commitsTask, issuesTask, prsTask);

                var commits = commitsTask.Result;
                var issues = issuesTask.Result.Where(i => i.PullRequest == null).ToList();
                var prs = prsTask.Result;

                foreach (var student in groupStudents)
                {
                    var username = student.GitHubUsername.Trim().ToLower();
                    if (string.IsNullOrEmpty(username)) continue;

                    var studentCommits = commits.Where(c => c.Author?.Login?.ToLower() == username).ToList();
                    var studentIssuesAssigned = issues.Where(i => i.Assignees.Any(a => a.Login.ToLower() == username)).ToList();
                    var studentIssuesResolved = studentIssuesAssigned.Where(i => i.State == ItemState.Closed).ToList();
                    var studentPrs = prs.Where(p => p.User?.Login?.ToLower() == username).ToList();
                    var mergedPrs = studentPrs.Where(p => p.MergedAt.HasValue).ToList();
                    
                    double avgMergeHours = mergedPrs.Any()
                        ? mergedPrs.Average(p => (p.MergedAt!.Value - p.CreatedAt).TotalHours)
                        : 0;

                    var data = new CollaborationData
                    {
                        CommitsCount = studentCommits.Count,
                        IssuesAssigned = studentIssuesAssigned.Count,
                        IssuesResolved = studentIssuesResolved.Count,
                        PrsParticipated = studentPrs.Count,
                        PrAverageMergeTimeHours = Math.Round(avgMergeHours, 1),
                        PrDiscussionCount = studentPrs.Sum(p => p.Comments)
                    };

                    data.HealthScore = CalculateHealthScore(data);

                    allRecords.Add(new StudentPerformanceRecord
                    {
                        Student = student,
                        Data = data
                    });
                }
            }
            catch (Exception ex)
            {
                errors.Add($"小组 {config.GroupId} ({owner}/{repo}): {ex.Message}");
            }
        }

        if (!allRecords.Any() && errors.Any())
        {
            throw new Exception("所有小组抓取均失败：\n" + string.Join("\n", errors));
        }

        return allRecords
            .OrderBy(r => r.Student.GroupId)
            .ThenByDescending(r => r.Student.IsLeader)
            .ThenByDescending(r => r.Data.HealthScore)
            .ToList();
    }

    /// <summary>
    /// 智能解析器：处理用户输入的各种形态的 Repo 地址
    /// </summary>
    private (string owner, string repo) ParseOwnerAndRepo(string inputOwner, string inputRepo)
    {
        string owner = inputOwner?.Trim() ?? "";
        string repo = inputRepo?.Trim() ?? "";

        // 如果 Repo 框里填的是完整网址
        if (repo.Contains("github.com/"))
        {
            var parts = repo.Split(new[] { "github.com/" }, StringSplitOptions.None)[1].Split('/');
            if (parts.Length >= 2)
            {
                owner = parts[0];
                repo = parts[1];
            }
        }
        // 如果 Owner 框里填的是完整网址
        else if (owner.Contains("github.com/"))
        {
             var parts = owner.Split(new[] { "github.com/" }, StringSplitOptions.None)[1].Split('/');
            if (parts.Length >= 2)
            {
                owner = parts[0];
                repo = parts[1];
            }
        }

        // 去掉末尾的 .git 标识
        if (repo.EndsWith(".git")) repo = repo.Substring(0, repo.Length - 4);

        return (owner, repo);
    }

    private double CalculateHealthScore(CollaborationData data)
    {
        double score = 0;
        score += Math.Min(30, data.CommitsCount * 0.5);
        score += Math.Min(30, data.IssueResolveRate * 0.3);
        score += Math.Min(40, data.PrsParticipated * 5);
        return Math.Round(score, 1);
    }
}
