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
    [ObservableProperty] private ObservableCollection<StudentPerformanceRecord> records = new();
    [ObservableProperty] private ObservableCollection<GroupRepoConfig> groupConfigs = new();

    [ObservableProperty] private PolarAxis[] radiusAxes = 
    {
        new PolarAxis 
        { 
            LabelsRotation = 0,
            TextSize = 13,
            LabelsPaint = new SolidColorPaint(SKColors.DodgerBlue)
            {
                FontFamily = "Microsoft YaHei"
            },
            SeparatorsPaint = new SolidColorPaint(new SKColor(60, 60, 60)) { StrokeThickness = 0.5f },
            MinLimit = 0,
            MaxLimit = 100
        }
    };

    [ObservableProperty] private ISeries[] radarSeries = Array.Empty<ISeries>();
    [ObservableProperty] private PolarAxis[] radarAxes = 
    {
        new PolarAxis 
        { 
            Labels = new[] { "代码提交", "Issue参与", "Issue解决率", "PR贡献", "PR沟通" },
            TextSize = 15,
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

    private StudentPerformanceRecord? _selectedRecord;
    public StudentPerformanceRecord? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (SetProperty(ref _selectedRecord, value))
            {
                UpdateRadarChart(value);
            }
        }
    }

    public MainViewModel(IGitHubApiService gitHubService)
    {
        _gitHubService = gitHubService;
        
        // 初始默认配置
        for (int i = 1; i <= 4; i++)
        {
            GroupConfigs.Add(new GroupRepoConfig { GroupId = i });
        }

        LoadLocalData();
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
                    if (data.GroupConfigs != null && data.GroupConfigs.Any())
                    {
                        GroupConfigs.Clear();
                        foreach (var c in data.GroupConfigs) GroupConfigs.Add(c);
                    }
                    
                    Students.Clear();
                    foreach (var s in data.Students) Students.Add(s);
                    
                    Records.Clear();
                    foreach (var r in data.Records) Records.Add(r);
                    
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
                Records = Records.ToList()
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_dataPath, json);
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存本地存档失败: {ex.Message}";
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
                Records.Clear(); 
                foreach (var row in rows)
                {
                    var student = new StudentInfo
                    {
                        StudentId = row.Cell(1).GetValue<string>(),
                        Name = row.Cell(2).GetValue<string>(),
                        IsLeader = row.Cell(3).GetValue<string>() == "是",
                        GitHubUsername = row.Cell(4).GetValue<string>(),
                        GroupId = row.Cell(5).GetValue<int>()
                    };
                    Students.Add(student);
                    
                    Records.Add(new StudentPerformanceRecord
                    {
                        Student = student,
                        Data = new CollaborationData()
                    });
                }
                SaveLocalData();
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
        if (!Students.Any())
        {
            StatusMessage = "错误：请先导入学生名单。";
            return;
        }

        if (string.IsNullOrWhiteSpace(Pat))
        {
            StatusMessage = "错误：请填写 GitHub PAT。";
            return;
        }

        IsBusy = true;
        StatusMessage = "正在并发抓取 4 个小组的 GitHub 协作数据...";
        
        try
        {
            var data = await _gitHubService.FetchAndAggregateDataAsync(Pat, GroupConfigs.ToList(), Students.ToList());
            
            if (data.Any())
            {
                Records.Clear();
                foreach (var item in data)
                {
                    Records.Add(item);
                }
                SaveLocalData();
                StatusMessage = $"数据抓取完成，共处理 {data.Count} 条活跃记录。";
                SelectedRecord = Records.FirstOrDefault();
            }
            else
            {
                StatusMessage = "抓取完成，但未发现匹配的协作数据。请检查 GitHub 账号是否正确。";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"抓取失败！具体原因：\n{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateRadarChart(StudentPerformanceRecord? record)
    {
        if (record == null)
        {
            RadarSeries = Array.Empty<ISeries>();
            return;
        }

        // 统一缩放处理（归一化到 0-100）
        RadarSeries = new ISeries[]
        {
            new PolarLineSeries<double>
            {
                Values = new double[] 
                { 
                    Math.Min(100, record.Data.CommitsCount * 5), 
                    Math.Min(100, record.Data.IssuesAssigned * 10), 
                    record.Data.IssueResolveRate, 
                    Math.Min(100, record.Data.PrsParticipated * 20), 
                    Math.Min(100, record.Data.PrDiscussionCount * 10) 
                },
                Name = record.Student.Name,
                Stroke = new SolidColorPaint(SKColors.DodgerBlue) { StrokeThickness = 3 },
                Fill = new SolidColorPaint(SKColors.DodgerBlue.WithAlpha(40)),
                GeometrySize = 6,
                GeometryFill = new SolidColorPaint(SKColors.DodgerBlue),
                GeometryStroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 1 }
            }
        };
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        if (!Records.Any())
        {
            StatusMessage = "警告：当前没有可导出的数据，请先执行“并发抓取”。";
            return;
        }

        var saveFileDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            Title = "导出协作数据台账",
            FileName = $"协作台账_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            IsBusy = true;
            StatusMessage = "正在生成 Excel 台账并处理样式...";

            try
            {
                var filePath = saveFileDialog.FileName;
                await Task.Run(() =>
                {
                    using var workbook = new XLWorkbook();
                    var sheet = workbook.Worksheets.Add("开源协作数据台账");

                    // 1. 设置表头
                    string[] headers = { "组别", "角色", "学号", "姓名", "GitHub账号", "Commit总数", "分派Issue", "解决Issue", "Issue解决率(%)", "参与PR", "PR讨论数", "健康度" };
                    for (int i = 0; i < headers.Length; i++)
                    {
                        var cell = sheet.Cell(1, i + 1);
                        cell.Value = headers[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#007ACC");
                        cell.Style.Font.FontColor = XLColor.White;
                    }

                    // 2. 写入数据
                    int row = 2;
                    foreach (var r in Records)
                    {
                        sheet.Cell(row, 1).Value = r.Student.GroupId;
                        sheet.Cell(row, 2).Value = r.Student.IsLeader ? "组长" : "组员";
                        sheet.Cell(row, 3).Value = r.Student.StudentId;
                        sheet.Cell(row, 4).Value = r.Student.Name;
                        sheet.Cell(row, 5).Value = r.Student.GitHubUsername;
                        sheet.Cell(row, 6).Value = r.Data.CommitsCount;
                        sheet.Cell(row, 7).Value = r.Data.IssuesAssigned;
                        sheet.Cell(row, 8).Value = r.Data.IssuesResolved;
                        sheet.Cell(row, 9).Value = r.Data.IssueResolveRate / 100.0;
                        sheet.Cell(row, 9).Style.NumberFormat.Format = "0.00%";
                        sheet.Cell(row, 10).Value = r.Data.PrsParticipated;
                        sheet.Cell(row, 11).Value = r.Data.PrDiscussionCount;
                        sheet.Cell(row, 12).Value = r.Data.HealthScore;
                        row++;
                    }

                    // 3. 表格美化
                    sheet.Columns().AdjustToContents();
                    sheet.SheetView.FreezeRows(1);
                    
                    workbook.SaveAs(filePath);
                });
                StatusMessage = $"台账导出成功！文件位置：{Path.GetFileName(filePath)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"导出失败：{ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    public class AppData
    {
        public string Pat { get; set; } = string.Empty;
        public List<GroupRepoConfig> GroupConfigs { get; set; } = new();
        public List<StudentInfo> Students { get; set; } = new();
        public List<StudentPerformanceRecord> Records { get; set; } = new();
    }
}
