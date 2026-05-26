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

        // 为了保证所有学生都能显示在表格中，按组别循环之前，先确保每个学生都有一个默认记录
        foreach (var config in groupConfigs)
        {
            var groupStudents = students.Where(s => s.GroupId == config.GroupId).ToList();
            if (!groupStudents.Any()) continue;

            var (owner, repo) = ParseOwnerAndRepo(config.RepoOwner, config.RepoName);

            // 1. 仓库未配置检查
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                foreach (var student in groupStudents)
                {
                    allRecords.Add(new StudentPerformanceRecord
                    {
                        Student = student,
                        Data = new CollaborationData(),
                        Remarks = "仓库地址未配置"
                    });
                }
                continue;
            }

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
                    
                    // 2. 账号未填写检查
                    if (string.IsNullOrEmpty(username))
                    {
                        allRecords.Add(new StudentPerformanceRecord
                        {
                            Student = student,
                            Data = new CollaborationData(),
                            Remarks = "GitHub账号未填写"
                        });
                        continue;
                    }

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

                    // 3. 如果数据全为0，提示可能是账号错误
                    string remark = "";
                    if (data.CommitsCount == 0 && data.IssuesAssigned == 0 && data.PrsParticipated == 0)
                    {
                        remark = "无活动记录，请核对账号是否正确";
                    }

                    allRecords.Add(new StudentPerformanceRecord
                    {
                        Student = student,
                        Data = data,
                        Remarks = remark
                    });
                }
            }
            catch (Exception ex)
            {
                // 4. API 异常处理（如仓库名错误，404等）
                string errorMsg = ex.Message.Length > 30 ? ex.Message.Substring(0, 30) + "..." : ex.Message;
                foreach (var student in groupStudents)
                {
                    allRecords.Add(new StudentPerformanceRecord
                    {
                        Student = student,
                        Data = new CollaborationData(),
                        Remarks = $"抓取失败: {errorMsg}"
                    });
                }
            }
        }

        // 把没有被包含在配置中的零散学生也加进去（如果有的话）
        var processedStudentIds = allRecords.Select(r => r.Student.StudentId).ToHashSet();
        var unprocessedStudents = students.Where(s => !processedStudentIds.Contains(s.StudentId));
        foreach (var student in unprocessedStudents)
        {
             allRecords.Add(new StudentPerformanceRecord
             {
                 Student = student,
                 Data = new CollaborationData(),
                 Remarks = "未找到所属小组的仓库配置"
             });
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
