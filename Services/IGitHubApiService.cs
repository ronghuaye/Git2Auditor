using System.Collections.Generic;
using System.Threading.Tasks;
using Git2Auditor.Models;

namespace Git2Auditor.Services;

public interface IGitHubApiService
{
    Task<List<StudentPerformanceRecord>> FetchAndAggregateDataAsync(string pat, List<GroupRepoConfig> groupConfigs, List<StudentInfo> students);
}
