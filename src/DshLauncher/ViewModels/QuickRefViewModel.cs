using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DshLauncher.ViewModels;

/// <summary>命令/Prompt 速查面板：常用 dsh 命令 + Prompt 模板，一键复制。</summary>
public partial class QuickRefViewModel : ObservableObject
{
    public QuickRefViewModel()
    {
        Commands = new QuickRefGroup("常用命令",
            new("启动 Web 界面", "npx @deepseek-ai/dsh web", "启动 DeepSeek Harness 界面并自动打开浏览器"),
            new("指定端口启动", "npx @deepseek-ai/dsh web --port 4321", "换个端口，避免冲突"),
            new("不自动开浏览器", "npx @deepseek-ai/dsh web --no-open", "只在后台起服务，手动打开"),
            new("查看组合配置树", "npx @deepseek-ai/dsh --dump-config", "诊断页「查看组合配置」也调用它"),
            new("命令行帮助", "npx @deepseek-ai/dsh web --help", "全部启动参数说明"),
            new("版本", "npx @deepseek-ai/dsh web --version", "当前安装版本"),
            new("列出插件", "npx @deepseek-ai/dsh plugin list", "查看已装插件"),
            new("安装插件", "npx @deepseek-ai/dsh plugin install <包名>", "安装一个 DeepSeek Harness 插件"));

        Prompts = new QuickRefGroup("Prompt 模板",
            new("代码评审", "请以资深工程师视角评审以下代码，指出可维护性、安全性、性能问题，并给出修改建议：\n\n[代码]", "输出问题清单 + 建议"),
            new("要点复述", "请用简洁的语言总结以下内容，输出要点列表：\n\n[内容]", "适合长文/会议记录"),
            new("根因定位", "请定位下面报错的根因，给出可复现的修复步骤：\n\n[报错]", "排障专用"),
            new("数据转换", "把下面数据转换成 JSON，字段命名遵循 camelCase：\n\n[数据]", "规范化数据"),
            new("会话导出提示", "把此会话导出为 Markdown，按「你 / 助手 / 工具」分块，工具调用折叠显示。", "配合启动器的会话导出功能"));
    }

    public QuickRefGroup Commands { get; }
    public QuickRefGroup Prompts { get; }

    [ObservableProperty]
    private string _status = string.Empty;

    [RelayCommand]
    private void Copy(QuickRefItem item)
    {
        if (item is null) return;
        try
        {
            System.Windows.Clipboard.SetText(item.Content);
            Status = "已复制：" + item.Name;
        }
        catch
        {
            Status = "复制失败。";
        }
    }
}

/// <summary>速查分组：标题 + 若干条目。</summary>
public sealed class QuickRefGroup
{
    public QuickRefGroup(string title, params QuickRefItem[] items)
    {
        Title = title;
        foreach (var i in items) Items.Add(i);
    }

    public string Title { get; }
    public ObservableCollection<QuickRefItem> Items { get; } = new();
}

/// <summary>单条速查：名称 + 内容(命令/模板) + 说明。</summary>
public sealed class QuickRefItem
{
    public QuickRefItem(string name, string content, string desc = "")
    {
        Name = name;
        Content = content;
        Desc = desc;
    }

    public string Name { get; }
    public string Content { get; }
    public string Desc { get; }
}
