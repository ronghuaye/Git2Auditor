using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Git2Auditor.Models;
using Git2Auditor.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Git2Auditor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IGitHubApiService _gitHubService;
    private readonly string _dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_data.json");

    [ObservableProperty] private string pat = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "就绪";
    
    [ObservableProperty] private ObservableCollection<StudentInfo> students = new();
    [ObservableProperty] private ObservableCollection<GroupRepoConfig> groupConfigs = new();
    
    // 小组宏观健康度数据
    [ObservableProperty] private ObservableCollection<GroupHealthData> groupHealthList = new();

    // 个人明细数据 (所有学生)
    public List<StudentPerformanceRecord> AllRecords { get; } = new();
    [ObservableProperty] private ObservableCollection<StudentPerformanceRecord> displayedRecords = new();

    private GroupHealthData? _selectedGroup;
    public GroupHealthData? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                // 仅作为状态通知，表格高亮由 UI Converter 处理
            }
        }
    }

    [ObservableProperty] private PolarAxis[] radiusAxes = 
    {
        new PolarAxis 
        { 
            LabelsRotation = 0,
            TextSize = 10,
            LabelsPaint = new SolidColorPaint(SKColors.DodgerBlue) { FontFamily = "Microsoft YaHei" },
            SeparatorsPaint = new SolidColorPaint(new SKColor(60, 60, 60)) { StrokeThickness = 0.5f },
            MinLimit = 0,
            MaxLimit = 100
        }
    };

    [ObservableProperty] private PolarAxis[] radarAxes = 
    {
        new PolarAxis 
        { 
            Labels = new[] { "CI/CD得分", "PR活跃", "代码审核", "文档", "Issue" },
            TextSize = 12,
            LabelsPaint = new SolidColorPaint(SKColors.DodgerBlue)
            {
                FontFamily = "Microsoft YaHei",
                SKFontStyle = new SKFontStyle(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            },
            SeparatorsPaint = new SolidColorPaint(new SKColor(60, 60, 60)) { StrokeThickness = 1 }
        }
    };

    [ObservableProperty] private SolidColorPaint tooltipTextPaint = new SolidColorPaint(SKColors.White)
    {
        FontFamily = "Microsoft YaHei"
    };

    public MainViewModel(IGitHubApiService gitHubService)
    {
        _gitHubService = gitHubService;
        LoadLocalData();
        _selectedGroup = null;
        UpdateDisplayedRecords(); 
    }

    private void LoadLocalData()
    {
        try
        {
            if (File.Exists(_dataPath))
            {
                var json = File.ReadAllText(_dataPath);
                var data = JsonSerializer.Deserialize<AppData>(json);
                if (data != null)
                {
                    Pat = data.Pat ?? string.Empty;
                    if (data.GroupConfigs != null)
                    {
                        GroupConfigs.Clear();
                        foreach (var c in data.GroupConfigs) GroupConfigs.Add(c);
                    }
                    
                    Students.Clear();
                    foreach (var s in data.Students) Students.Add(s);
                    
                    AllRecords.Clear();
                    if (data.Records != null) AllRecords.AddRange(data.Records);
                    
                    GroupHealthList.Clear();
                    if (data.GroupHealths != null)
                    {
                        foreach (var g in data.GroupHealths) GroupHealthList.Add(g);
                    }
                    
                    StatusMessage = $"已加载本地存档：{Students.Count} 名学生。";
                }
            }
        }
        catch { /* 忽略异常 */ }
    }

    private void SaveLocalData()
    {
        try
        {
            var data = new AppData
            {
                Pat = Pat,
                GroupConfigs = GroupConfigs.ToList(),
                Students = Students.ToList(),
                Records = AllRecords,
                GroupHealths = GroupHealthList.ToList()
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_dataPath, json);
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存本地存档失败: {ex.Message}";
        }
    }

    private void UpdateDisplayedRecords()
    {
        DisplayedRecords.Clear();
        foreach (var r in AllRecords.OrderBy(r => r.Student.GroupId).ThenByDescending(r => r.Student.IsLeader))
        {
            DisplayedRecords.Add(r);
        }
    }

    [RelayCommand]
    private void ImportStudents()
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            Title = "选择学生名单 Excel 文件"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                using var workbook = new XLWorkbook(openFileDialog.FileName);
                var worksheet = workbook.Worksheet(1);
                var rows = worksheet.RangeUsed().RowsUsed().Skip(1); 

                Students.Clear();
                AllRecords.Clear(); 
                foreach (var row in rows)
                {
                    string studentId = row.Cell(1).Value.ToString().Trim();
                    string name = row.Cell(2).Value.ToString().Trim();
                    if (string.IsNullOrWhiteSpace(studentId) && string.IsNullOrWhiteSpace(name)) continue;

                    int.TryParse(row.Cell(5).Value.ToString(), out int groupId);

                    var student = new StudentInfo
                    {
                        StudentId = studentId,
                        Name = name,
                        IsLeader = row.Cell(3).Value.ToString().Trim() == "是",
                        GitHubUsername = row.Cell(4).Value.ToString().Trim(),
                        GroupId = groupId == 0 ? 1 : groupId
                    };
                    Students.Add(student);
                    
                    AllRecords.Add(new StudentPerformanceRecord
                    {
                        Student = student,
                        Data = new IndividualData()
                    });
                }
                
                var groupIds = Students.Select(s => s.GroupId).Distinct().OrderBy(g => g).ToList();
                var existingConfigs = GroupConfigs.ToDictionary(c => c.GroupId, c => c);
                GroupConfigs.Clear();
                GroupHealthList.Clear();
                foreach (var gid in groupIds)
                {
                    if (existingConfigs.TryGetValue(gid, out var config))
                        GroupConfigs.Add(config);
                    else
                        GroupConfigs.Add(new GroupRepoConfig { GroupId = gid });
                    
                    GroupHealthList.Add(new GroupHealthData { GroupId = gid });
                }

                SaveLocalData();
                UpdateDisplayedRecords(); 
                StatusMessage = $"成功导入 {Students.Count} 名学生，存档已更新。";
            }
            catch (Exception ex)
            {
                StatusMessage = $"导入失败: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    private async Task FetchDataAsync()
    {
        if (!Students.Any()) { StatusMessage = "错误：请先导入学生名单。"; return; }
        if (string.IsNullOrWhiteSpace(Pat)) { StatusMessage = "错误：请填写 GitHub PAT。"; return; }

        IsBusy = true;
        StatusMessage = "正在并发抓取 GitHub 协作数据及架构指标...";
        
        try
        {
            var (groupHealths, individualRecords) = await _gitHubService.FetchAndAggregateDataAsync(Pat, GroupConfigs.ToList(), Students.ToList());
            
            if (individualRecords.Any())
            {
                AllRecords.Clear();
                AllRecords.AddRange(individualRecords);
                
                GroupHealthList.Clear();
                foreach (var gh in groupHealths) GroupHealthList.Add(gh);

                SaveLocalData();
                UpdateDisplayedRecords(); 
                StatusMessage = $"数据抓取完成，共处理 {individualRecords.Count} 条记录与 {groupHealths.Count} 个仓库指标。";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"抓取失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        if (!AllRecords.Any()) { StatusMessage = "警告：无数据可导出。"; return; }

        var saveFileDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            Title = "导出协作数据台账",
            FileName = $"协作与架构台账_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            IsBusy = true;
            try
            {
                var filePath = saveFileDialog.FileName;
                await Task.Run(() =>
                {
                    using var workbook = new XLWorkbook();
                    
                    // Sheet 1: 小组宏观健康度
                    var sheetGroup = workbook.Worksheets.Add("小组整体架构监控");
                    string[] gHeaders = { "组别", "仓库 Owner", "仓库 Name", "CI/CD得分", "PR活跃", "代码审核", "文档完善度", "Issue管理分", "综合健康度" };
                    for (int i = 0; i < gHeaders.Length; i++)
                    {
                        var cell = sheetGroup.Cell(1, i + 1);
                        cell.Value = gHeaders[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#4A0000");
                        cell.Style.Font.FontColor = XLColor.White;
                    }
                    
                    int gRow = 2;
                    foreach(var g in GroupHealthList)
                    {
                        sheetGroup.Cell(gRow, 1).Value = g.GroupId;
                        sheetGroup.Cell(gRow, 2).Value = g.RepoOwner;
                        sheetGroup.Cell(gRow, 3).Value = g.RepoName;
                        sheetGroup.Cell(gRow, 4).Value = g.CICDScore;
                        sheetGroup.Cell(gRow, 5).Value = g.PRActivityScore;
                        sheetGroup.Cell(gRow, 6).Value = g.CodeReviewScore;
                        sheetGroup.Cell(gRow, 7).Value = g.DocumentationScore;
                        sheetGroup.Cell(gRow, 8).Value = g.IssueScore;
                        sheetGroup.Cell(gRow, 9).Value = g.GroupHealthScore;
                        gRow++;
                    }

                    // Sheet 2: 个人明细
                    var sheetInd = workbook.Worksheets.Add("个人协作明细");
                    string[] iHeaders = { "组别", "角色", "学号", "姓名", "GitHubID", "Commits", "分配Issue", "解决Issue", "解决率", "参与PR", "PR沟通", "个人得分", "预警", "备注" };
                    for (int i = 0; i < iHeaders.Length; i++)
                    {
                        var cell = sheetInd.Cell(1, i + 1);
                        cell.Value = iHeaders[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#007ACC");
                        cell.Style.Font.FontColor = XLColor.White;
                    }

                    int iRow = 2;
                    foreach (var r in AllRecords)
                    {
                        sheetInd.Cell(iRow, 1).Value = r.Student.GroupId;
                        sheetInd.Cell(iRow, 2).Value = r.Student.IsLeader ? "组长" : "组员";
                        sheetInd.Cell(iRow, 3).Value = r.Student.StudentId;
                        sheetInd.Cell(iRow, 4).Value = r.Student.Name;
                        sheetInd.Cell(iRow, 5).Value = r.Student.GitHubUsername;
                        sheetInd.Cell(iRow, 6).Value = r.Data.CommitsCount;
                        sheetInd.Cell(iRow, 7).Value = r.Data.IssuesAssigned;
                        sheetInd.Cell(iRow, 8).Value = r.Data.IssuesResolved;
                        sheetInd.Cell(iRow, 9).Value = r.Data.IssueResolveRate / 100.0;
                        sheetInd.Cell(iRow, 10).Value = r.Data.PrsParticipated;
                        sheetInd.Cell(iRow, 11).Value = r.Data.PrDiscussionCount;
                        sheetInd.Cell(iRow, 12).Value = r.Data.IndividualScore;
                        sheetInd.Cell(iRow, 13).Value = r.IsSoloHero ? "⚠️高危" : "";
                        sheetInd.Cell(iRow, 14).Value = r.Remarks;
                        iRow++;
                    }

                    sheetGroup.Columns().AdjustToContents();
                    sheetInd.Columns().AdjustToContents();
                    workbook.SaveAs(filePath);
                });
                StatusMessage = "台账导出成功！";
            }
            catch (Exception ex) { StatusMessage = $"导出失败: {ex.Message}"; }
            finally { IsBusy = false; }
        }
    }

    public class AppData
    {
        public string Pat { get; set; } = string.Empty;
        public List<GroupRepoConfig> GroupConfigs { get; set; } = new();
        public List<StudentInfo> Students { get; set; } = new();
        public List<StudentPerformanceRecord> Records { get; set; } = new();
        public List<GroupHealthData> GroupHealths { get; set; } = new();
    }
}
