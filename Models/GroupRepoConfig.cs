using CommunityToolkit.Mvvm.ComponentModel;

namespace Git2Auditor.Models;

public partial class GroupRepoConfig : ObservableObject
{
    [ObservableProperty] private int groupId;
    [ObservableProperty] private string repoOwner = string.Empty;
    [ObservableProperty] private string repoName = string.Empty;
}
