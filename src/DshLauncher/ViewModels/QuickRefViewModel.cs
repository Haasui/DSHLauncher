using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DshLauncher.ViewModels;

/// <summary>命令 / Prompt 速查面板：按分组展示，支持搜索过滤，一键复制。</summary>
public partial class QuickRefViewModel : ObservableObject
{
    public QuickRefViewModel()
    {
        Groups.Add(new QuickRefGroup("启动 / 界面", new[]
        {
            new QuickRefItem("启动 Web 界面", "npx @deepseek-ai/dsh web", "启动 DeepSeek Harness 并自动打开浏览器"),
            new QuickRefItem("指定端口启动", "npx @deepseek-ai/dsh web --port 4321", "换个端口，避免冲突"),
            new QuickRefItem("不自动开浏览器", "npx @deepseek-ai/dsh web --no-open", "只在后台起服务，手动打开"),
            new QuickRefItem("启动器后台拉起（等价）", "npx @deepseek-ai/dsh web --no-open --port 3080", "启动器默认拉起的命令"),
            new QuickRefItem("命令行帮助", "npx @deepseek-ai/dsh web --help", "查看全部启动参数"),
            new QuickRefItem("安装版本", "npx @deepseek-ai/dsh --version", "当前安装的 dsh 版本"),
        }));

        Groups.Add(new QuickRefGroup("插件管理", new[]
        {
            new QuickRefItem("列出插件", "dsh plugin list", "查看已安装插件"),
            new QuickRefItem("安装插件", "dsh plugin install <包名>", "装一个，支持 npm 包或 github:user/repo"),
            new QuickRefItem("更新插件", "dsh plugin update <包名>", "更新指定插件"),
            new QuickRefItem("卸载插件", "dsh plugin remove <包名>", "卸载指定插件"),
            new QuickRefItem("指定 profile 操作", "dsh plugin --profile web install <包名>", "对 web profile 的插件安装"),
        }));

        Groups.Add(new QuickRefGroup("会话 / 工作区", new[]
        {
            new QuickRefItem("列出会话", "dsh session list", "当前所有会话（session.list）"),
            new QuickRefItem("搜索会话", "dsh session search <关键词>", "跨会话检索（session.search）"),
            new QuickRefItem("导出会话", "dsh session export <会话id>", "导出含子代理/图片（session.export）"),
            new QuickRefItem("工作区列表", "dsh workspace list", "工作区 → 会话（workspace.list）"),
        }));

        Groups.Add(new QuickRefGroup("配置 / 诊断", new[]
        {
            new QuickRefItem("查看配置树", "dsh web --dump-config", "完整配置（诊断页也调用它）"),
            new QuickRefItem("查看 host", "dsh host describe", "版本 / 工作目录 / 模型"),
            new QuickRefItem("设置默认模型", "dsh agent-default-model set <provider>/<model>", "图形化配置见「设置」页"),
            new QuickRefItem("查看设置", "dsh settings describe", "settings.describe 只读视图"),
        }));

        Groups.Add(new QuickRefGroup("Prompt 模板", new[]
        {
            new QuickRefItem("代码评审",
                "你是一名资深后端工程师。请以严格的代码评审视角，评审下面这段代码：\n\n要求：\n- 从可维护性、可读性、安全性、性能、错误处理五方面检查\n- 按【严重 / 中 / 低】分级列出问题，每条给出具体位置与修改建议\n- 指出缺失的边界情况与潜在隐患\n- 若涉及并发/资源，说明风险场景\n\n代码：\n[CODE]",
                "分级列出问题 + 建议"),
            new QuickRefItem("要点复述",
                "你是一名专业的文字整理助手。请把下面的内容提炼成要点：\n\n要求：\n- 用编号列表输出，每条一句话，保留关键数据/结论\n- 去除重复与客套话\n- 含数字/时间务必准确\n- 末尾补一句「一句话总结」\n\n内容：\n[CONTENT]",
                "长文 / 会议记录提炼"),
            new QuickRefItem("根因定位",
                "你是一名资深排障专家。请定位下面报错的根因：\n\n要求：\n- 先给出最可能的根因（按概率排序，说明依据）\n- 结合报错栈 / 日志指出关键线索\n- 给出可复现的最小修复步骤\n- 说明如何避免再次发生\n\n报错：\n[ERROR]",
                "排障专用"),
            new QuickRefItem("Bug 修复",
                "你是一名资深研发。请定位并修复下面的 bug：\n\n要求：\n- 说明根因（为什么错）\n- 给出修改后的代码（或 diff），并解释改动\n- 指出受影响的其它调用点\n- 给出验证方式 / 测试用例\n\n代码 & Bug 描述：\n[CODE + BUG]",
                "带根因解释与改动"),
            new QuickRefItem("写单元测试",
                "你是一名测试工程师。请为下面的代码补充单元测试（xUnit）：\n\n要求：\n- 覆盖：正常路径 / 边界值 / 异常输入 / 空值\n- 用 Arrange-Act-Assert 结构\n- 测试用例命名清晰（Given_When_Then）\n- 指出缺少测试的分支\n- 依赖外部服务时说明如何 mock\n\n代码：\n[CODE]",
                "提升覆盖率"),
            new QuickRefItem("数据转换",
                "你是一名数据处理专家。请把下面的数据转换成 JSON：\n\n要求：\n- 字段命名严格 camelCase\n- 保持原始类型（数字不要转字符串）\n- 数组 / 嵌套结构保持层级\n- 转换前先列出字段映射，再输出 JSON\n- JSON 必须可被 JSON.parse 直接解析（无注释 / 尾逗号）\n\n原始数据：\n[DATA]",
                "规范化数据"),
            new QuickRefItem("生成 README",
                "你是一名开源项目文档工程师。请为下面的项目生成一份 README：\n\n要求包含章节：\n- 项目简介（一句话 + 一段说明）\n- 功能特性（列表）\n- 安装（命令 + 前置要求）\n- 使用（主要用法 + 示例）\n- 配置项说明（若有）\n- 常见问题（FAQ，3-5 条）\n- License\n语言用中文，简洁清晰。\n\n项目说明：\n[PROJECT]",
                "开源项目文档"),
            new QuickRefItem("需求 → 方案",
                "你是一名资深架构师。请把下面的需求拆解成实现方案：\n\n要求：\n- 拆成步骤清单（数据流 → 接口 → 落地）\n- 每步说明「做什么 + 为什么」\n- 指出关键接口 / 模块划分\n- 标注风险点与依赖\n- 给出验收标准（怎么算完成）\n\n需求：\n[REQUIREMENT]",
                "开发前规划"),
            new QuickRefItem("正则 / 解析",
                "你是一名文本处理专家。请写一个正则（或解析规则）来匹配下面的样例：\n\n要求：\n- 给出正则表达式 + 逐部分解释\n- 给出 3 个「应匹配」和 3 个「不匹配」的测试用例\n- 说明如何捕获分组\n- 若正则不适合，改用手写解析给出伪代码\n\n输入样例：\n[SAMPLES]",
                "文本抽取"),
            new QuickRefItem("SQL 生成",
                "你是一名数据工程师。请把下面的业务描述转成 SQL：\n\n要求：\n- 给出可执行的 SQL（优先 PostgreSQL / MySQL 兼容写法）\n- 说明表结构假设（若无，给出合理的建表语句）\n- 考虑索引与性能\n- 给出关键查询的解释\n\n业务说明：\n[DESC]",
                "拿描述写查询"),
            new QuickRefItem("翻译润色",
                "你是一名翻译润色专家。请把下面的文字翻译并润色（按提示给目标语言）：\n\n要求：\n- 忠实原意，用词地道自然\n- 保持原文语气（正式 / 口语）\n- 专有名词保留英文\n- 输出译文；若有更佳译法用括号标注\n\n原文：\n[TEXT]",
                "中英互译"),
            new QuickRefItem("会议纪要",
                "你是一名会议记录员。请把下面的会议讨论整理成纪要：\n\n要求：\n- 按「结论 / 决定 / 待办 / 负责人 / 截止时间」结构化输出\n- 保留争议与未决事项\n- 语言简洁，避免流水账\n\n讨论内容：\n[MEETING]",
                "讨论转纪要"),
        }));
        RefreshFilter();
    }

    public ObservableCollection<QuickRefGroup> Groups { get; } = new();
    public ObservableCollection<QuickRefGroup> FilteredGroups { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;
    partial void OnSearchQueryChanged(string value) => RefreshFilter();

    [ObservableProperty]
    private string _status = string.Empty;

    private void RefreshFilter()
    {
        FilteredGroups.Clear();
        var q = (SearchQuery ?? "").Trim();
        foreach (var g in Groups)
        {
            var items = q.Length == 0 ? g.Items
                : new ObservableCollection<QuickRefItem>(g.Items.Where(i =>
                    i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || i.Desc.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || i.Content.Contains(q, StringComparison.OrdinalIgnoreCase)));
            if (items.Count == 0) continue;
            FilteredGroups.Add(new QuickRefGroup(g.Title, items));
        }
    }

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
    public QuickRefGroup(string title, IEnumerable<QuickRefItem> items)
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
